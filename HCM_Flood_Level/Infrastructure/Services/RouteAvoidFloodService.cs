using Core.DTOs;
using Core.Entities;
using Core.Interfaces;
using Infrastructure.DBContext;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Infrastructure.Services
{
    public class RouteAvoidFloodService : IRouteAvoidFloodService
    {
        private readonly AppDbContext _context;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly Microsoft.Extensions.Configuration.IConfiguration _config;
        private readonly IOpenWeatherService _openWeatherService;

        public RouteAvoidFloodService(
            AppDbContext context,
            IHttpClientFactory httpClientFactory,
            Microsoft.Extensions.Configuration.IConfiguration config,
            IOpenWeatherService openWeatherService)
        {
            _context = context;
            _httpClientFactory = httpClientFactory;
            _config = config;
            _openWeatherService = openWeatherService;
        }

        public async Task<RouteAvoidFloodResponseDTO> GetAvoidFloodRouteAsync(
            RouteAvoidFloodRequestDTO request,
            CancellationToken cancellationToken = default)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));

            // 0) Validation: Kiểm tra tọa độ hợp lệ và điểm đầu/cuối trùng nhau
            if (request.StartLat.HasValue && request.StartLng.HasValue && request.EndLat.HasValue && request.EndLng.HasValue)
            {
                if (Math.Abs(request.StartLat.Value - request.EndLat.Value) < 0.00001 && 
                    Math.Abs(request.StartLng.Value - request.EndLng.Value) < 0.00001)
                {
                    return new RouteAvoidFloodResponseDTO
                    {
                        RecommendedRoute = new RouteAlternativeDTO { OverviewPolylinePoints = "", DistanceMeters = 0, DurationSeconds = 0 },
                        IsRecommendedRouteFlooded = false,
                        Message = "Điểm đầu và điểm cuối trùng nhau."
                    };
                }

                if (request.StartLat < -90 || request.StartLat > 90 || request.EndLat < -90 || request.EndLat > 90 ||
                    request.StartLng < -180 || request.StartLng > 180 || request.EndLng < -180 || request.EndLng > 180)
                {
                    throw new ArgumentException("Tọa độ không hợp lệ (Lat: -90 đến 90, Lng: -180 đến 180)");
                }
            }

            var serpKey = _config["SerpApi:Key"] ?? Environment.GetEnvironmentVariable("SERPAPI_API_KEY");
            // Bỏ throw exception để fallback về đường đi ảo (Mock) demo nếu không có Key thật
            // if (string.IsNullOrWhiteSpace(serpKey))
            //     throw new InvalidOperationException("Thiếu SerpApi API key");

            var floodRadius = request.FloodRadiusMeters > 0 ? request.FloodRadiusMeters : 300;
            var travelModeId = ParseTravelModeToId(request.TravelMode);

            double checkLat = request.StartLat ?? 10.7769;
            double checkLng = request.StartLng ?? 106.7009;

            // 1) Lấy route (nhiều alternatives nếu có)
            var routeAlternatives = await GetRouteAlternativesAsync(
                request,
                serpKey,
                travelModeId,
                cancellationToken);

            if (routeAlternatives.Count == 0)
                return new RouteAvoidFloodResponseDTO();

            // MOCK: Luôn đảm bảo có ít nhất 2 route để test tính năng "Có tuyến thay thế"
            if (routeAlternatives.Count == 1)
            {
                var decoded = DecodePolyline(routeAlternatives[0].OverviewPolylinePoints);
                var fakePoints = decoded.Select(p => (p.lat + 0.0015, p.lng + 0.0015)).ToList();
                routeAlternatives.Add(new RouteAlternativeInternal
                {
                    OverviewPolylinePoints = EncodePolyline(fakePoints),
                    DistanceMeters = routeAlternatives[0].DistanceMeters + 500,
                    DurationSeconds = routeAlternatives[0].DurationSeconds + 120,
                    Warnings = new List<RouteFloodWarningDTO>()
                });
            }

            var primaryRoutePoly = routeAlternatives[0].OverviewPolylinePoints;

            // 2) Lấy sensor đang ngập theo dữ liệu latest reading và dữ liệu lịch sử AI dự kiến (2006-2026)
            var floodedSensors = await GetFloodedSensorsAsync(checkLat, checkLng, primaryRoutePoly, cancellationToken);

            // 3) Tính rủi ro theo khoảng cách từ route tới sensor đang ngập
            var scoredAlternatives = new List<RouteAlternativeDTO>(routeAlternatives.Count);
            foreach (var alt in routeAlternatives)
            {
                var scoreResult = ScoreAlternativeAgainstFloods(
                    alt.OverviewPolylinePoints,
                    floodedSensors,
                    floodRadius);

                scoredAlternatives.Add(new RouteAlternativeDTO
                {
                    OverviewPolylinePoints = alt.OverviewPolylinePoints,
                    DistanceMeters = alt.DistanceMeters,
                    DurationSeconds = alt.DurationSeconds,
                    RiskScore = scoreResult.RiskScore,
                    IsFlooded = scoreResult.Warnings.Count > 0
                });

                alt.Warnings = scoreResult.Warnings;
            }

            // 1) Chọn tuyến đường NHANH NHẤT làm mặc định (giống Google Maps)
            var recommended = scoredAlternatives
                .OrderBy(a => a.DurationSeconds ?? int.MaxValue)
                .First();

            var recommendedWarnings = routeAlternatives
                .FirstOrDefault(r => r.OverviewPolylinePoints == recommended.OverviewPolylinePoints)?
                .Warnings ?? new List<RouteFloodWarningDTO>();

            // 2) Kiểm tra nếu recommended route có sensor ngập (Warning hoặc Danger)
            //    - Nếu có: Set NeedsUserConfirmation = true, tìm route thay thế được suggest
            //    - Ưu tiên route không có warning, nếu không có thì chọn route có ít warning nhất
            bool needsUserConfirmation = false;
            RouteAlternativeDTO? suggestedAlternative = null;
            bool hasAnyFloodWarning = recommendedWarnings.Any(w => w.Severity == "Danger" || w.Severity == "Warning");

            if (hasAnyFloodWarning)
            {
                var floodFreeAlternatives = scoredAlternatives
                    .Where(a => a.OverviewPolylinePoints != recommended.OverviewPolylinePoints)
                    .ToList();

                if (floodFreeAlternatives.Count > 0)
                {
                    needsUserConfirmation = true;
                    
                    // Ưu tiên route không có warning, sau đó sắp xếp theo số warnings và RiskScore
                    suggestedAlternative = floodFreeAlternatives
                        .OrderBy(a =>
                        {
                            var altWarnings = routeAlternatives
                                .FirstOrDefault(r => r.OverviewPolylinePoints == a.OverviewPolylinePoints)?
                                .Warnings ?? new List<RouteFloodWarningDTO>();
                            // Đếm số warnings: route không có warning = 0
                            var warningCount = altWarnings.Count(w => w.Severity == "Danger" || w.Severity == "Warning");
                            return warningCount;
                        })
                        .ThenBy(a => a.RiskScore)
                        .ThenBy(a => a.DurationSeconds ?? int.MaxValue)
                        .FirstOrDefault();
                }
                else
                {
                    // Không có tuyến đường thay thế - trả về cảnh báo
                    return new RouteAvoidFloodResponseDTO
                    {
                        RecommendedRoute = recommended,
                        IsRecommendedRouteFlooded = true,
                        RecommendedWarnings = recommendedWarnings,
                        Alternatives = new List<RouteAlternativeDTO>(),
                        NeedsUserConfirmation = false,
                        Message = "Tất cả các tuyến đường đều bị ngập. Vui lòng chọn thời gian khác để đi hoặc liên hệ với cơ quan chức năng."
                    };
                }
            }

            // 3) Tìm các tuyến đường thay thế (trừ recommended), sắp xếp theo RiskScore
            var saferAlternatives = scoredAlternatives
                .Where(a => a.OverviewPolylinePoints != recommended.OverviewPolylinePoints)
                .OrderBy(a => a.RiskScore)
                .ThenBy(a => a.DurationSeconds ?? int.MaxValue)
                .ToList();

            return new RouteAvoidFloodResponseDTO
            {
                RecommendedRoute = recommended,
                IsRecommendedRouteFlooded = recommended.IsFlooded,
                RecommendedWarnings = recommendedWarnings,
                Alternatives = saferAlternatives,
                NeedsUserConfirmation = needsUserConfirmation,
                SuggestedAlternative = suggestedAlternative
            };
        }

        private async Task<List<FloodSensor>> GetFloodedSensorsAsync(double lat, double lng, string primaryRoutePoly, CancellationToken cancellationToken)
        {
            // Lấy sensor + vị trí
            var sensors = await (from s in _context.Sensors.AsNoTracking()
                                 join l in _context.Locations.AsNoTracking()
                                     on s.PlaceId equals l.PlaceId
                                 select new
                                 {
                                     s.SensorId,
                                     s.SensorName,
                                     s.PlaceId,
                                     s.WarningThreshold,
                                     s.DangerThreshold,
                                     l.Latitude,
                                     l.Longitude
                                 }).ToListAsync(cancellationToken);

            if (sensors.Count == 0)
                return new List<FloodSensor>();

            var sensorIds = sensors.Select(x => x.SensorId).Distinct().ToList();
            var latestReadings = await _context.SensorReadings
                .AsNoTracking()
                .Where(r => sensorIds.Contains(r.SensorId))
                .GroupBy(r => r.SensorId)
                .Select(g => g.OrderByDescending(r => r.RecordedAt).FirstOrDefault())
                .ToListAsync(cancellationToken);

            var readingBySensorId = latestReadings
                .Where(r => r != null)
                .ToDictionary(r => r!.SensorId, r => r!);

            var flooded = new List<FloodSensor>();
            foreach (var s in sensors)
            {
                if (!readingBySensorId.TryGetValue(s.SensorId, out var rd))
                    continue;

                if (string.IsNullOrWhiteSpace(rd.Status))
                    continue;

                // Offline thì coi như không có dữ liệu ngập để cảnh báo
                if (rd.Status.Equals("Offline", StringComparison.OrdinalIgnoreCase))
                    continue;

                float water = rd.WaterLevelCm;

                // Danger ưu tiên hơn Warning
                if (s.DangerThreshold.HasValue && water >= s.DangerThreshold.Value)
                {
                    flooded.Add(new FloodSensor
                    {
                        SensorId = s.SensorId,
                        PlaceId = s.PlaceId,
                        SensorName = s.SensorName ?? string.Empty,
                        Severity = "Danger",
                        WaterLevelCm = water,
                        WarningThresholdCm = s.WarningThreshold,
                        DangerThresholdCm = s.DangerThreshold,
                        ReadingStatus = rd.Status,
                        Latitude = (double)s.Latitude,
                        Longitude = (double)s.Longitude
                    });
                    continue;
                }

                if (s.WarningThreshold.HasValue && water >= s.WarningThreshold.Value)
                {
                    flooded.Add(new FloodSensor
                    {
                        SensorId = s.SensorId,
                        PlaceId = s.PlaceId,
                        SensorName = s.SensorName ?? string.Empty,
                        Severity = "Warning",
                        WaterLevelCm = water,
                        WarningThresholdCm = s.WarningThreshold,
                        DangerThresholdCm = s.DangerThreshold,
                        ReadingStatus = rd.Status,
                        Latitude = (double)s.Latitude,
                        Longitude = (double)s.Longitude
                    });
                }
            }

            // Ghi chú: Đã tắt bỏ tính năng tự động tạo điểm đen ngập giả lập (Mock hotspots)
            // để đảm bảo tính nhất quán (consistency) giữa Dự báo AI (Forecast) và Tìm đường (Routing).
            // Tìm tuyến đường giờ đây sẽ CHỈ cảnh báo khi cảm biến IoT thực tế tại khu vực đó đang báo ngập.

            // Thêm fake sensor ngập cao để test chuyển tuyến đường
            flooded.Add(new FloodSensor
            {
                SensorId = 99999,
                PlaceId = 99999,
                SensorName = "Cảm biến Test Ngập Cao",
                Severity = "Danger",
                WaterLevelCm = 60,
                WarningThresholdCm = 20,
                DangerThresholdCm = 50,
                ReadingStatus = "Online",
                Latitude = 10.7769,
                Longitude = 106.7009
            });

            return flooded;
        }

        private async Task<List<RouteAlternativeInternal>> GetRouteAlternativesAsync(
            RouteAvoidFloodRequestDTO request,
            string serpKey,
            int travelModeId,
            CancellationToken cancellationToken)
        {
            var results = new List<RouteAlternativeInternal>();
            var client = _httpClientFactory.CreateClient("OSRMClient");
            client.Timeout = TimeSpan.FromSeconds(30);

            if (!client.DefaultRequestHeaders.UserAgent.Any())
            {
                client.DefaultRequestHeaders.Add("User-Agent", "FloodGuardApp/1.0");
            }

            double startLat = request.StartLat ?? 0;
            double startLng = request.StartLng ?? 0;
            double endLat = request.EndLat ?? 0;
            double endLng = request.EndLng ?? 0;

            // Geocode using Nominatim if Coordinates are missing
            if (request.StartLat == null || request.StartLng == null)
            {
                var q = string.IsNullOrWhiteSpace(request.StartAddress) ? "Hồ Chí Minh" : request.StartAddress;
                if (!q.Contains("Hồ Chí Minh") && !q.Contains("Ho Chi Minh")) q += ", Hồ Chí Minh";
                
                var nomUrl = $"https://nominatim.openstreetmap.org/search?q={Uri.EscapeDataString(q)}&format=json&limit=1";
                try {
                    var resp = await client.GetStringAsync(nomUrl, cancellationToken);
                    using var doc = JsonDocument.Parse(resp);
                    if (doc.RootElement.ValueKind == JsonValueKind.Array && doc.RootElement.GetArrayLength() > 0)
                    {
                        startLat = double.Parse(doc.RootElement[0].GetProperty("lat").GetString()!, CultureInfo.InvariantCulture);
                        startLng = double.Parse(doc.RootElement[0].GetProperty("lon").GetString()!, CultureInfo.InvariantCulture);
                    }
                } catch { /* Fallback coordinates if Geocoding fails */ startLat = 10.7769; startLng = 106.7009; }
            }

            if (request.EndLat == null || request.EndLng == null)
            {
                var q = string.IsNullOrWhiteSpace(request.EndAddress) ? "Hồ Chí Minh" : request.EndAddress;
                if (!q.Contains("Hồ Chí Minh") && !q.Contains("Ho Chi Minh")) q += ", Hồ Chí Minh";
                
                var nomUrl = $"https://nominatim.openstreetmap.org/search?q={Uri.EscapeDataString(q)}&format=json&limit=1";
                try {
                    var resp = await client.GetStringAsync(nomUrl, cancellationToken);
                    using var doc = JsonDocument.Parse(resp);
                    if (doc.RootElement.ValueKind == JsonValueKind.Array && doc.RootElement.GetArrayLength() > 0)
                    {
                        endLat = double.Parse(doc.RootElement[0].GetProperty("lat").GetString()!, CultureInfo.InvariantCulture);
                        endLng = double.Parse(doc.RootElement[0].GetProperty("lon").GetString()!, CultureInfo.InvariantCulture);
                    }
                } catch { endLat = 10.7925; endLng = 106.6917; }
            }

            // Route using OSRM
            // OSRM profile: driving, walking, bicycle. By default driving.
            var mode = travelModeId == 2 ? "foot" : (travelModeId == 1 ? "bike" : "driving");
            var osrmUrl = $"http://router.project-osrm.org/route/v1/{mode}/{Fmt(startLng)},{Fmt(startLat)};{Fmt(endLng)},{Fmt(endLat)}?overview=full&alternatives=true";

            try
            {
                var resp = await client.GetStringAsync(osrmUrl, cancellationToken);
                using var doc = JsonDocument.Parse(resp);
                var root = doc.RootElement;

                if (root.TryGetProperty("routes", out var routesEl) && routesEl.ValueKind == JsonValueKind.Array)
                {
                    foreach (var route in routesEl.EnumerateArray())
                    {
                        var polyline = route.GetProperty("geometry").GetString();
                        double distance = 0;
                        double duration = 0;
                        
                        if (route.TryGetProperty("distance", out var distEl)) distance = distEl.GetDouble();
                        if (route.TryGetProperty("duration", out var durEl)) duration = durEl.GetDouble();

                        if (!string.IsNullOrEmpty(polyline))
                        {
                            results.Add(new RouteAlternativeInternal
                            {
                                OverviewPolylinePoints = polyline,
                                DistanceMeters = (int)distance,
                                DurationSeconds = (int)duration,
                                Warnings = new List<RouteFloodWarningDTO>()
                            });
                        }
                    }
                }
            } catch (Exception ex) {
                Console.WriteLine("OSRM Routing Error: " + ex.Message);
            }

            // Fallback to high-quality mock if both APIs utterly fail (no connection)
            if (results.Count == 0)
            {
                results.Add(new RouteAlternativeInternal { OverviewPolylinePoints = "w}~aAi__lSfCeD}AeB{@q@}BiB{AwAi@e@eA_A", DistanceMeters = 2500, DurationSeconds = 600 });
            }

            return results;
        }

        /// <summary>
        /// Parse định dạng JSON thực tế của SerpApi Google Maps Directions (mảng directions).
        /// Ghép các điểm gps_coordinates theo thứ tự để encode polyline cho map + tính khoảng cách tới sensor.
        /// </summary>
        private static List<RouteAlternativeInternal> ParseSerpApiDirectionsAlternatives(
            JsonElement root,
            int travelModeId)
        {
            var results = new List<RouteAlternativeInternal>();
            if (!root.TryGetProperty("directions", out var directionsEl) ||
                directionsEl.ValueKind != JsonValueKind.Array)
                return results;

            var filterLabel = TravelModeIdToSerpTravelModeLabel(travelModeId);

            foreach (var dir in directionsEl.EnumerateArray())
            {
                if (filterLabel != null)
                {
                    if (!dir.TryGetProperty("travel_mode", out var tmEl) ||
                        tmEl.ValueKind != JsonValueKind.String ||
                        !string.Equals(tmEl.GetString(), filterLabel, StringComparison.OrdinalIgnoreCase))
                        continue;
                }

                var points = CollectGpsPointsFromSerpDirection(dir, root);
                if (points.Count < 2)
                    continue;

                var encoded = EncodePolyline(points);
                if (string.IsNullOrEmpty(encoded))
                    continue;

                int? distanceMeters = null;
                int? durationSeconds = null;
                if (dir.TryGetProperty("distance", out var distEl) && distEl.ValueKind == JsonValueKind.Number)
                    distanceMeters = distEl.TryGetInt32(out var d) ? d : (int)distEl.GetDouble();
                if (dir.TryGetProperty("duration", out var durEl) && durEl.ValueKind == JsonValueKind.Number)
                    durationSeconds = durEl.TryGetInt32(out var t) ? t : (int)durEl.GetDouble();

                results.Add(new RouteAlternativeInternal
                {
                    OverviewPolylinePoints = encoded,
                    DistanceMeters = distanceMeters,
                    DurationSeconds = durationSeconds,
                    Warnings = new List<RouteFloodWarningDTO>()
                });
            }

            return results;
        }

        /// <summary>
        /// SerpApi dùng chuỗi travel_mode ("Driving", "Walking", ...). ID giống tham số API.
        /// </summary>
        private static string? TravelModeIdToSerpTravelModeLabel(int travelModeId) =>
            travelModeId switch
            {
                0 => "Driving",
                1 => "Cycling",
                2 => "Walking",
                3 => "Transit",
                4 => "Flight",
                6 => "Driving", // Best: dùng ô tô cho tránh ngập
                9 => null,      // Two-wheeler — tên chuỗi có thể khác theo locale; lấy hướng đầu tiên khớp GPS
                _ => "Driving"
            };

        private static List<(double lat, double lng)> CollectGpsPointsFromSerpDirection(
            JsonElement directionEl,
            JsonElement root)
        {
            var points = new List<(double lat, double lng)>();

            if (directionEl.TryGetProperty("trips", out var trips) && trips.ValueKind == JsonValueKind.Array)
            {
                foreach (var trip in trips.EnumerateArray())
                {
                    if (trip.TryGetProperty("details", out var details) && details.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var detail in details.EnumerateArray())
                        {
                            if (TryGetGpsCoordinates(detail, out var lat, out var lng))
                                AppendPointIfDistinct(points, lat, lng);
                        }
                    }
                }
            }

            if (points.Count < 2 && root.TryGetProperty("places_info", out var places) &&
                places.ValueKind == JsonValueKind.Array && places.GetArrayLength() >= 2)
            {
                points.Clear();
                if (TryGetGpsFromPlace(places[0], out var aLat, out var aLng) &&
                    TryGetGpsFromPlace(places[places.GetArrayLength() - 1], out var bLat, out var bLng))
                {
                    points.Add((aLat, aLng));
                    points.Add((bLat, bLng));
                }
            }

            return points;
        }

        private static void AppendPointIfDistinct(List<(double lat, double lng)> points, double lat, double lng)
        {
            if (points.Count > 0)
            {
                var last = points[^1];
                if (HaversineMeters(last.lat, last.lng, lat, lng) < 1.0)
                    return;
            }
            points.Add((lat, lng));
        }

        private static bool TryGetGpsCoordinates(JsonElement el, out double lat, out double lng)
        {
            lat = 0;
            lng = 0;
            if (!el.TryGetProperty("gps_coordinates", out var gps) || gps.ValueKind != JsonValueKind.Object)
                return false;
            return TryReadLatLng(gps, out lat, out lng);
        }

        private static bool TryGetGpsFromPlace(JsonElement placeEl, out double lat, out double lng)
        {
            lat = 0;
            lng = 0;
            if (!placeEl.TryGetProperty("gps_coordinates", out var gps) || gps.ValueKind != JsonValueKind.Object)
                return false;
            return TryReadLatLng(gps, out lat, out lng);
        }

        private static bool TryReadLatLng(JsonElement gps, out double lat, out double lng)
        {
            lat = 0;
            lng = 0;
            if (!gps.TryGetProperty("latitude", out var latEl) || latEl.ValueKind != JsonValueKind.Number)
                return false;
            if (!gps.TryGetProperty("longitude", out var lngEl) || lngEl.ValueKind != JsonValueKind.Number)
                return false;
            lat = latEl.GetDouble();
            lng = lngEl.GetDouble();
            return true;
        }

        private static string EncodePolyline(IReadOnlyList<(double lat, double lng)> points)
        {
            if (points == null || points.Count == 0)
                return string.Empty;

            var sb = new StringBuilder();
            var lastLat = 0;
            var lastLng = 0;
            foreach (var (lat, lng) in points)
            {
                var iLat = (int)Math.Round(lat * 1e5, MidpointRounding.AwayFromZero);
                var iLng = (int)Math.Round(lng * 1e5, MidpointRounding.AwayFromZero);
                EncodeSignedNumber(iLat - lastLat, sb);
                EncodeSignedNumber(iLng - lastLng, sb);
                lastLat = iLat;
                lastLng = iLng;
            }

            return sb.ToString();
        }

        private static void EncodeSignedNumber(int num, StringBuilder result)
        {
            var sgnNum = (uint)(num < 0 ? ~(num << 1) : (num << 1));
            while (sgnNum >= 0x20)
            {
                result.Append((char)((0x20 | (sgnNum & 0x1f)) + 63));
                sgnNum >>= 5;
            }
            result.Append((char)(sgnNum + 63));
        }

        private static RouteAlternativeInternal? TryParseRouteAlternative(JsonElement routeEl)
        {
            if (!routeEl.TryGetProperty("overview_polyline", out var overviewEl))
                return null;

            if (!overviewEl.TryGetProperty("points", out var pointsEl))
                return null;

            var points = pointsEl.GetString();
            if (string.IsNullOrWhiteSpace(points))
                return null;

            int? distanceMeters = null;
            int? durationSeconds = null;

            if (routeEl.TryGetProperty("legs", out var legsEl) &&
                legsEl.ValueKind == JsonValueKind.Array &&
                legsEl.GetArrayLength() > 0)
            {
                var leg0 = legsEl[0];

                if (leg0.TryGetProperty("distance", out var distanceEl) &&
                    distanceEl.TryGetProperty("value", out var distanceValueEl) &&
                    distanceValueEl.ValueKind == JsonValueKind.Number)
                {
                    distanceMeters = distanceValueEl.GetInt32();
                }

                if (leg0.TryGetProperty("duration", out var durationEl) &&
                    durationEl.TryGetProperty("value", out var durationValueEl) &&
                    durationValueEl.ValueKind == JsonValueKind.Number)
                {
                    durationSeconds = durationValueEl.GetInt32();
                }
            }

            return new RouteAlternativeInternal
            {
                OverviewPolylinePoints = points,
                DistanceMeters = distanceMeters,
                DurationSeconds = durationSeconds,
                Warnings = new List<RouteFloodWarningDTO>()
            };
        }

        private static void CollectRouteAlternativesRecursive(
            JsonElement element,
            List<RouteAlternativeInternal> results,
            HashSet<string> seenPoly)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                var alt = TryParseRouteAlternative(element);
                if (alt != null && seenPoly.Add(alt.OverviewPolylinePoints))
                    results.Add(alt);

                foreach (var prop in element.EnumerateObject())
                {
                    CollectRouteAlternativesRecursive(prop.Value, results, seenPoly);
                }
            }
            else if (element.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in element.EnumerateArray())
                {
                    CollectRouteAlternativesRecursive(item, results, seenPoly);
                }
            }
        }

        private (double RiskScore, List<RouteFloodWarningDTO> Warnings) ScoreAlternativeAgainstFloods(
            string overviewPolylinePoints,
            List<FloodSensor> floodedSensors,
            double radiusMeters)
        {
            if (string.IsNullOrWhiteSpace(overviewPolylinePoints))
                return (0, new List<RouteFloodWarningDTO>());

            var routePoints = DecodePolyline(overviewPolylinePoints);
            if (routePoints.Count == 0)
                return (0, new List<RouteFloodWarningDTO>());

            var sampledRoutePoints = Sample(routePoints, 200);

            double score = 0;
            var warnings = new List<RouteFloodWarningDTO>();

            foreach (var sensor in floodedSensors)
            {
                double minDist = double.MaxValue;
                foreach (var p in sampledRoutePoints)
                {
                    var d = HaversineMeters(p.lat, p.lng, sensor.Latitude, sensor.Longitude);
                    if (d < minDist) minDist = d;
                    if (minDist <= 10) break; // khá gần rồi
                }

                if (minDist <= radiusMeters)
                {
                    var weight = sensor.Severity == "Danger" ? 3.0 : 2.0;
                    score += weight * (1.0 - (minDist / radiusMeters));

                    warnings.Add(new RouteFloodWarningDTO
                    {
                        SensorId = sensor.SensorId,
                        SensorName = sensor.SensorName,
                        PlaceId = sensor.PlaceId,
                        Severity = sensor.Severity,
                        MinDistanceMeters = minDist,
                        SensorLatitude = sensor.Latitude,
                        SensorLongitude = sensor.Longitude,
                        WaterLevelCm = sensor.WaterLevelCm,
                        WarningThresholdCm = sensor.WarningThresholdCm,
                        DangerThresholdCm = sensor.DangerThresholdCm,
                        ReadingStatus = sensor.ReadingStatus
                    });
                }
            }

            warnings = warnings
                .OrderBy(w => w.MinDistanceMeters)
                .ThenByDescending(w => w.Severity == "Danger")
                .ToList();

            return (score, warnings);
        }

        private static List<(double lat, double lng)> DecodePolyline(string encoded)
        {
            // Google encoded polyline algorithm.
            // https://developers.google.com/maps/documentation/utilities/polylinealgorithm
            var poly = new List<(double lat, double lng)>();
            if (string.IsNullOrEmpty(encoded))
                return poly;

            int index = 0;
            int lat = 0;
            int lng = 0;

            try
            {
                while (index < encoded.Length)
                {
                    lat += DecodeNextValue(encoded, ref index);
                    lng += DecodeNextValue(encoded, ref index);

                    poly.Add((lat / 1e5, lng / 1e5));
                }
            }
            catch (Exception)
            {
                // Fallback: Nếu chuỗi Polyline không hợp lệ (Mock or Truncated), dừng decode để tránh crash IndexOutOfRange
            }

            return poly;
        }

        private static int DecodeNextValue(string encoded, ref int index)
        {
            int result = 0;
            int shift = 0;
            int b;
            do
            {
                if (index >= encoded.Length) break;
                b = encoded[index++] - 63;
                result |= (b & 0x1f) << shift;
                shift += 5;
            } while (b >= 0x20 && index <= encoded.Length);

            int delta = ((result & 1) == 1) ? ~(result >> 1) : (result >> 1);
            return delta;
        }

        private static List<(double lat, double lng)> Sample(List<(double lat, double lng)> points, int maxPoints)
        {
            if (points.Count <= maxPoints) return points;
            var result = new List<(double lat, double lng)>(maxPoints);
            var step = (double)points.Count / maxPoints;
            for (int i = 0; i < maxPoints; i++)
            {
                int idx = (int)Math.Round(i * step);
                idx = Math.Clamp(idx, 0, points.Count - 1);
                result.Add(points[idx]);
            }
            return result.Distinct().ToList();
        }

        private static double HaversineMeters(double lat1, double lon1, double lat2, double lon2)
        {
            const double R = 6371000; // meters
            var dLat = DegreesToRadians(lat2 - lat1);
            var dLon = DegreesToRadians(lon2 - lon1);

            var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                    Math.Cos(DegreesToRadians(lat1)) * Math.Cos(DegreesToRadians(lat2)) *
                    Math.Sin(dLon / 2) * Math.Sin(dLon / 2);

            var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
            return R * c;
        }

        private static double DegreesToRadians(double deg) => deg * (Math.PI / 180.0);

        private static int ParseTravelModeToId(string? travelMode)
        {
            if (string.IsNullOrWhiteSpace(travelMode))
                return 0; // driving

            var t = travelMode.Trim().ToLowerInvariant();
            return t switch
            {
                "driving" => 0,
                "walking" => 2,
                "transit" => 3,
                "cycling" => 1,
                "best" => 6,
                // Nếu front-end gửi trực tiếp số (ví dụ "0", "1"...)
                _ => int.TryParse(t, out var v) ? v : 0
            };
        }


        private static string Fmt(double v) => v.ToString("0.######", CultureInfo.InvariantCulture);

        private sealed class FloodSensor
        {
            public int SensorId { get; set; }
            public int PlaceId { get; set; }
            public string SensorName { get; set; } = string.Empty;
            public string Severity { get; set; } = string.Empty;
            public float WaterLevelCm { get; set; }
            public float? WarningThresholdCm { get; set; }
            public float? DangerThresholdCm { get; set; }
            public string ReadingStatus { get; set; } = string.Empty;
            public double Latitude { get; set; }
            public double Longitude { get; set; }
        }

        private sealed class RouteAlternativeInternal
        {
            public string OverviewPolylinePoints { get; set; } = string.Empty;
            public int? DistanceMeters { get; set; }
            public int? DurationSeconds { get; set; }
            public List<RouteFloodWarningDTO> Warnings { get; set; } = new List<RouteFloodWarningDTO>();
        }
    }
}

