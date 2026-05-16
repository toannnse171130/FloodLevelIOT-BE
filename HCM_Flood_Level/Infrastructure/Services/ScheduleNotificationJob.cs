using Core.Entities;
using Core.Interfaces;
using Infrastructure.DBContext;
using Infrastructure.Hubs;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Services
{
    public class ScheduleNotificationJob : BackgroundService
    {
        private readonly IServiceProvider _services;
        private readonly ILogger<ScheduleNotificationJob> _logger;
        private readonly IHubContext<SensorHub> _hubContext;
        private readonly TimeSpan _interval = TimeSpan.FromHours(1);

        public ScheduleNotificationJob(
            IServiceProvider services,
            ILogger<ScheduleNotificationJob> logger,
            IHubContext<SensorHub> hubContext)
        {
            _services = services;
            _logger = logger;
            _hubContext = hubContext;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await CheckAndNotify(stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "ScheduleNotificationJob error");
                }

                await Task.Delay(_interval, stoppingToken);
            }
        }

        private async Task CheckAndNotify(CancellationToken ct)
        {
            using var scope = _services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var notificationService = scope.ServiceProvider.GetRequiredService<INotificationService>();

            var now = DateTime.UtcNow;
            var threeDaysFromNow = now.AddDays(3);

            var schedules = await db.MaintenanceSchedules
                .Include(s => s.Sensor)
                .Include(s => s.AssignedTechnician)
                .Where(s => s.Status != "Completed"
                    && s.EndDate.HasValue
                    && s.AssignedTechnicianId.HasValue
                    && s.EndDate.Value < threeDaysFromNow)
                .ToListAsync(ct);

            foreach (var schedule in schedules)
            {
                if (ct.IsCancellationRequested) break;

                var type = schedule.EndDate!.Value < now ? "Overdue" : "DueSoon";

                var alreadySent = await db.NotificationLogs
                    .AnyAsync(n => n.ScheduleId == schedule.ScheduleId
                        && n.NotificationType == type, ct);
                if (alreadySent) continue;

                var sensorName = schedule.Sensor?.SensorName ?? $"Cảm biến #{schedule.SensorId}";
                var techEmail = schedule.AssignedTechnician?.Email;

                if (string.IsNullOrWhiteSpace(techEmail))
                {
                    _logger.LogWarning("Schedule {Id}: technician {TechId} has no email",
                        schedule.ScheduleId, schedule.AssignedTechnicianId);
                }
                else
                {
                    try
                    {
                        var (subject, body) = BuildEmailContent(
                            schedule.ScheduleId, sensorName, schedule.EndDate!.Value, type);
                        await notificationService.SendEmailAsync(techEmail, subject, body);
                        _logger.LogInformation("Sent {Type} email for schedule {Id} to {Email}",
                            type, schedule.ScheduleId, techEmail);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to send {Type} email for schedule {Id}",
                            type, schedule.ScheduleId);
                    }
                }

                // Log regardless of email success to prevent retry spam
                db.NotificationLogs.Add(new NotificationLog
                {
                    ScheduleId = schedule.ScheduleId,
                    TechnicianId = schedule.AssignedTechnicianId!.Value,
                    NotificationType = type,
                    SentAt = DateTime.UtcNow,
                });
                await db.SaveChangesAsync(ct);

                // Send via SignalR to assigned technician's group only
                try
                {
                    var groupName = $"technician_{schedule.AssignedTechnicianId}";
                    await _hubContext.Clients.Group(groupName).SendAsync("ReceiveScheduleReminder", new
                    {
                        ScheduleId = schedule.ScheduleId,
                        SensorId = schedule.SensorId,
                        SensorName = sensorName,
                        TechnicianId = schedule.AssignedTechnicianId,
                        Type = type,
                        EndDate = schedule.EndDate,
                        Message = type == "Overdue"
                            ? $"Lịch bảo trì #{schedule.ScheduleId} đã quá hạn"
                            : $"Lịch bảo trì #{schedule.ScheduleId} sắp tới hạn",
                    }, ct);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to broadcast SignalR for schedule {Id}",
                        schedule.ScheduleId);
                }
            }
        }

        private static (string subject, string body) BuildEmailContent(
            int scheduleId, string sensorName, DateTime endDate, string type)
        {
            var dateStr = endDate.ToString("dd/MM/yyyy HH:mm");

            if (type == "Overdue")
            {
                return (
                    $"[Quá hạn] Lịch bảo trì #{scheduleId} - {sensorName}",
                    $"Lịch bảo trì #{scheduleId} cho cảm biến {sensorName} đã quá hạn từ {dateStr}. Vui lòng xử lý ngay."
                );
            }

            return (
                $"[Sắp tới hạn] Lịch bảo trì #{scheduleId} - {sensorName}",
                $"Lịch bảo trì #{scheduleId} cho cảm biến {sensorName} sẽ tới hạn vào {dateStr}. Vui lòng hoàn thành đúng hạn."
            );
        }
    }
}
