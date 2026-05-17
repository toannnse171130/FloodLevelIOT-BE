using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace Core.DTOs
{
    public class ManageSensorDTO
    {
        public int SensorId { get; set; }
        public string SensorName { get; set; }
        public string Title { get; set; }
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public DateTime? InstalledAt { get; set; }
        public string? Status { get; set; }
        public double? WaterLevel { get; set; }
        public string? SignalStrength { get; set; }
    }

    public class SensorDTO
    {
        //Thong so ky thuat
        public int SensorId { get; set; }
        public string SensorCode { get; set; }
        public string SensorName { get; set; }
        public string Protocol { get; set; }
        public DateTime WarrantyDate { get; set; }
        public string SensorType { get; set; }
        public double WarningThreshold { get; set; }
        public double DangerThreshold { get; set; }
        public int? MaxLevel { get; set; }
        public int? Battery { get; set; }
        // Lich su & Vi tri
        public DateTime? InstalledAt { get; set; } // ngay lap dat
        public DateTime CommissionedAt { get; set; } // ngay van hanh
        public int? TechnicianId { get; set; }
        public string InstalledByStaff { get; set; }
        public LocationDTO Location { get; set; }
        //Bao tri & trang thai
        public double? WaterLevel { get; set; }
        public string? Status { get; set; }
        public DateTime? RecordAt { get; set; }

        // [JsonPropertyName("schedule")]
        public List<ScheduleDTO> Schedule { get; set; } = new();

        // [JsonPropertyName("request")]
        public List<RequestDTO> Request { get; set; } = new();
    }

    public class CreateSensorDTO
    {
        // Location Info from Map
        public int AreaId { get; set; }
        public string? Title { get; set; }
        public string? Address { get; set; }
        public decimal Latitude { get; set; }
        public decimal Longitude { get; set; }

        // Sensor Details
        public int TechnicianId { get; set; }
        public string Specification { get; set; }
        public string SensorCode { get; set; }
        public string SensorName { get; set; }
        public string Protocol { get; set; }
        public string SensorType { get; set; }
        public double WarningThreshold { get; set; }
        public double DangerThreshold { get; set; }
        public int MaxLevel { get; set; } 
    }

    public class UpdateSensorDTO
    {
        public int? PlaceId { get; set; }
        public int? TechnicianId { get; set; }
        public string? Specification { get; set; }
        public string? SensorCode { get; set; }
        public string? SensorName { get; set; }
        public string? Protocol { get; set; }
        public string? SensorType { get; set; }
        public double? WarningThreshold { get; set; }
        public double? DangerThreshold { get; set; }
        public int? MaxLevel { get; set; }
        public decimal? Latitude { get; set; }
        public decimal? Longitude { get; set; }
        public string? Title { get; set; }
        public string? Address { get; set; }
    }

    public class SensorAreaDTO
    {
        public int SensorId { get; set; }
        public string SensorName { get; set; }
    }

}
