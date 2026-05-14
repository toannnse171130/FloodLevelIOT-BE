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

            // Search by Severity name when search term is provided
            if (!string.IsNullOrEmpty(param.Search))
                query = query.Where(h => h.Severity.ToString().ToLower().Contains(param.Search));

            return await query
                .OrderByDescending(h => h.Severity)
                .ThenByDescending(h => h.CreatedAt)
                .Skip((param.Pagenumber - 1) * param.Pagesize)
                .Take(param.Pagesize)
                .ToListAsync();
        }

        public async Task<int> CountAsync()
            => await _context.Histories.CountAsync();
    }
}
