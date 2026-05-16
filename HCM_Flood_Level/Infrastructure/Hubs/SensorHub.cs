using Microsoft.AspNetCore.SignalR;

namespace Infrastructure.Hubs
{
    public class SensorHub : Hub
    {
        public Task JoinTechnicianGroup(int technicianId)
        {
            return Groups.AddToGroupAsync(Context.ConnectionId, $"technician_{technicianId}");
        }

        public Task LeaveTechnicianGroup(int technicianId)
        {
            return Groups.RemoveFromGroupAsync(Context.ConnectionId, $"technician_{technicianId}");
        }
    }
}
