using AutoMapper;
using Core.DTOs;
using Core.Entities;
using Core.Interfaces;
using Core.Services;
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
    public class UserRepository : GenericRepository<User>, IUserRepository
    {
        private readonly AppDbContext _context;
        private readonly IFileProvider _fileProvider;
        private readonly IMapper _mapper;

        public UserRepository(AppDbContext context, IFileProvider fileProvider, IMapper mapper) : base(context)
        {
            _context = context;
            _fileProvider = fileProvider;
            _mapper = mapper;
        }

        public async Task<bool> AddNewStaffAsync(CreateUserDTO dto)
        {
            var acc = _mapper.Map<User>(dto);
            
            if (!string.IsNullOrEmpty(dto.Password))
            {
                acc.PasswordHash = PasswordHelper.HashPassword(dto.Password);
            }
            
            acc.RoleId = 2;
            acc.IsActive = true;
            acc.CreatedAt = DateTime.UtcNow;

            await _context.Users.AddAsync(acc);
            await _context.SaveChangesAsync();
            return true;
        }

        public async Task<StaffDeleteUserResult> DeleteStaffAsync(int id)
        {
            var currentUser = await _context.Users
                .Include(u => u.Role)
                .FirstOrDefaultAsync(u => u.UserId == id);

            if (currentUser == null)
                return StaffDeleteUserResult.UserNotFound;

            if (!string.Equals(currentUser.Role?.RoleName, "Technician", StringComparison.OrdinalIgnoreCase))
                return StaffDeleteUserResult.TargetNotTechnician;

            const string completed = "Completed";

            var hasIncompleteRequests = await _context.MaintenanceRequests
                .AnyAsync(r => r.AssignedTechnicianTo == id && r.Status != completed);

            var hasIncompleteSchedules = await _context.MaintenanceSchedules
                .AnyAsync(s => s.AssignedTechnicianId == id && (s.Status == null || s.Status != completed));

            if (hasIncompleteRequests || hasIncompleteSchedules)
                return StaffDeleteUserResult.TechnicianHasIncompleteWork;

            _context.Users.Remove(currentUser);
            await _context.SaveChangesAsync();
            return StaffDeleteUserResult.Success;
        }

        public async Task<IEnumerable<User>> GetAllUserAsync(EntityParam entityParam)
        {
            var query = _context.Users
                .Include(u => u.Role)
                .Where(u => u.RoleId != 1) // Exclude users with RoleId = 1
                .AsQueryable();

            if (!string.IsNullOrEmpty(entityParam.Search))
            {
                query = query.Where(u =>
                    u.FullName.ToLower().Contains(entityParam.Search) ||
                    u.PhoneNumber.ToLower().Contains(entityParam.Search) ||
                    u.Email.ToLower().Contains(entityParam.Search));
            }

            if (entityParam.RoleId.HasValue)
            {
                query = query.Where(u => u.RoleId == entityParam.RoleId.Value);
            }

            query = query.OrderBy(u => u.FullName)
                         .Skip((entityParam.Pagenumber - 1) * entityParam.Pagesize)
                         .Take(entityParam.Pagesize);

            return await query.ToListAsync();
        }

        public async Task<int> CountUserAsync(EntityParam entityParam)
        {
            var query = _context.Users
                .Where(u => u.RoleId != 1)
                .AsQueryable();

            if (!string.IsNullOrEmpty(entityParam.Search))
            {
                query = query.Where(u =>
                    u.FullName.ToLower().Contains(entityParam.Search) ||
                    u.PhoneNumber.ToLower().Contains(entityParam.Search) ||
                    u.Email.ToLower().Contains(entityParam.Search));
            }

            if (entityParam.RoleId.HasValue)
            {
                query = query.Where(u => u.RoleId == entityParam.RoleId.Value);
            }

            return await query.CountAsync();
        }

        public async Task<bool> UpdateProfileAsync(int id, UpdateProfileDTO dto)
        {
            var currentUser = _context.Users.Find(id);
            if (currentUser == null) return false;
            if (!string.IsNullOrEmpty(dto.FullName))
                currentUser.FullName = dto.FullName;
            if (!string.IsNullOrEmpty(dto.PhoneNumber))
                currentUser.PhoneNumber = dto.PhoneNumber;
            await _context.SaveChangesAsync();
            return true;
        }

        public async Task<bool> UpdateStaffAsync(int id, UpdateUserDTO dto)
        {
            var currentUser = await _context.Users.FindAsync(id);

            if (currentUser == null)
                return false;

            // Only update fields that are provided (partial update)
            if (dto.RoleId.HasValue)
                currentUser.RoleId = dto.RoleId.Value;

            if (dto.Status.HasValue)
                currentUser.IsActive = dto.Status.Value;

            await _context.SaveChangesAsync();
            return true;
        }
    }
}
