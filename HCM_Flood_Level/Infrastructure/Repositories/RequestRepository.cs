using AutoMapper;
using Core.DTOs;
using Core.Entities;
using Core.Interfaces;
using Core.Sharing;
using Infrastructure.DBContext;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Infrastructure.Repositories
{
    public class RequestRepository : GenericRepository<MaintenanceRequest>, IRequestRepository
    {
        private readonly AppDbContext _context;
        private readonly IFileProvider _fileProvider;
        private readonly IMapper _mapper;

        public RequestRepository(AppDbContext context, IFileProvider fileProvider, IMapper mapper) : base(context)
        {
            _context = context;
            _fileProvider = fileProvider;
            _mapper = mapper;
        }

        public async Task<bool> StaffCreateRequestAsync(StaffCreateRequestDTO dto)
        {
            // 1. Check if sensor exists
            var sensorExist = await _context.Sensors.AnyAsync(s => s.SensorId == dto.SensorId);
            if (!sensorExist) throw new Exception("Sensor không tồn tại");

            // 2. Check if priority exists
            var priorityExist = await _context.Priorities.AnyAsync(p => p.PriorityId == dto.Priorityid);
            if (!priorityExist) throw new Exception("Độ ưu tiên không tồn tại");

            // 3. Check if technician exists and has the correct role (Technician = 3)
            var technician = await _context.Users.Include(u => u.Role).FirstOrDefaultAsync(u => u.UserId == dto.AssignedTechnicianTo);
            if (technician == null) throw new Exception("Kỹ thuật viên không tồn tại");
            if (technician.Role?.RoleName != "Technician" && technician.RoleId != 3) 
                throw new Exception($"Người dùng {technician.FullName} không có vai trò Kỹ thuật viên (RoleId: {technician.RoleId})");

            var hasIncompleteRequest = await _context.MaintenanceRequests.AnyAsync(r =>
                r.SensorId == dto.SensorId && (r.Status == null || r.Status != "Completed"));
            if (hasIncompleteRequest)
                throw new Exception("Sensor đang có yêu cầu bảo trì chưa hoàn thành. Chỉ được tạo yêu cầu mới khi yêu cầu hiện tại đã Completed.");

            // 4. Map DTO to Entity
            var request = _mapper.Map<MaintenanceRequest>(dto);

            // 5. Set default values
            request.Status = "Pending";
            request.CreatedAt = DateTime.UtcNow;
            
            if (dto.Deadline.HasValue)
            {
                request.Deadline = DateTime.SpecifyKind(dto.Deadline.Value, DateTimeKind.Utc);
            }

            // 6. Add to context and save
            await _context.MaintenanceRequests.AddAsync(request);
            var result = await _context.SaveChangesAsync();
            
            return result > 0;
        }

        public async Task<bool> StaffDeleteRequestAsync(int requestId)
        {
            var request = await _context.MaintenanceRequests.FindAsync(requestId);
            if (request == null) return false;
            if (request.Status != "Completed")
                throw new Exception("Chỉ được xóa yêu cầu khi trạng thái là Completed.");

            _context.MaintenanceRequests.Remove(request);
            return await _context.SaveChangesAsync() > 0;
        }

        public async Task<IEnumerable<MaintenanceRequest>> StaffGetRequestAsync(EntityParam entityParam)
        {
            var query = _context.MaintenanceRequests
                .Include(r => r.Sensor)
                .Include(r => r.Priority)
                .Include(r => r.AssignedTechnician)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(entityParam.RequestStatus))
            {
                query = query.Where(r => r.Status.ToLower() == entityParam.RequestStatus.ToLower());
            }

            if (!string.IsNullOrWhiteSpace(entityParam.RequestPriority))
            {
                query = query.Where(r => r.Priority.DisplayName.ToLower() == entityParam.RequestPriority.ToLower());
            }

            var requests = await query.OrderBy(r => r.Status == "Pending" ? 0 : r.Status == "Assigned" ? 1 : r.Status == "InProgress" ? 2 : r.Status == "Completed" ? 3 : r.Status == "Cancelled" ? 4 : 5)
                .ThenByDescending(s => s.CreatedAt)
                         .Skip((entityParam.Pagenumber - 1) * entityParam.Pagesize)
                         .Take(entityParam.Pagesize)
                         .ToListAsync();

            // Nếu status thực tế là InProgress thì Staff sẽ thấy là Assigned
            foreach (var r in requests)
            {
                if (r.Status == "InProgress")
                {
                    r.Status = "Assigned";
                }
            }

            return requests;
        }

        public async Task<bool> TechnicianUpdateStatusAsync(int requestId, TechnicianUpdateStatusDTO dto)
        {
            var request = await _context.MaintenanceRequests.FindAsync(requestId);

            if (request == null) return false;

            // Nếu chuyển sang InProgress từ Pending
            if (dto.Status == "InProgress" && request.Status == "Pending")
            {
                request.AssignedAt = DateTime.UtcNow;
            }

            // Nếu chuyển sang Completed
            if (dto.Status == "Completed")
            {
                request.ResolvedAt = DateTime.UtcNow;
            }

            request.Status = dto.Status;
            return await _context.SaveChangesAsync() > 0;
        }

        public async Task<IEnumerable<MaintenanceRequest>> TechnicianGetRequestAsync(int technicianId, EntityParam entityParam)
        {
            var query = _context.MaintenanceRequests
                .Include(r => r.Sensor)
                .Include(r => r.Priority)
                .Include(r => r.AssignedTechnician)
                .Where(r => r.AssignedTechnicianTo == technicianId)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(entityParam.RequestStatus))
            {
                query = query.Where(r => r.Status.ToLower() == entityParam.RequestStatus.ToLower());
            }

            if (!string.IsNullOrWhiteSpace(entityParam.RequestPriority))
            {
                query = query.Where(r => r.Priority.DisplayName.ToLower() == entityParam.RequestPriority.ToLower());
            }

            query = query.OrderBy(r => r.Status == "Pending" ? 0 : r.Status == "Assigned" ? 1 : r.Status == "InProgress" ? 2 : r.Status == "Completed" ? 3 : r.Status == "Cancelled" ? 4 : 5)
                .ThenByDescending(s => s.CreatedAt)
                         .Skip((entityParam.Pagenumber - 1) * entityParam.Pagesize)
                         .Take(entityParam.Pagesize);

            return await query.ToListAsync();
        }

        public async Task<IEnumerable<MaintenanceRequest>> GetBySensorIdAsync(int sensorId)
        {
            return await _context.MaintenanceRequests
                .AsSplitQuery()
                .Include(r => r.Sensor)
                .Include(r => r.Priority)
                .Include(r => r.AssignedTechnician)
                .Where(r => r.SensorId == sensorId)
                .OrderByDescending(r => r.CreatedAt)
                .ToListAsync();
        }

        public async Task<IEnumerable<MaintenanceRequest>> GetByAssignedTechnicianIdAsync(int technicianId)
        {
            var requests = await _context.MaintenanceRequests
                .AsNoTracking()
                .AsSplitQuery()
                .Include(r => r.Sensor)
                .Include(r => r.Priority)
                .Include(r => r.AssignedTechnician)
                .Where(r => r.AssignedTechnicianTo == technicianId)
                .OrderBy(r => r.Status == "Pending" ? 0 : r.Status == "Assigned" ? 1 : r.Status == "InProgress" ? 2 : r.Status == "Completed" ? 3 : r.Status == "Cancelled" ? 4 : 5)
                .ThenByDescending(r => r.CreatedAt)
                .ToListAsync();

            foreach (var r in requests)
            {
                if (r.Status == "InProgress")
                    r.Status = "Assigned";
            }

            return requests;
        }
    }
}
