using Microsoft.AspNetCore.SignalR;
using System.Security.Claims;

namespace Infrastructure.Hubs
{
    public class SensorHub : Hub
    {
        public override async Task OnConnectedAsync()
        {
            var userId = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? Context.User?.FindFirst("sub")?.Value;
            var role = Context.User?.FindFirst(ClaimTypes.Role)?.Value;

            if (!string.IsNullOrEmpty(userId) && role == "Technician")
            {
                await Groups.AddToGroupAsync(Context.ConnectionId, $"technician_{userId}");
            }

            await base.OnConnectedAsync();
        }

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
