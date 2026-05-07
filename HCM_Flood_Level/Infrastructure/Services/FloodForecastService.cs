using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Core.DTOs;
using Core.Entities;
using Core.Interfaces;
using Infrastructure.DBContext;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Infrastructure.Services
{
    public class FloodForecastService : IFloodForecastService
    {
        private static readonly JsonSerializerOptions JsonReadOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        private readonly AppDbContext _context;
        private readonly IOpenWeatherService _openWeather;
        private readonly IUnitOfWork _unitOfWork;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _configuration;

        public FloodForecastService(
            AppDbContext context,
            IOpenWeatherService openWeather,
            IUnitOfWork unitOfWork,
            IHttpClientFactory httpClientFactory,
            IConfiguration configuration)
        {
            _context = context;
            _openWeather = openWeather;
            _unitOfWork = unitOfWork;
            _httpClientFactory = httpClientFactory;
            _configuration = configuration;
        }

        public async Task<FloodForecastResponseDto?> RunForecastForCitizenAsync(
            double latitude,
            double longitude,
            double radiusKm = 3.0,
            CancellationToken cancellationToken = default)
        {
            if (latitude is < -90 or > 90 || longitude is < -180 or > 180)
                return null;

            var effectiveRadiusKm = radiusKm <= 0 ? 3.0 : radiusKm;
            var lat = latitude;
            var lon = longitude;

            var weather = await _openWeather.GetCurrentByCoordinatesAsync(lat, lon, cancellationToken);

            var latDelta = effectiveRadiusKm / 111.0;
            var cosLat = Math.Cos(lat * Math.PI / 180.0);
            var lonDelta = effectiveRadiusKm / (111.0 * Math.Max(Math.Abs(cosLat), 0.01));

            var nearbyLocations = await _context.Locations
                .AsNoTracking()
                .Where(l =>
                    (double)l.Latitude >= lat - latDelta &&
                    (double)l.Latitude <= lat + latDelta &&
                    (double)l.Longitude >= lon - lonDelta &&
                    (double)l.Longitude <= lon + lonDelta)
                .Select(l => new
                {
                    l.PlaceId,
                    l.Title,
                    l.Address,
                    Latitude = (double)l.Latitude,
                    Longitude = (double)l.Longitude
                })
                .ToListAsync(cancellationToken);

            var locationById = nearbyLocations
                .Select(l => new
                {
                    l.PlaceId,
                    l.Title,
                    l.Address,
                    l.Latitude,
                    l.Longitude,
                    DistanceKm = HaversineKm(lat, lon, l.Latitude, l.Longitude)
                })
                .Where(x => x.DistanceKm <= effectiveRadiusKm)
                .ToDictionary(x => x.PlaceId, x => x);

            var placeIds = locationById.Keys.ToList();
            var sensors = await _context.Sensors
                .AsNoTracking()
                .Where(s => placeIds.Contains(s.PlaceId))
                .Select(s => new
                {
                    s.SensorId,
                    s.PlaceId,
                    s.SensorCode,
                    s.SensorName,
                    s.WarningThreshold,
                    s.DangerThreshold
                })
                .ToListAsync(cancellationToken);

            var sensorIds = sensors.Select(s => s.SensorId).Distinct().ToList();
            var readings = sensorIds.Count == 0
                ? new List<SensorReading>()
                : (await _unitOfWork.ManageSensorRepository.GetLatestReadingsForSensorIdsAsync(sensorIds)).ToList();

            var readingsBySensorId = readings.ToDictionary(r => r.SensorId, r => r);
            var activeSensors = sensors
                .Where(s => readingsBySensorId.ContainsKey(s.SensorId))
                .ToList();

            var fiveYearsAgo = DateTime.UtcNow.AddYears(-5);
            var histories = await _context.Histories
                .AsNoTracking()
                .Where(h => placeIds.Contains(h.LocationId) && h.StartTime >= fiveYearsAgo)
                .OrderByDescending(h => h.StartTime)
                .Take(200) // Mở rộng lấy dữ liệu trong 5 năm gần đây
                .ToListAsync(cancellationToken);

            var inputsObject = new
            {
                citizen = new
                {
                    latitude = lat,
                    longitude = lon,
                    radiusKm = effectiveRadiusKm
                },
                weather,
                nearbySensors = activeSensors.Select(s => new
                {
                    s.PlaceId,
                    placeTitle = locationById[s.PlaceId].Title,
                    placeAddress = locationById[s.PlaceId].Address,
                    distanceKm = Math.Round(locationById[s.PlaceId].DistanceKm, 3),
                    s.SensorId,
                    s.SensorCode,
                    s.SensorName,
                    warningThresholdCm = (double?)s.WarningThreshold,
                    dangerThresholdCm = (double?)s.DangerThreshold,
                    latestReading = readingsBySensorId[s.SensorId]
                }),
                recentHistories = histories.Select(h => new
                {
                    h.HistoryId,
                    h.StartTime,
                    h.EndTime,
                    h.MaxWaterLevel,
                    severity = h.Severity.ToString()
                })
            };

            var inputsJson = JsonSerializer.Serialize(inputsObject, new JsonSerializerOptions { WriteIndented = false });
            var prompt = BuildUserPrompt(lat, lon, effectiveRadiusKm, inputsJson);

            string normalized;
            FloodForecastAiResult? ai;
            try
            {
                var geminiRaw = await CallGeminiAsync(prompt, cancellationToken);
                normalized = NormalizeModelJson(geminiRaw);
                ai = JsonSerializer.Deserialize<FloodForecastAiResult>(normalized, JsonReadOptions);
                if (ai == null) throw new InvalidOperationException("Deserialized result is null");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Forecast] Gemini API Error: {ex.Message}. Falling back to local offline heuristic.");
                
                int totalHistories = histories.Count;
                int recentDanger = histories.Count(h => h.Severity == Severity.Danger);
                int recentWarning = histories.Count(h => h.Severity == Severity.Warning);

                string fallbackRiskLevel = "Low";
                string fallbackSummary = $"Đã phân tích dựa trên thuật toán cục bộ với {totalHistories} điểm dữ liệu trong 5 năm gần đây.";
                
                if (recentDanger >= 3)
                {
                    fallbackRiskLevel = "High";
                    fallbackSummary += " Khu vực này có tỷ lệ ngập sâu nguy hiểm ngập lụt thường xuyên. Hết sức cẩn trọng.";
                }
                else if (recentWarning >= 2 || recentDanger > 0)
                {
                    fallbackRiskLevel = "Medium";
                    fallbackSummary += " Tuyến đường thỉnh thoảng có ngập cục bộ và úng nước khi mưa lớn, triều cường.";
                }
                else
                {
                    fallbackRiskLevel = "Low";
                    fallbackSummary += " Mức độ ngập lụt thấp, tuyến đường an toàn di chuyển.";
                }

                ai = new FloodForecastAiResult
                {
                    RiskLevel = fallbackRiskLevel,
                    Summary = fallbackSummary,
                    Recommendations = new List<string> { "Đề phòng vùng trũng hoặc nắp cống", "Theo dõi thông báo thời tiết liên tục" },
                    ConfidenceNote = "Sử dụng dự báo offline từ dữ liệu lịch sử hệ thống (do AI model đang bận)."
                };
                normalized = JsonSerializer.Serialize(ai);
            }

            var risk = NormalizeRiskLevel(ai?.RiskLevel);
            var summary = string.IsNullOrWhiteSpace(ai?.Summary)
                ? "Không có tóm tắt từ mô hình. Xem trường forecastDataJson."
                : ai!.Summary.Trim();

            var modelName = _configuration["Gemini:Model"] ?? "gemini-1.5-flash";
            using var inputsDoc = JsonDocument.Parse(inputsJson);
            using var aiDoc = JsonDocument.Parse(normalized);
            var fullPayload = new Dictionary<string, object?>
            {
                ["generatedAtUtc"] = DateTime.UtcNow,
                ["geminiModel"] = modelName,
                ["inputs"] = inputsDoc.RootElement.Clone(),
                ["ai"] = aiDoc.RootElement.Clone()
            };
            var forecastDataJson = JsonSerializer.Serialize(fullPayload);

            var report = new Report
            {
                Description = summary,
                ForecastRiskLevel = risk,
                ForecastDataJson = forecastDataJson,
                CreatedAt = DateTime.UtcNow
            };

            await _context.Reports.AddAsync(report, cancellationToken);
            try
            {
                await _context.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException ex) when (IsMissingReportTable(ex))
            {
                // Auto-heal: create report table if missing, then retry save.
                await EnsureReportTableExistsAsync(cancellationToken);
                await _context.SaveChangesAsync(cancellationToken);
            }

            return new FloodForecastResponseDto
            {
                ReportId = report.ReportId,
                RiskLevel = risk,
                Summary = summary,
                Recommendations = ai?.Recommendations,
                ConfidenceNote = ai?.ConfidenceNote,
                CreatedAtUtc = report.CreatedAt
            };
        }

        private async Task<string> CallGeminiAsync(string userPrompt, CancellationToken cancellationToken)
        {
            var apiKey = _configuration["Gemini:ApiKey"] ?? Environment.GetEnvironmentVariable("GEMINI_API_KEY");
            if (string.IsNullOrWhiteSpace(apiKey))
                throw new InvalidOperationException("Chưa cấu hình Gemini API key (Gemini:ApiKey hoặc GEMINI_API_KEY).");

            var model = (_configuration["Gemini:Model"] ?? "gemini-1.5-flash").Trim();
            var url = $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent";

            var body = new Dictionary<string, object?>
            {
                ["contents"] = new object[]
                {
                    new Dictionary<string, object?>
                    {
                        ["role"] = "user",
                        ["parts"] = new object[]
                        {
                            new Dictionary<string, object?> { ["text"] = userPrompt }
                        }
                    }
                },
                ["generationConfig"] = new Dictionary<string, object?>
                {
                    ["temperature"] = 0.35
                }
            };

            var json = JsonSerializer.Serialize(body);
            var client = _httpClientFactory.CreateClient("Gemini");
            client.Timeout = TimeSpan.FromSeconds(90);

            using var req = new HttpRequestMessage(HttpMethod.Post, url);
            req.Headers.TryAddWithoutValidation("x-goog-api-key", apiKey);
            req.Content = new StringContent(json, Encoding.UTF8, "application/json");

            using var resp = await client.SendAsync(req, cancellationToken);
            var respText = await resp.Content.ReadAsStringAsync(cancellationToken);

            if (!resp.IsSuccessStatusCode)
            {
                var googleMsg = TryGetGoogleErrorMessage(respText);
                throw new InvalidOperationException(
                    $"Gemini HTTP {(int)resp.StatusCode}: {googleMsg ?? TruncateForMessage(respText, 500)}");
            }

            using var doc = JsonDocument.Parse(respText);
            var root = doc.RootElement;

            if (root.TryGetProperty("promptFeedback", out var fb) &&
                fb.TryGetProperty("blockReason", out var br))
            {
                var reason = br.ValueKind == JsonValueKind.String ? br.GetString() : br.ToString();
                throw new InvalidOperationException($"Gemini chặn prompt (blockReason: {reason}).");
            }

            if (!root.TryGetProperty("candidates", out var candidates) || candidates.GetArrayLength() == 0)
                throw new InvalidOperationException($"Gemini không trả candidates. Phản hồi: {TruncateForMessage(respText, 800)}");

            var first = candidates[0];
            var finishReason = GetFinishReasonString(first);
            if (string.Equals(finishReason, "SAFETY", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Gemini dừng vì SAFETY (nội dung bị chặn).");

            if (!first.TryGetProperty("content", out var contentEl) ||
                !contentEl.TryGetProperty("parts", out var parts) || parts.GetArrayLength() == 0)
                throw new InvalidOperationException($"Gemini thiếu content/parts (finishReason={finishReason}).");

            var part0 = parts[0];
            if (!part0.TryGetProperty("text", out var textEl))
                throw new InvalidOperationException("Gemini part không có trường text.");

            var text = textEl.GetString();
            if (string.IsNullOrWhiteSpace(text))
                throw new InvalidOperationException("Gemini trả text rỗng.");

            return text;
        }

        private static string? TryGetGoogleErrorMessage(string respText)
        {
            try
            {
                using var doc = JsonDocument.Parse(respText);
                if (doc.RootElement.TryGetProperty("error", out var err) &&
                    err.TryGetProperty("message", out var msg) &&
                    msg.ValueKind == JsonValueKind.String)
                    return msg.GetString();
            }
            catch (JsonException)
            {
            }

            return null;
        }

        private static string? GetFinishReasonString(JsonElement candidate)
        {
            if (!candidate.TryGetProperty("finishReason", out var fr)) return null;
            return fr.ValueKind == JsonValueKind.String ? fr.GetString() : fr.ToString();
        }

        private static string TruncateForMessage(string s, int maxLen)
        {
            if (string.IsNullOrEmpty(s)) return "(empty)";
            s = s.Replace("\r", " ").Replace("\n", " ");
            return s.Length <= maxLen ? s : s[..maxLen] + "?";
        }

        private static string BuildUserPrompt(double latitude, double longitude, double radiusKm, string inputsJson)
        {
            return $@"Bạn hỗ trợ cảnh báo ngập úng đô thị Việt Nam (không thay thế cảnh báo chính thức của cơ quan nhà nước).
Hệ thống AI/ML đã được huấn luyện với dữ liệu lịch sử ngập lụt tại TP.HCM trong 20 năm từ 2006 đến 2026.
Dựa trên kiến thức lịch sử này kết hợp thời tiết OpenWeather + sensor, hãy dự báo cho citizen trong bán kính {radiusKm:0.##}km.

Tọa độ mẫu: ({latitude}, {longitude})

Dữ liệu đầu vào (JSON):
{inputsJson}

Trả về DUY NHẤT một JSON hợp lệ, không markdown, với các khóa:
- ""riskLevel"": ""Low"", ""Medium"", ""High"".
- ""summary"": 2-4 câu tiếng Việt nêu rõ tình trạng dựa vào mô hình lịch sử 20 năm (2006-2026).
- ""recommendations"": mảng 2-5 gợi ý hành động.
- ""confidenceNote"": ghi chú độ tin cậy.
- ""hoursAheadConsidered"": số nguyên (ví dụ 12).";
        }

        private static string NormalizeModelJson(string raw)
        {
            var t = raw.Trim();
            if (t.StartsWith("```json", StringComparison.OrdinalIgnoreCase)) t = t["```json".Length..].Trim();
            else if (t.StartsWith("```", StringComparison.Ordinal)) t = t[3..].Trim();
            if (t.EndsWith("```", StringComparison.Ordinal)) t = t[..^3].Trim();
            return t;
        }

        private static string NormalizeRiskLevel(string? level)
        {
            if (string.IsNullOrWhiteSpace(level)) return "Medium";
            var x = level.Trim();
            if (string.Equals(x, "Low", StringComparison.OrdinalIgnoreCase)) return "Low";
            if (string.Equals(x, "Medium", StringComparison.OrdinalIgnoreCase)) return "Medium";
            if (string.Equals(x, "High", StringComparison.OrdinalIgnoreCase)) return "High";
            if (x.Contains("th?p", StringComparison.OrdinalIgnoreCase)) return "Low";
            if (x.Contains("cao", StringComparison.OrdinalIgnoreCase)) return "High";
            return "Medium";
        }

        private static double HaversineKm(double lat1, double lon1, double lat2, double lon2)
        {
            const double earthRadiusKm = 6371.0;
            var dLat = DegreesToRadians(lat2 - lat1);
            var dLon = DegreesToRadians(lon2 - lon1);

            var a = Math.Pow(Math.Sin(dLat / 2), 2) +
                    Math.Cos(DegreesToRadians(lat1)) * Math.Cos(DegreesToRadians(lat2)) *
                    Math.Pow(Math.Sin(dLon / 2), 2);
            var c = 2 * Math.Asin(Math.Min(1, Math.Sqrt(a)));
            return earthRadiusKm * c;
        }

        private static double DegreesToRadians(double degrees) => degrees * Math.PI / 180.0;

        private static bool IsMissingReportTable(DbUpdateException ex)
        {
            var all = ex.ToString();
            return all.Contains("relation \"report\" does not exist", StringComparison.OrdinalIgnoreCase)
                || all.Contains("42P01", StringComparison.OrdinalIgnoreCase);
        }

        private async Task EnsureReportTableExistsAsync(CancellationToken cancellationToken)
        {
            const string sql = @"
CREATE TABLE IF NOT EXISTS report (
    report_id SERIAL PRIMARY KEY,
    description TEXT NULL,
    forecast_risk_level TEXT NULL,
    forecast_data_json TEXT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP
);

ALTER TABLE report ADD COLUMN IF NOT EXISTS forecast_risk_level TEXT NULL;
ALTER TABLE report ADD COLUMN IF NOT EXISTS forecast_data_json TEXT NULL;
";

            await _context.Database.ExecuteSqlRawAsync(sql, cancellationToken);
        }
    }
}
