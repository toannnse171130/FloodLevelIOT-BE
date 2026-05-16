using Core.Entities;
using Core.Interfaces;
using Core.Sharing;
using Infrastructure.DBContext;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories
{
    public class SensorReadingRepository : ISensorReadingRepository
    {
        private readonly AppDbContext _context;

        public SensorReadingRepository(AppDbContext context)
        {
            _context = context;
        }

        public async Task AddAsync(SensorReading reading)
        {
            await _context.SensorReadings.AddAsync(reading);
            await _context.SaveChangesAsync();
        }

        public async Task<IReadOnlyList<SensorReading>> GetAllAsync(EntityParam param)
        {
            var query = _context.SensorReadings.AsQueryable();

            if (param.SensorId.HasValue)
                query = query.Where(r => r.SensorId == param.SensorId.Value);

            if (param.StartDate.HasValue)
                query = query.Where(r => r.RecordedAt >= param.StartDate.Value);

            if (param.EndDate.HasValue)
                query = query.Where(r => r.RecordedAt <= param.EndDate.Value);

            return await query
                .OrderByDescending(r => r.RecordedAt)
                .Skip((param.Pagenumber - 1) * param.Pagesize)
                .Take(param.Pagesize)
                .ToListAsync();
        }

        public async Task<int> CountAsync(int? sensorId = null, DateTime? startDate = null, DateTime? endDate = null)
        {
            var query = _context.SensorReadings.AsQueryable();

            if (sensorId.HasValue)
                query = query.Where(r => r.SensorId == sensorId.Value);

            if (startDate.HasValue)
                query = query.Where(r => r.RecordedAt >= startDate.Value);

            if (endDate.HasValue)
                query = query.Where(r => r.RecordedAt <= endDate.Value);

            return await query.CountAsync();
        }
    }
}
