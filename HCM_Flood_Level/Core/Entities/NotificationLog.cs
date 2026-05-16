namespace Core.Entities
{
    public class NotificationLog
    {
        public int Id { get; set; }
        public int ScheduleId { get; set; }
        public int TechnicianId { get; set; }
        public string NotificationType { get; set; } = "";
        public DateTime SentAt { get; set; } = DateTime.UtcNow;
    }
}
