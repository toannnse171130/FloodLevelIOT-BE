using Core.Entities;
using Core.Interfaces;
using Infrastructure.DBContext;

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
    }
}
