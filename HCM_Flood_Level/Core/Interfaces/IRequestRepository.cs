using Core.DTOs;
using Core.Entities;
using Core.Sharing;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Core.Interfaces
{
    public interface IRequestRepository : IGenericRepository<MaintenanceRequest>
    {
        Task<bool> StaffCreateRequestAsync(StaffCreateRequestDTO dto);
        Task<IEnumerable<MaintenanceRequest>> StaffGetRequestAsync(EntityParam entityParam);
        Task<IEnumerable<MaintenanceRequest>> TechnicianGetRequestAsync(int technicianId, EntityParam entityParam);
        Task<bool> TechnicianUpdateStatusAsync(int requestId, TechnicianUpdateStatusDTO dto);
        Task<bool> StaffUpdateRequestAsync(int requestId, StaffUpdateRequestDTO dto);
        Task<bool> StaffDeleteRequestAsync(int requestId);
        Task<IEnumerable<MaintenanceRequest>> GetBySensorIdAsync(int sensorId);
        Task<IEnumerable<MaintenanceRequest>> GetByAssignedTechnicianIdAsync(int technicianId);
    }
}
