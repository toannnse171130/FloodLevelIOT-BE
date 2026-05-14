using Core.Entities;
using Core.Sharing;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Core.Interfaces
{
    public interface ISensorReadingRepository
    {
        Task AddAsync(SensorReading reading);
        Task<IReadOnlyList<SensorReading>> GetAllAsync(EntityParam param);
        Task<int> CountAsync(int? sensorId = null);
    }
}
