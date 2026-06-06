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
    public class SensorRepository : GenericRepository<Sensor>, ISensorRepository
    {
        private readonly AppDbContext _context;
        private readonly IFileProvider _fileProvider;
        private readonly IMapper _mapper;
        private readonly IMapsService _mapsService;
        private readonly IScheduleRepository _maintenanceScheduleRepository;

        public SensorRepository(AppDbContext context, IFileProvider fileProvider, IMapper mapper, IMapsService mapsService, IScheduleRepository maintenanceScheduleRepository) : base(context)
        {
            _context = context;
            _fileProvider = fileProvider;
            _mapper = mapper;
            _mapsService = mapsService;
            _maintenanceScheduleRepository = maintenanceScheduleRepository;
        }

        public async Task<Sensor> GetByIdAsync(int id)
        {
            return await _context.Sensors.FindAsync(id);
        }

        public async Task<Sensor> GetByDeviceId(string sensordcode)
        {
            return await _context.Sensors
                .FirstOrDefaultAsync(x => x.SensorCode == sensordcode);
        }

        public async Task<Sensor> GetByIdWithDetailsAsync(int id)
        {
            return await _context.Sensors
                .Include(s => s.Location)
                    .ThenInclude(l => l.Area)
                .Include(s => s.Technician)
                .FirstOrDefaultAsync(s => s.SensorId == id);
        }


        public async Task<IEnumerable<Sensor>> GetAllSensorsAsync(EntityParam param)
        {
            var query = _context.Sensors
                .Include(s => s.Location)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(param.Search))
            {
                query = query.Where(s =>
                    s.SensorCode.Contains(param.Search) ||
                    s.SensorName.Contains(param.Search) ||
                    s.SensorType.Contains(param.Search) ||
                    s.Location.Title.Contains(param.Search)
                );
            }

            return await query
                .OrderByDescending(s => s.InstalledAt)
                .Skip((param.Pagenumber - 1) * param.Pagesize)
                .Take(param.Pagesize)
                .ToListAsync();
        }

        public async Task<int> AddNewSensorAsync(CreateSensorDTO dto)
        {
            // 1. Find or create location
            var location = await _context.Locations
                .FirstOrDefaultAsync(l => l.Latitude == dto.Latitude && l.Longitude == dto.Longitude);

            if (location == null)
            {
                location = new Location
                {
                    AreaId = dto.AreaId,
                    Title = dto.Title ?? "Unknown",
                    Address = dto.Address ?? "Unknown",
                    Latitude = dto.Latitude,
                    Longitude = dto.Longitude
                };
                await _context.Locations.AddAsync(location);
                await _context.SaveChangesAsync();
            }

            // 2. Prevent duplicate sensor for same location
            var duplicateLocation = await _context.Sensors.AnyAsync(s => s.PlaceId == location.PlaceId);
            if (duplicateLocation)
                return 0;

            // 3. Create sensor
            var sensor = _mapper.Map<Sensor>(dto);
            sensor.PlaceId = location.PlaceId;

            sensor.InstalledAt = DateTime.UtcNow;
            sensor.CreatedAt = DateTime.UtcNow;

            await _context.Sensors.AddAsync(sensor);
            await _context.SaveChangesAsync();

            // 4. Default reading
            var defaultReading = new SensorReading
            {
                SensorId = sensor.SensorId,
                Status = "Offline",
                WaterLevelCm = 0,
                SignalStrength = "Không kết nối",
                BatteryPercent = 100,
                RecordedAt = DateTime.UtcNow
            };
            await AddSensorReadingAsync(defaultReading);

            // 5. Auto monthly maintenance schedule
            var schedule = new MaintenanceSchedule
            {
                SensorId = sensor.SensorId,
                ScheduleType = "Monthly",
                ScheduleMode = "Auto",
                StartDate = DateTime.UtcNow,
                EndDate = DateTime.UtcNow.AddMonths(1),
                AssignedTechnicianId = dto.TechnicianId,
                Status = "Scheduled",
                CreatedAt = DateTime.UtcNow
            };
            await _context.MaintenanceSchedules.AddAsync(schedule);
            await _context.SaveChangesAsync();

            return sensor.SensorId;
        }

        public async Task<bool> LocationExistsAsync(int placeId)
        {
            return await _context.Locations.AnyAsync(l => l.PlaceId == placeId);
        }

        public async Task<bool> LocationHasSensorAsync(int placeId)
        {
            return await _context.Sensors.AnyAsync(s => s.PlaceId == placeId);
        }

        public async Task<bool> UpdateSensorAsync(int id, UpdateSensorDTO dto)
        {
            var sensor = await _context.Sensors.FindAsync(id);

            if (sensor == null)
                return false;

            if (dto.PlaceId.HasValue)
            {
                // Client sends the AreaId in PlaceId. A Sensor points to a Location (not an Area),
                // so resolve the area to a concrete Location via find-or-create, mirroring sensor creation.
                var areaId = dto.PlaceId.Value;
                var areaExists = await _context.Areas.AnyAsync(a => a.AreaId == areaId);
                if (!areaExists)
                    return false;

                var currentLocation = await _context.Locations.FindAsync(sensor.PlaceId);
                var lat = dto.Latitude ?? currentLocation?.Latitude ?? 0m;
                var lng = dto.Longitude ?? currentLocation?.Longitude ?? 0m;
                var title = !string.IsNullOrEmpty(dto.Title) ? dto.Title : (currentLocation?.Title ?? "Unknown");
                var address = !string.IsNullOrEmpty(dto.Address) ? dto.Address : (currentLocation?.Address ?? "Unknown");

                var location = await _context.Locations
                    .FirstOrDefaultAsync(l => l.AreaId == areaId && l.Latitude == lat && l.Longitude == lng);

                if (location == null)
                {
                    location = new Location
                    {
                        AreaId = areaId,
                        Title = title,
                        Address = address,
                        Latitude = lat,
                        Longitude = lng
                    };
                    await _context.Locations.AddAsync(location);
                    await _context.SaveChangesAsync();
                }

                sensor.PlaceId = location.PlaceId;
            }

            if (dto.TechnicianId.HasValue)
            {
                sensor.TechnicianId = dto.TechnicianId.Value;
            }

            if (!string.IsNullOrEmpty(dto.Specification))
                sensor.Specification = dto.Specification;

            // If sensor code is being changed, ensure uniqueness
            if (!string.IsNullOrEmpty(dto.SensorCode) && dto.SensorCode != sensor.SensorCode)
            {
                var codeExists = await _context.Sensors.AnyAsync(s => s.SensorCode == dto.SensorCode && s.SensorId != sensor.SensorId);
                if (codeExists)
                    return false;

                sensor.SensorCode = dto.SensorCode;
            }

            if (!string.IsNullOrEmpty(dto.SensorName))
                sensor.SensorName = dto.SensorName;

            if (!string.IsNullOrEmpty(dto.Protocol))
                sensor.Protocol = dto.Protocol;

            if (!string.IsNullOrEmpty(dto.SensorType))
                sensor.SensorType = dto.SensorType;

            if (dto.WarningThreshold.HasValue)
                sensor.WarningThreshold = (float?)dto.WarningThreshold.Value;

            if (dto.DangerThreshold.HasValue)
                sensor.DangerThreshold = (float?)dto.DangerThreshold.Value;

            if (dto.MaxLevel.HasValue)
                sensor.MaxLevel = dto.MaxLevel.Value;

            if (dto.Latitude.HasValue || dto.Longitude.HasValue || !string.IsNullOrEmpty(dto.Title) || !string.IsNullOrEmpty(dto.Address))
            {
                var location = await _context.Locations.FindAsync(sensor.PlaceId);
                if (location != null)
                {
                    if (dto.Latitude.HasValue)
                        location.Latitude = dto.Latitude.Value;
                    if (dto.Longitude.HasValue)
                        location.Longitude = dto.Longitude.Value;
                    if (!string.IsNullOrEmpty(dto.Title))
                        location.Title = dto.Title;
                    if (!string.IsNullOrEmpty(dto.Address))
                        location.Address = dto.Address;
                }
            }

            await _context.SaveChangesAsync();
            return true;
        }

        public async Task<bool?> DeleteSensorAsync(int id)
        {
            var sensor = await _context.Sensors.FindAsync(id);
            if (sensor == null)
                return null;

            var schedules = await _context.MaintenanceSchedules.Where(s => s.SensorId == id).ToListAsync();
            if (schedules.Any())
                _context.MaintenanceSchedules.RemoveRange(schedules);

            var requests = await _context.MaintenanceRequests.Where(m => m.SensorId == id).ToListAsync();
            if (requests.Any())
                _context.MaintenanceRequests.RemoveRange(requests);

            _context.Sensors.Remove(sensor);
            await _context.SaveChangesAsync();
            return true;
        }

        public async Task<IEnumerable<SensorReading>> GetLatestReadingsForSensorIdsAsync(IEnumerable<int> sensorIds)
        {
            if (sensorIds == null)
                return new List<SensorReading>();

            var ids = sensorIds.Distinct().ToList();

            var latest = await _context.SensorReadings
                .Where(r => ids.Contains(r.SensorId))
                .GroupBy(r => r.SensorId)
                .Select(g => g.OrderByDescending(r => r.RecordedAt).FirstOrDefault())
                .ToListAsync();

            return latest.Where(r => r != null)!;
        }

        public async Task<IEnumerable<int>> GetAllSensorIdsAsync()
        {
            return await _context.Sensors.Select(s => s.SensorId).ToListAsync();
        }

        public async Task AddSensorReadingAsync(SensorReading reading)
        {
            if (reading == null) return;
            await _context.SensorReadings.AddAsync(reading);
            await _context.SaveChangesAsync();
        }

        public async Task PruneSensorReadingsAsync(int sensorId, int maxEntries)
        {
            var readings = await _context.SensorReadings
                .Where(r => r.SensorId == sensorId)
                .OrderByDescending(r => r.RecordedAt)
                .ToListAsync();

            if (readings.Count <= maxEntries) return;

            var toDelete = readings.Skip(maxEntries).ToList();
            _context.SensorReadings.RemoveRange(toDelete);
            await _context.SaveChangesAsync();
        }

        public async Task<double?> GetMaxHistoryLevelForSensorAsync(int sensorId)
        {
            var sensor = await _context.Sensors.FindAsync(sensorId);
            if (sensor == null || sensor.PlaceId == 0) return null;

            var maxLevel = await _context.Histories
                .Where(h => h.LocationId == sensor.PlaceId)
                .MaxAsync(h => (float?)h.MaxWaterLevel);

            return maxLevel;
        }

        public async Task AddHistoryAsync(History history)
        {
            if (history == null) return;
            await _context.Histories.AddAsync(history);
            await _context.SaveChangesAsync();
        }
    }
}
