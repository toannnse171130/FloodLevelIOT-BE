using Core.DTOs;
using Core.Interfaces;
using FakeItEasy;
using Microsoft.AspNetCore.Mvc;
using WebAPI.Controllers;
using WebAPI.Errors;

namespace FloodLevelIOT_BE.Test.Controllers;

public class FloodForecastControllerTest
{
    private readonly IFloodForecastService _floodForecastService;
    private const string InvalidCoordinateMessage = "Tọa độ không hợp lệ (lat: -90..90, lon: -180..180).";

    public FloodForecastControllerTest()
    {
        _floodForecastService = A.Fake<IFloodForecastService>();
    }

    [Fact]
    public async Task GetFloodForecast_WithValidAreaId_ReturnsOkWithForecastData()
    {
        var dto = new FloodForecastRequestDto { Latitude = 10.8231, Longitude = 106.6297, RadiusKm = 5 };
        var response = new FloodForecastResponseDto
        {
            ReportId = 1,
            RiskLevel = "Low",
            Summary = "Conditions stable.",
            Recommendations = new List<string> { "Monitor local alerts." },
            CreatedAtUtc = DateTime.UtcNow
        };
        A.CallTo(() => _floodForecastService.RunForecastForCitizenAsync(dto.Latitude, dto.Longitude, 5.0, A<int?>._, A<CancellationToken>._))
            .Returns(response);
        var controller = new FloodForecastController(_floodForecastService);

        var result = await controller.RunForecast(dto, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Same(response, ok.Value);
    }

    [Fact]
    public async Task GetFloodForecast_WithInvalidAreaId_ReturnsNotFound()
    {
        var dto = new FloodForecastRequestDto { Latitude = 10.8, Longitude = 106.6, RadiusKm = 2 };
        A.CallTo(() => _floodForecastService.RunForecastForCitizenAsync(dto.Latitude, dto.Longitude, 2.0, A<int?>._, A<CancellationToken>._))
            .Returns((FloodForecastResponseDto?)null);
        var controller = new FloodForecastController(_floodForecastService);

        var result = await controller.RunForecast(dto, CancellationToken.None);

        var bad = Assert.IsType<BadRequestObjectResult>(result);
        var body = Assert.IsType<BaseCommentResponse>(bad.Value);
        Assert.Equal(400, body.Statuscodes);
        Assert.Equal("Không thể khởi tạo mô hình dự báo (tọa độ không hợp lệ).", body.Message);
    }

    [Fact]
    public async Task GetFloodRisk_WithValidSensorId_ReturnsOkWithRiskLevel()
    {
        var dto = new FloodForecastRequestDto { Latitude = 21.0285, Longitude = 105.8542, RadiusKm = 3 };
        var response = new FloodForecastResponseDto
        {
            ReportId = 2,
            RiskLevel = "Moderate",
            Summary = "Elevated water levels possible.",
            Recommendations = new List<string>(),
            CreatedAtUtc = DateTime.UtcNow
        };
        A.CallTo(() => _floodForecastService.RunForecastForCitizenAsync(dto.Latitude, dto.Longitude, 3.0, A<int?>._, A<CancellationToken>._))
            .Returns(response);
        var controller = new FloodForecastController(_floodForecastService);

        var result = await controller.RunForecast(dto, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var payload = Assert.IsType<FloodForecastResponseDto>(ok.Value);
        Assert.Equal("Moderate", payload.RiskLevel);
    }

    [Fact]
    public async Task GetFloodAlert_WithHighRiskArea_ReturnsOkWithAlertInfo()
    {
        var dto = new FloodForecastRequestDto { Latitude = 16.0471, Longitude = 108.2068, RadiusKm = 4 };
        var response = new FloodForecastResponseDto
        {
            ReportId = 3,
            RiskLevel = "High",
            Summary = "Flood risk elevated in the selected radius.",
            Recommendations = new List<string> { "Avoid low-lying roads.", "Move valuables to higher floors." },
            ConfidenceNote = "Based on recent sensor and rainfall signals.",
            CreatedAtUtc = DateTime.UtcNow
        };
        A.CallTo(() => _floodForecastService.RunForecastForCitizenAsync(dto.Latitude, dto.Longitude, 4.0, A<int?>._, A<CancellationToken>._))
            .Returns(response);
        var controller = new FloodForecastController(_floodForecastService);

        var result = await controller.RunForecast(dto, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var payload = Assert.IsType<FloodForecastResponseDto>(ok.Value);
        Assert.Equal("High", payload.RiskLevel);
        Assert.NotNull(payload.Recommendations);
        Assert.Equal(2, payload.Recommendations!.Count);
    }

    [Fact]
    public async Task GetHistoricalFloodData_WithValidPeriod_ReturnsOkWithHistoricalData()
    {
        var dto = new FloodForecastRequestDto { Latitude = 10.0, Longitude = 106.0, RadiusKm = 0 };
        var response = new FloodForecastResponseDto
        {
            ReportId = 4,
            RiskLevel = "Low",
            Summary = "Historical context included in model run.",
            CreatedAtUtc = DateTime.UtcNow
        };
        A.CallTo(() => _floodForecastService.RunForecastForCitizenAsync(10.0, 106.0, 3.0, A<int?>._, A<CancellationToken>._))
            .Returns(response);
        var controller = new FloodForecastController(_floodForecastService);

        var result = await controller.RunForecast(dto, CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
        A.CallTo(() => _floodForecastService.RunForecastForCitizenAsync(10.0, 106.0, 3.0, A<int?>._, A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }

    /// <summary>UTCID-06: RunForecast với body null → 400, không gọi service.</summary>
    [Fact]
    public async Task RunForecast_WithNullPayload_ReturnsBadRequest()
    {
        var controller = new FloodForecastController(_floodForecastService);

        var result = await controller.RunForecast(null!, CancellationToken.None);

        var bad = Assert.IsType<BadRequestObjectResult>(result);
        var body = Assert.IsType<BaseCommentResponse>(bad.Value);
        Assert.Equal(400, body.Statuscodes);
        Assert.Equal("Payload không hợp lệ.", body.Message);
        A.CallTo(() => _floodForecastService.RunForecastForCitizenAsync(A<double>._, A<double>._, A<double>._, A<int?>._, A<CancellationToken>._))
            .MustNotHaveHappened();
    }

    /// <summary>UTCID-07: vĩ độ ngoài phạm vi (vd. 91) → 400, không gọi service.</summary>
    [Fact]
    public async Task RunForecast_WithInvalidLatitude_ReturnsBadRequest()
    {
        var dto = new FloodForecastRequestDto { Latitude = 91, Longitude = 106.6, RadiusKm = 2 };
        var controller = new FloodForecastController(_floodForecastService);

        var result = await controller.RunForecast(dto, CancellationToken.None);

        var bad = Assert.IsType<BadRequestObjectResult>(result);
        var body = Assert.IsType<BaseCommentResponse>(bad.Value);
        Assert.Equal(400, body.Statuscodes);
        Assert.Equal(InvalidCoordinateMessage, body.Message);
        A.CallTo(() => _floodForecastService.RunForecastForCitizenAsync(A<double>._, A<double>._, A<double>._, A<int?>._, A<CancellationToken>._))
            .MustNotHaveHappened();
    }

    /// <summary>UTCID-07: kinh độ ngoài phạm vi (vd. 181) → 400, không gọi service.</summary>
    [Fact]
    public async Task RunForecast_WithInvalidLongitude_ReturnsBadRequest()
    {
        var dto = new FloodForecastRequestDto { Latitude = 10.0, Longitude = 181, RadiusKm = 2 };
        var controller = new FloodForecastController(_floodForecastService);

        var result = await controller.RunForecast(dto, CancellationToken.None);

        var bad = Assert.IsType<BadRequestObjectResult>(result);
        var body = Assert.IsType<BaseCommentResponse>(bad.Value);
        Assert.Equal(400, body.Statuscodes);
        Assert.Equal(InvalidCoordinateMessage, body.Message);
        A.CallTo(() => _floodForecastService.RunForecastForCitizenAsync(A<double>._, A<double>._, A<double>._, A<int?>._, A<CancellationToken>._))
            .MustNotHaveHappened();
    }

    /// <summary>UTCID-08: lỗi dịch vụ — <see cref="InvalidOperationException"/> → 502, Message = nội dung exception.</summary>
    [Fact]
    public async Task RunForecast_WhenServiceThrowsInvalidOperationException_Returns502()
    {
        const string message = "Forecast model unavailable.";
        var dto = new FloodForecastRequestDto { Latitude = 10.8, Longitude = 106.6, RadiusKm = 2 };
        A.CallTo(() => _floodForecastService.RunForecastForCitizenAsync(dto.Latitude, dto.Longitude, 2.0, A<int?>._, A<CancellationToken>._))
            .ThrowsAsync(new InvalidOperationException(message));
        var controller = new FloodForecastController(_floodForecastService);

        var result = await controller.RunForecast(dto, CancellationToken.None);

        var error = Assert.IsType<ObjectResult>(result);
        Assert.Equal(502, error.StatusCode);
        var body = Assert.IsType<BaseCommentResponse>(error.Value);
        Assert.Equal(502, body.Statuscodes);
        Assert.Equal(message, body.Message);
    }

    /// <summary>UTCID-08: lỗi dịch vụ — exception chung → 500, Message cố định (lỗi máy chủ nội bộ).</summary>
    [Fact]
    public async Task RunForecast_WhenServiceThrowsException_Returns500()
    {
        var dto = new FloodForecastRequestDto { Latitude = 10.8, Longitude = 106.6, RadiusKm = 2 };
        A.CallTo(() => _floodForecastService.RunForecastForCitizenAsync(dto.Latitude, dto.Longitude, 2.0, A<int?>._, A<CancellationToken>._))
            .ThrowsAsync(new Exception("Unexpected error"));
        var controller = new FloodForecastController(_floodForecastService);

        var result = await controller.RunForecast(dto, CancellationToken.None);

        var error = Assert.IsType<ObjectResult>(result);
        Assert.Equal(500, error.StatusCode);
        var body = Assert.IsType<BaseCommentResponse>(error.Value);
        Assert.Equal(500, body.Statuscodes);
        Assert.Equal("Đã xảy ra lỗi máy chủ nội bộ!!!", body.Message);
    }

}
