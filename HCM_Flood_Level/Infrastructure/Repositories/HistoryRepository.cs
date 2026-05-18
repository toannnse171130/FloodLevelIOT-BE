using Core.Entities;
using Core.Interfaces;
using Core.Sharing;
using Infrastructure.DBContext;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories
{
    public class HistoryRepository : IHistoryRepository
    {
        private readonly AppDbContext _context;

        public HistoryRepository(AppDbContext context)
        {
            _context = context;
        }

        public async Task<IReadOnlyList<History>> GetAllAsync(EntityParam param)
        {
            var query = _context.Histories.AsQueryable();

            if (!string.IsNullOrEmpty(param.Search))
                query = query.Where(h => h.Severity.ToString().ToLower().Contains(param.Search));

            return await query
                .OrderByDescending(h => h.Severity)
                .ThenByDescending(h => h.CreatedAt)
                .Skip((param.Pagenumber - 1) * param.Pagesize)
                .Take(param.Pagesize)
                .ToListAsync();
        }

        public async Task<IReadOnlyList<History>> GetFilteredAsync(int? placeId, int hours, int limit)
        {
            var query = _context.Histories.AsNoTracking().AsQueryable();

            if (placeId.HasValue && placeId.Value > 0)
                query = query.Where(h => h.LocationId == placeId.Value);

            if (hours > 0)
            {
                var since = DateTime.UtcNow.AddHours(-hours);
                query = query.Where(h => h.CreatedAt >= since || h.StartTime >= since);
            }

            query = query
                .OrderByDescending(h => h.CreatedAt)
                .ThenByDescending(h => h.StartTime);

            if (limit > 0)
                query = query.Take(limit);
            else
                query = query.Take(500);

            return await query.ToListAsync();
        }

        public async Task<int> CountAsync()
            => await _context.Histories.CountAsync();

        public async Task<History> AddAsync(History history)
        {
            await _context.Histories.AddAsync(history);
            await _context.SaveChangesAsync();
            return history;
        }
    }
}
