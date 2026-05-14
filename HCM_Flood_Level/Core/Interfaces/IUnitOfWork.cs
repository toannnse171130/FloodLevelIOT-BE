using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Core.Interfaces
{
    public interface IUnitOfWork
    {
        IUserRepository ManageUserRepository { get; }
        ISensorRepository ManageSensorRepository { get; }
        IScheduleRepository ManageMaintenanceScheduleRepository { get; }
        ILocationRepository LocationRepository { get; }
        IRequestRepository ManageRequestRepository { get; }
        IAreaRepository AreaRepository { get; }
        IHistoryRepository HistoryRepository { get; }
        ISensorReadingRepository SensorReadingRepository { get; }
    }
}
