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
    public class ScheduleRepository : GenericRepository<MaintenanceSchedule>, IScheduleRepository
    {
        private readonly AppDbContext _context;
        private readonly IFileProvider _fileProvider;
        private readonly IMapper _mapper;

        public ScheduleRepository(AppDbContext context, IFileProvider fileProvider, IMapper mapper) : base(context)
        {
            _context = context;
            _fileProvider = fileProvider;
            _mapper = mapper;
        }

        public async Task<bool> AddNewScheduleAsync(CreateMaintenanceScheduleDTO dto)
        {
            var hasIncomplete = await _context.MaintenanceSchedules.AnyAsync(s =>
                s.SensorId == dto.SensorId && (s.Status == null || s.Status != "Completed"));
            if (hasIncomplete)
                return false;

            var schedule = _mapper.Map<MaintenanceSchedule>(dto);

            schedule.ScheduleMode = "Manual";
            schedule.Status = "Scheduled";
            schedule.CreatedAt = DateTime.UtcNow;

            await _context.MaintenanceSchedules.AddAsync(schedule);
            await _context.SaveChangesAsync();
            return true;
        }

        public async Task<bool> AddAutoScheduleAsync(int sensorId)
        {
            var hasIncomplete = await _context.MaintenanceSchedules.AnyAsync(s =>
                s.SensorId == sensorId && (s.Status == null || s.Status != "Completed"));
            if (hasIncomplete)
                return false;

            var schedule = new MaintenanceSchedule
            {
                SensorId = sensorId,
                ScheduleMode = "Auto",
                Status = "Scheduled",
                CreatedAt = DateTime.UtcNow
            };

            await _context.MaintenanceSchedules.AddAsync(schedule);
            await _context.SaveChangesAsync();
            return true;
        }

        public async Task<IEnumerable<MaintenanceSchedule>> GetAllSchedulesAsync(EntityParam entityParam)
        {
            var overdueSchedules = await _context.MaintenanceSchedules
                .Where(s => s.EndDate.HasValue && s.EndDate.Value < DateTime.UtcNow && s.Status != "Completed")
                .ToListAsync();

            foreach (var schedule in overdueSchedules)
            {
                schedule.Status = "Overdue";
            }
            await _context.SaveChangesAsync();
            
            var query = _context.MaintenanceSchedules
                .Include(u => u.Sensor)
                .Include(u => u.AssignedTechnician)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(entityParam.ScheduleStatus))
            {
                query = query.Where(s => s.Status.ToLower() == entityParam.ScheduleStatus.ToLower());
            }

            if (!string.IsNullOrWhiteSpace(entityParam.ScheduleType))
            {
                query = query.Where(s => s.ScheduleType.ToLower() == entityParam.ScheduleType.ToLower());
            }

            if (!string.IsNullOrWhiteSpace(entityParam.ScheduleMode))
            {
                query = query.Where(s => s.ScheduleMode.ToLower() == entityParam.ScheduleMode.ToLower());
            }

            query = query.OrderBy(s => s.Status == "Scheduled" ? 0 : s.Status == "Active" ? 1 : s.Status == "Completed" ? 2 : s.Status == "Paused" ? 3 : s.Status == "Overdue" ? 4 : 5)
                         .ThenByDescending(s => s.CreatedAt)
                         .Skip((entityParam.Pagenumber - 1) * entityParam.Pagesize)
                         .Take(entityParam.Pagesize);

            return await query.ToListAsync();
        }

        public async Task<IEnumerable<MaintenanceSchedule>> GetSchedulesByTechnicianAsync(int technicianId, EntityParam entityParam)
        {
            var overdueSchedules = await _context.MaintenanceSchedules
                .Where(s => s.AssignedTechnicianId == technicianId && s.EndDate.HasValue && s.EndDate.Value < DateTime.UtcNow && s.Status != "Completed")
                .ToListAsync();

            foreach (var schedule in overdueSchedules)
            {
                schedule.Status = "Overdue";
            }
            await _context.SaveChangesAsync();

            var query = _context.MaintenanceSchedules
                .Include(u => u.Sensor)
                .Where(s => s.AssignedTechnicianId == technicianId)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(entityParam.ScheduleStatus))
            {
                query = query.Where(s => s.Status.ToLower() == entityParam.ScheduleStatus.ToLower());
            }

            if (!string.IsNullOrWhiteSpace(entityParam.ScheduleType))
            {
                query = query.Where(s => s.ScheduleType.ToLower() == entityParam.ScheduleType.ToLower());
            }

            query = query.OrderBy(s => s.Status == "Scheduled" ? 0 : s.Status == "Active" ? 1 : s.Status == "Completed" ? 2 : s.Status == "Paused" ? 3 : s.Status == "Overdue" ? 4 : 5)
                         .ThenByDescending(s => s.CreatedAt)
                         .Skip((entityParam.Pagenumber - 1) * entityParam.Pagesize)
                         .Take(entityParam.Pagesize);

            return await query.ToListAsync();
        }

        public async Task<bool> UpdateScheduleAsync(int id, UpdateMaintenanceScheduleDTO dto)
        {
            var schedule = await _context.MaintenanceSchedules.FindAsync(id);
            if (schedule == null) return false;

            if (!string.IsNullOrEmpty(dto.ScheduleType))
                schedule.ScheduleType = dto.ScheduleType;

            if (dto.StartDate.HasValue)
                schedule.StartDate = DateTime.SpecifyKind(dto.StartDate.Value, DateTimeKind.Utc);

            if (dto.EndDate.HasValue)
                schedule.EndDate = DateTime.SpecifyKind(dto.EndDate.Value, DateTimeKind.Utc);

            if (dto.AssignedTechnicianId.HasValue)
                schedule.AssignedTechnicianId = dto.AssignedTechnicianId.Value;

            if (!string.IsNullOrEmpty(dto.Note))
                schedule.Note = dto.Note;

            await _context.SaveChangesAsync();
            return true;
        }

        public async Task<bool> UpdateScheduleStatusAsync(int id, UpdateScheduleStatusDTO dto)
        {
            var schedule = await _context.MaintenanceSchedules.FindAsync(id);
            if (schedule == null) return false;

            // Define valid statuses based on DB schema CHECK constraint
            var validStatuses = new[] { "Scheduled", "Active", "Paused", "Completed", "Overdue" };
            if (!validStatuses.Contains(dto.Status)) return false;

            schedule.Status = dto.Status;
            await _context.SaveChangesAsync();

            // Auto-renew: create next month schedule when Auto schedule is completed
            if (dto.Status == "Completed" && schedule.ScheduleMode == "Auto")
            {
                var hasPending = await _context.MaintenanceSchedules.AnyAsync(s =>
                    s.SensorId == schedule.SensorId && s.ScheduleId != schedule.ScheduleId
                    && s.Status != "Completed" && s.ScheduleMode == "Auto");

                if (!hasPending)
                {
                    var nextStart = schedule.EndDate ?? DateTime.UtcNow;
                    var newSchedule = new MaintenanceSchedule
                    {
                        SensorId = schedule.SensorId,
                        ScheduleType = schedule.ScheduleType,
                        ScheduleMode = "Auto",
                        StartDate = nextStart,
                        EndDate = nextStart.AddMonths(1),
                        AssignedTechnicianId = schedule.AssignedTechnicianId,
                        Status = "Scheduled",
                        CreatedAt = DateTime.UtcNow
                    };
                    await _context.MaintenanceSchedules.AddAsync(newSchedule);
                    await _context.SaveChangesAsync();
                }
            }

            return true;
        }

        public async Task<bool> DeleteScheduleAsync(int id)
        {
            var schedule = await _context.MaintenanceSchedules.FindAsync(id);
            if (schedule == null) return false;

            _context.MaintenanceSchedules.Remove(schedule);
            await _context.SaveChangesAsync();
            return true;
        }

        public async Task<IEnumerable<MaintenanceSchedule>> GetBySensorIdAsync(int sensorId)
        {
            return await _context.MaintenanceSchedules
                .AsSplitQuery()
                .Include(s => s.AssignedTechnician)
                .Include(s => s.Sensor)
                .Where(s => s.SensorId == sensorId)
                .OrderByDescending(s => s.CreatedAt)
                .ToListAsync();
        }

        public async Task<IEnumerable<MaintenanceSchedule>> GetByAssignedTechnicianIdAsync(int technicianId)
        {
            var overdueSchedules = await _context.MaintenanceSchedules
                .Where(s => s.AssignedTechnicianId == technicianId && s.EndDate.HasValue && s.EndDate.Value < DateTime.UtcNow && s.Status != "Completed")
                .ToListAsync();

            foreach (var schedule in overdueSchedules)
            {
                schedule.Status = "Overdue";
            }

            if (overdueSchedules.Count > 0)
                await _context.SaveChangesAsync();

            return await _context.MaintenanceSchedules
                .AsSplitQuery()
                .Include(s => s.Sensor)
                .Include(s => s.AssignedTechnician)
                .Where(s => s.AssignedTechnicianId == technicianId)
                .OrderBy(s => s.Status == "Scheduled" ? 0 : s.Status == "Active" ? 1 : s.Status == "Completed" ? 2 : s.Status == "Paused" ? 3 : s.Status == "Overdue" ? 4 : 5)
                .ThenByDescending(s => s.CreatedAt)
                .ToListAsync();
        }
    }
}
