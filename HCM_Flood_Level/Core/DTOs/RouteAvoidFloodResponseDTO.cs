using System.Collections.Generic;

namespace Core.DTOs
{
    public class RouteAvoidFloodResponseDTO
    {
        public RouteAlternativeDTO RecommendedRoute { get; set; }
        public bool IsRecommendedRouteFlooded { get; set; }
        public List<RouteFloodWarningDTO> RecommendedWarnings { get; set; } = new List<RouteFloodWarningDTO>();

        // Cho FE hiển thị nếu muốn (vd: so sánh 2-3 route khác nhau)
        public List<RouteAlternativeDTO> Alternatives { get; set; } = new List<RouteAlternativeDTO>();
        
        // True nếu route bị ngập cao (Danger) và có route thay thế
        // UI sẽ hỏi: "Tuyến đường bị ngập cao, bạn có muốn chuyển sang tuyến khác không?"
        public bool NeedsUserConfirmation { get; set; } = false;
        
        // Route thay thế được suggest khi NeedsUserConfirmation = true
        public RouteAlternativeDTO? SuggestedAlternative { get; set; }
        
        public string? Message { get; set; }
    }

    public class RouteAlternativeDTO
    {
        // Polyline encoded format để vẽ route trên map
        public string OverviewPolylinePoints { get; set; } = string.Empty;

        // Có thể null nếu SerpApi không trả đủ
        public int? DistanceMeters { get; set; }
        public int? DurationSeconds { get; set; }

        public double RiskScore { get; set; }
        public bool IsFlooded { get; set; }
    }

    public class RouteFloodWarningDTO
    {
        public int SensorId { get; set; }
        public int PlaceId { get; set; }
        public string SensorName { get; set; } = string.Empty;

        // "Warning" | "Danger" | "Offline"
        public string Severity { get; set; } = string.Empty;

        // Distance từ vị trí sensor đến đoạn route gần nhất (m)
        public double MinDistanceMeters { get; set; }

        public double SensorLatitude { get; set; }
        public double SensorLongitude { get; set; }

        public float WaterLevelCm { get; set; }
        public float? WarningThresholdCm { get; set; }
        public float? DangerThresholdCm { get; set; }
        public string ReadingStatus { get; set; } = string.Empty;
    }
}

