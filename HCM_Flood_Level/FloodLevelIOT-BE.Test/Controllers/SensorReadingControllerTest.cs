using AutoMapper;
using Core.DTOs;
using Core.Entities;
using Core.Interfaces;
using Core.Sharing;
using FakeItEasy;
using Microsoft.AspNetCore.Mvc;
using WebAPI.Controllers;
using WebAPI.Errors;
using WebAPI.Helpers;

namespace FloodLevelIOT_BE.Test.Controllers;

public class SensorReadingControllerTest
{
    private static IMapper CreateTestMapper()
    {
        var mapper = A.Fake<IMapper>();
        A.CallTo(() => mapper.Map<List<SensorReadingDTO>>(A<object>._))
            .ReturnsLazily((object source) =>
            {
                var list = source as IEnumerable<SensorReading> ?? new List<SensorReading>();
                return list.Select(r => new SensorReadingDTO
                {
                    ReadingId = r.ReadingId,
                    SensorId = r.SensorId,
                    Status = r.Status,
                    WaterLevelCm = r.WaterLevelCm,
                    BatteryPercent = r.BatteryPercent,
                    SignalStrength = r.SignalStrength,
                    RecordedAt = r.RecordedAt
                }).ToList();
            });
        return mapper;
    }

    private static (IUnitOfWork unitOfWork, ISensorReadingRepository readingRepo) CreateFakeUnitOfWork()
    {
        var readingRepo = A.Fake<ISensorReadingRepository>();
        var unitOfWork = A.Fake<IUnitOfWork>();
        A.CallTo(() => unitOfWork.SensorReadingRepository).Returns(readingRepo);
        return (unitOfWork, readingRepo);
    }

    [Fact]
    public async Task GetLatestReadings_WithValidSensorIds_ReturnsOkWithReadings()
    {
        var readings = new List<SensorReading>
        {
            new() { ReadingId = 1, SensorId = 10, Status = "Online", WaterLevelCm = 5, BatteryPercent = 90, SignalStrength = "-70", RecordedAt = new DateTime(2025, 6, 1, 12, 0, 0, DateTimeKind.Utc) },
            new() { ReadingId = 2, SensorId = 20, Status = "Online", WaterLevelCm = 8, BatteryPercent = 85, SignalStrength = "-65", RecordedAt = new DateTime(2025, 6, 2, 12, 0, 0, DateTimeKind.Utc) }
        };

        var (unitOfWork, readingRepo) = CreateFakeUnitOfWork();
        A.CallTo(() => readingRepo.GetAllAsync(A<EntityParam>._)).Returns(readings);
        A.CallTo(() => readingRepo.CountAsync(A<int?>._)).Returns(2);

        var controller = new SensorReadingController(unitOfWork, CreateTestMapper());

        var result = await controller.GetAllSensorReadings();

        var ok = Assert.IsType<OkObjectResult>(result);
        var payload = Assert.IsType<Pagination<SensorReadingDTO>>(ok.Value);
        Assert.Equal(2, payload.TotalCount);
    }

    [Fact]
    public async Task GetLatestReadings_WithNoData_ReturnsOkWithEmptyList()
    {
        var (unitOfWork, readingRepo) = CreateFakeUnitOfWork();
        A.CallTo(() => readingRepo.GetAllAsync(A<EntityParam>._)).Returns(new List<SensorReading>());
        A.CallTo(() => readingRepo.CountAsync(A<int?>._)).Returns(0);

        var controller = new SensorReadingController(unitOfWork, CreateTestMapper());

        var result = await controller.GetAllSensorReadings();

        var ok = Assert.IsType<OkObjectResult>(result);
        var payload = Assert.IsType<Pagination<SensorReadingDTO>>(ok.Value);
        Assert.Empty(payload.Data);
        Assert.Equal(0, payload.TotalCount);
    }

    [Fact]
    public void GetMqttLog_ReturnsOkWithCountAndMessages()
    {
        var (unitOfWork, _) = CreateFakeUnitOfWork();
        var controller = new SensorReadingController(unitOfWork, A.Fake<IMapper>());

        var result = controller.GetMqttLog();

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.NotNull(ok.Value);
        var countProp = ok.Value.GetType().GetProperty("count");
        var messagesProp = ok.Value.GetType().GetProperty("messages");
        Assert.NotNull(countProp);
        Assert.NotNull(messagesProp);
        Assert.IsType<int>(countProp.GetValue(ok.Value));
        Assert.NotNull(messagesProp.GetValue(ok.Value));
    }

