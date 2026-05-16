using Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.DBContext
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
        {
        }

        public virtual DbSet<User> Users { get; set; }
        public virtual DbSet<Role> Roles { get; set; }
        public virtual DbSet<Location> Locations { get; set; }
        public virtual DbSet<Sensor> Sensors { get; set; }
        public virtual DbSet<Priority> Priorities { get; set; }
        public virtual DbSet<MaintenanceRequest> MaintenanceRequests { get; set; }
        public virtual DbSet<MaintenanceSchedule> MaintenanceSchedules { get; set; }
        public virtual DbSet<History> Histories { get; set; }
        public virtual DbSet<Area> Areas { get; set; }
        public virtual DbSet<SensorReading> SensorReadings { get; set; }
        public virtual DbSet<Report> Reports { get; set; }
        public virtual DbSet<NotificationLog> NotificationLogs { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.HasPostgresEnum<Severity>();

            modelBuilder.Entity<User>().ToTable("users");
            modelBuilder.Entity<Role>().ToTable("roles");
            modelBuilder.Entity<Location>().ToTable("locations");
            modelBuilder.Entity<Sensor>().ToTable("sensors");
            modelBuilder.Entity<Priority>().ToTable("priorities");
            modelBuilder.Entity<MaintenanceRequest>().ToTable(
                "maintenancerequests",
                t => t.HasCheckConstraint(
                    "CK_MaintenanceRequests_Status",
                    "\"status\" IN ('Pending','Assigned','InProgress','Completed','Cancelled')"
                )
            );
            modelBuilder.Entity<MaintenanceSchedule>().ToTable(
                "maintenanceschedules",
                t =>
                {
                    t.HasCheckConstraint(
                        "CK_MaintenanceSchedules_ScheduleType",
                        "\"schedule_type\" IN ('Weekly','Monthly','Quarterly')"
                    );
                    t.HasCheckConstraint(
                        "CK_MaintenanceSchedules_ScheduleMode",
                        "\"schedule_mode\" IN ('Manual','Auto')"
                    );
                    t.HasCheckConstraint(
                        "CK_MaintenanceSchedules_Status",
                        "\"status\" IN ('Scheduled','Active','Paused','Completed','Overdue')"
                    );
                }
            );
            modelBuilder.Entity<History>().ToTable("history");
            modelBuilder.Entity<Area>().ToTable("areas");
            modelBuilder.Entity<SensorReading>().ToTable("sensorreadings");
            modelBuilder.Entity<Report>().ToTable("report");
            modelBuilder.Entity<NotificationLog>().ToTable("notificationlogs");

            modelBuilder.Entity<Sensor>()
                .HasIndex(s => s.SensorCode)
                .IsUnique();

            modelBuilder.Entity<Location>()
                .HasOne(l => l.Area)
                .WithMany(a => a.Locations)
                .HasForeignKey(l => l.AreaId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Sensor>()
                .HasOne(s => s.Location)
                .WithMany(l => l.Sensors)
                .HasForeignKey(s => s.PlaceId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Sensor>()
                .HasOne(s => s.Technician)
                .WithMany(u => u.SensorsMaintained)
                .HasForeignKey(s => s.TechnicianId)
                .IsRequired(false)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<MaintenanceRequest>()
                .HasOne(m => m.AssignedTechnician)
                .WithMany(u => u.MaintenanceRequestsAssigned)
                .HasForeignKey(m => m.AssignedTechnicianTo)
                .IsRequired(false)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("FK_MaintenanceRequest_AssignedTechnician");

            modelBuilder.Entity<MaintenanceRequest>()
                .HasOne(m => m.Priority)
                .WithMany(p => p.MaintenanceRequests)
                .HasForeignKey(m => m.PriorityId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<MaintenanceRequest>()
                .HasOne(m => m.Sensor)
                .WithMany(s => s.MaintenanceRequests)
                .HasForeignKey(m => m.SensorId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<MaintenanceSchedule>()
                .HasOne(s => s.Sensor)
                .WithMany(s => s.MaintenanceSchedules)
                .HasForeignKey(s => s.SensorId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<MaintenanceSchedule>()
                .HasOne(s => s.AssignedTechnician)
                .WithMany(u => u.MaintenanceSchedulesAssigned)
                .HasForeignKey(s => s.AssignedTechnicianId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("FK_MaintenanceSchedule_AssignedTechnician");

            ConfigureColumnNames(modelBuilder);
        }

        private static void ConfigureColumnNames(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<User>(entity =>
            {
                entity.HasKey(e => e.UserId);
                entity.Property(e => e.UserId).HasColumnName("user_id");
                entity.Property(e => e.FullName).HasColumnName("full_name");
                entity.Property(e => e.Email).HasColumnName("email");
                entity.Property(e => e.PhoneNumber).HasColumnName("phone_number");
                entity.Property(e => e.PasswordHash).HasColumnName("password_hash");
                entity.Property(e => e.RoleId).HasColumnName("role_id");
                entity.Property(e => e.IsActive).HasColumnName("is_active");
                entity.Property(e => e.EmailOtpHash).HasColumnName("email_otp_hash");
                entity.Property(e => e.EmailOtpExpiredAt).HasColumnName("email_otp_expired_at");
                entity.Property(e => e.CreatedAt).HasColumnName("created_at").ValueGeneratedOnAdd();
            });
            modelBuilder.Entity<Role>(entity =>
            {
                entity.Property(e => e.RoleId).HasColumnName("role_id");
                entity.Property(e => e.RoleName).HasColumnName("role_name");
            });

            modelBuilder.Entity<Priority>(entity =>
            {
                entity.Property(e => e.PriorityId).HasColumnName("priority_id");
                entity.Property(e => e.DisplayName).HasColumnName("display_name");
            });

            modelBuilder.Entity<Location>(entity =>
            {
                entity.HasKey(e => e.PlaceId);
                entity.Property(e => e.PlaceId).HasColumnName("place_id");
                entity.Property(e => e.AreaId).HasColumnName("area_id");
                entity.Property(e => e.Title).HasColumnName("title");
                entity.Property(e => e.Address).HasColumnName("address");
                entity.Property(e => e.Latitude).HasColumnName("latitude");
                entity.Property(e => e.Longitude).HasColumnName("longitude");
            });

            modelBuilder.Entity<Area>(entity =>
            {
                entity.HasKey(e => e.AreaId);
                entity.Property(e => e.AreaId).HasColumnName("area_id");
                entity.Property(e => e.AreaName).HasColumnName("area_name");
            });

            modelBuilder.Entity<Sensor>(entity =>
            {
                entity.Property(e => e.SensorId).HasColumnName("sensor_id");
                entity.Property(e => e.PlaceId).HasColumnName("place_id");
                entity.Property(e => e.TechnicianId).HasColumnName("technician_id");
                entity.Property(e => e.SensorCode).HasColumnName("sensor_code");
                entity.Property(e => e.SensorName).HasColumnName("sensor_name");
                entity.Property(e => e.SensorType).HasColumnName("sensor_type");
                entity.Property(e => e.Protocol).HasColumnName("protocol");
                entity.Property(e => e.Specification).HasColumnName("specification");
                entity.Property(e => e.InstalledAt).HasColumnName("installed_at");
                entity.Property(e => e.WarningThreshold).HasColumnName("warning_threshold");
                entity.Property(e => e.DangerThreshold).HasColumnName("danger_threshold");
                entity.Property(e => e.MaxLevel).HasColumnName("max_level");
                entity.Property(e => e.CreatedAt).HasColumnName("created_at").ValueGeneratedOnAdd();
            });

            modelBuilder.Entity<MaintenanceRequest>(entity =>
            {
                entity.HasKey(e => e.RequestId);
                entity.Property(e => e.RequestId).HasColumnName("request_id");
                entity.Property(e => e.SensorId).HasColumnName("sensor_id");
                entity.Property(e => e.PriorityId).HasColumnName("priority_id");
                entity.Property(e => e.Description).HasColumnName("description");
                entity.Property(e => e.Deadline).HasColumnName("deadline");
                entity.Property(e => e.AssignedTechnicianTo).HasColumnName("assigned_technician_to");
                entity.Property(e => e.Note).HasColumnName("note");
                entity.Property(e => e.Status).HasColumnName("status");
                entity.Property(e => e.AssignedAt).HasColumnName("assigned_at");
                entity.Property(e => e.ResolvedAt).HasColumnName("resolved_at");
                entity.Property(e => e.CreatedAt).HasColumnName("created_at").ValueGeneratedOnAdd();
            });

            modelBuilder.Entity<MaintenanceSchedule>(entity =>
            {
                entity.HasKey(e => e.ScheduleId);
                entity.Property(e => e.ScheduleId).HasColumnName("schedule_id");
                entity.Property(e => e.SensorId).HasColumnName("sensor_id");
                entity.Property(e => e.ScheduleType).HasColumnName("schedule_type");
                entity.Property(e => e.ScheduleMode).HasColumnName("schedule_mode");
                entity.Property(e => e.StartDate).HasColumnName("start_date");
                entity.Property(e => e.EndDate).HasColumnName("end_date");
                entity.Property(e => e.AssignedTechnicianId).HasColumnName("assigned_technician_id");
                entity.Property(e => e.Note).HasColumnName("note");
                entity.Property(e => e.Status).HasColumnName("status");
                entity.Property(e => e.CreatedAt).HasColumnName("created_at").ValueGeneratedOnAdd();
            });

            modelBuilder.Entity<History>(entity =>
            {
                entity.HasKey(e => e.HistoryId);
                entity.Property(e => e.HistoryId).HasColumnName("history_id");
                entity.Property(e => e.LocationId).HasColumnName("location_id");
                entity.Property(e => e.StartTime).HasColumnName("start_time");
                entity.Property(e => e.EndTime).HasColumnName("end_time");
                entity.Property(e => e.MaxWaterLevel).HasColumnName("max_water_level");
                entity.Property(e => e.Severity)
                    .HasColumnName("severity")
                    .HasDefaultValue(Severity.Safe)
                    .HasConversion<string>();
                entity.Property(e => e.CreatedAt).HasColumnName("created_at").ValueGeneratedOnAdd();
            });

            modelBuilder.Entity<SensorReading>(entity =>
            {
                entity.HasKey(e => e.ReadingId);
                entity.Property(e => e.ReadingId).HasColumnName("reading_id");
                entity.Property(e => e.SensorId).HasColumnName("sensor_id");
                entity.Property(e => e.Status).HasColumnName("status");
                entity.Property(e => e.WaterLevelCm).HasColumnName("water_level_cm");
                entity.Property(e => e.BatteryPercent).HasColumnName("battery_percent");
                entity.Property(e => e.SignalStrength).HasColumnName("signal_strength");
                entity.Property(e => e.RecordedAt).HasColumnName("recorded_at").ValueGeneratedOnAdd();
            });

            modelBuilder.Entity<Report>(entity =>
            {
                entity.HasKey(e => e.ReportId);
                entity.Property(e => e.ReportId).HasColumnName("report_id");
                entity.Property(e => e.Description).HasColumnName("description");
                entity.Property(e => e.ForecastRiskLevel).HasColumnName("forecast_risk_level");
                entity.Property(e => e.ForecastDataJson).HasColumnName("forecast_data_json");
                entity.Property(e => e.CreatedAt).HasColumnName("created_at").ValueGeneratedOnAdd();
            });

            modelBuilder.Entity<NotificationLog>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Id).HasColumnName("id");
                entity.Property(e => e.ScheduleId).HasColumnName("schedule_id");
                entity.Property(e => e.TechnicianId).HasColumnName("technician_id");
                entity.Property(e => e.NotificationType).HasColumnName("notification_type");
                entity.Property(e => e.SentAt).HasColumnName("sent_at");
                entity.HasIndex(e => new { e.ScheduleId, e.NotificationType }).IsUnique();
            });
        }
    }
}
