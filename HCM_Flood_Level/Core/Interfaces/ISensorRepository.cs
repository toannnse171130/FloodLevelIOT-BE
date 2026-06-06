using Core.DTOs;
using Core.Entities;
using Core.Interfaces;
using Core.Sharing;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Core.Interfaces
{
    public interface ISensorRepository : IGenericRepository<Sensor>
    {
        Task<IEnumerable<Sensor>> GetAllSensorsAsync(EntityParam param);
        Task<IEnumerable<SensorReading>> GetLatestReadingsForSensorIdsAsync(IEnumerable<int> sensorIds);
        Task<IEnumerable<int>> GetAllSensorIdsAsync();
        Task AddSensorReadingAsync(SensorReading reading);
        Task PruneSensorReadingsAsync(int sensorId, int maxEntries);
        Task<double?> GetMaxHistoryLevelForSensorAsync(int sensorId);
        Task AddHistoryAsync(History history);
        Task<int> AddNewSensorAsync(CreateSensorDTO dto);
        Task<bool> LocationExistsAsync(int placeId);
        Task<bool> LocationHasSensorAsync(int placeId);
        Task<bool> UpdateSensorAsync(int id, UpdateSensorDTO dto);
        Task<bool?> DeleteSensorAsync(int id);


        //24/03 - for SensorController GetById and GetByDeviceId
        Task<Sensor> GetByIdAsync(int id);
        Task<Sensor> GetByDeviceId(string deviceId);

        // Loads sensor with Location + Location.Area (for AreaName) + Technician
        Task<Sensor> GetByIdWithDetailsAsync(int id);
    }
}