    [Fact]
    public async Task GetHistoryReadings_WithValidSensorId_ReturnsOkWithHistoryData()
    {
        var readings = new List<SensorReading>
        {
            new() { ReadingId = 1, SensorId = 42, Status = "Online", WaterLevelCm = 12, BatteryPercent = 80, SignalStrength = "-60", RecordedAt = new DateTime(2025, 3, 1, 8, 0, 0, DateTimeKind.Utc) }
        };

        var (unitOfWork, readingRepo) = CreateFakeUnitOfWork();
        A.CallTo(() => readingRepo.GetAllAsync(A<EntityParam>._)).Returns(readings);
        A.CallTo(() => readingRepo.CountAsync(A<int?>._)).Returns(1);

        var controller = new SensorReadingController(unitOfWork, CreateTestMapper());

        var result = await controller.GetAllSensorReadings(sensorId: 42);

        var ok = Assert.IsType<OkObjectResult>(result);
        var payload = Assert.IsType<Pagination<SensorReadingDTO>>(ok.Value);
        Assert.Single(payload.Data);
        Assert.Equal(42, payload.Data[0].SensorId);
    }

    [Fact]
    public async Task PruneSensorReadings_OrderedByRecordedAtDesc_ReturnsCorrectOrder()
    {
        var older = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var newer = new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        // Repository returns already sorted (desc)
        var readings = new List<SensorReading>
        {
            new() { ReadingId = 2, SensorId = 1, Status = "Online", WaterLevelCm = 2, BatteryPercent = 99, SignalStrength = "-51", RecordedAt = newer },
            new() { ReadingId = 1, SensorId = 1, Status = "Online", WaterLevelCm = 1, BatteryPercent = 100, SignalStrength = "-50", RecordedAt = older }
        };

        var (unitOfWork, readingRepo) = CreateFakeUnitOfWork();
        A.CallTo(() => readingRepo.GetAllAsync(A<EntityParam>._)).Returns(readings);
        A.CallTo(() => readingRepo.CountAsync(A<int?>._)).Returns(2);

        var controller = new SensorReadingController(unitOfWork, CreateTestMapper());

        var result = await controller.GetAllSensorReadings();

        var ok = Assert.IsType<OkObjectResult>(result);
        var payload = Assert.IsType<Pagination<SensorReadingDTO>>(ok.Value);
        Assert.Equal(2, payload.TotalCount);
        Assert.True(payload.Data[0].RecordedAt > payload.Data[1].RecordedAt);
    }

    [Fact]
    public async Task GetAllSensorReadings_WhenMappingThrows_Returns500()
    {
        var readings = new List<SensorReading>
        {
            new() { ReadingId = 1, SensorId = 1, Status = "Online", WaterLevelCm = 1, BatteryPercent = 100, SignalStrength = "-50", RecordedAt = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc) }
        };

        var (unitOfWork, readingRepo) = CreateFakeUnitOfWork();
        A.CallTo(() => readingRepo.GetAllAsync(A<EntityParam>._)).Returns(readings);
        A.CallTo(() => readingRepo.CountAsync(A<int?>._)).Returns(1);

        var mapper = A.Fake<IMapper>();
        A.CallTo(() => mapper.Map<List<SensorReadingDTO>>(A<object>._))
            .Throws(new InvalidOperationException("Map failed"));

        var controller = new SensorReadingController(unitOfWork, mapper);

        var result = await controller.GetAllSensorReadings();

        var error = Assert.IsType<ObjectResult>(result);
        Assert.Equal(500, error.StatusCode);
        var body = Assert.IsType<BaseCommentResponse>(error.Value);
        Assert.Equal(500, body.Statuscodes);
    }

    [Fact]
    public async Task GetAllSensorReadings_WhenRepositoryThrows_Returns500()
    {
        var (unitOfWork, readingRepo) = CreateFakeUnitOfWork();
        A.CallTo(() => readingRepo.GetAllAsync(A<EntityParam>._))
            .Throws(new InvalidOperationException("DB failed"));

        var controller = new SensorReadingController(unitOfWork, CreateTestMapper());

        var result = await controller.GetAllSensorReadings();

        var error = Assert.IsType<ObjectResult>(result);
        Assert.Equal(500, error.StatusCode);
        var body = Assert.IsType<BaseCommentResponse>(error.Value);
        Assert.Equal(500, body.Statuscodes);
    }
}
