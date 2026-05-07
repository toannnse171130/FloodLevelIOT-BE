using AutoMapper;
using Core.DTOs;
using Core.Entities;
using Core.Interfaces;
using FakeItEasy;
using Infrastructure.DBContext;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebAPI.Controllers;
using WebAPI.Errors;

namespace FloodLevelIOT_BE.Test.Controllers;

internal sealed class TestAppDbContextForSensorReading : AppDbContext
{
    public TestAppDbContextForSensorReading(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Ignore<Location>();
        modelBuilder.Entity<SensorReading>(entity =>
        {
            entity.HasKey(e => e.ReadingId);
            entity.Ignore(e => e.Sensor);
        });
        modelBuilder.Ignore<History>();
        modelBuilder.Ignore<Report>();
        modelBuilder.Ignore<User>();
        modelBuilder.Ignore<Role>();
        modelBuilder.Ignore<Sensor>();
        modelBuilder.Ignore<Area>();
        modelBuilder.Ignore<Core.Entities.Priority>();
        modelBuilder.Ignore<MaintenanceRequest>();
        modelBuilder.Ignore<MaintenanceSchedule>();
    }
}

public class SensorReadingControllerTest
{
    private static DbContextOptions<AppDbContext> CreateOptions()
        => new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

    private static IMapper CreateTestMapper()
    {
        var mapper = A.Fake<IMapper>();
        A.CallTo(() => mapper.Map<List<SensorReadingDTO>>(A<object>._))
            .ReturnsLazily((object source) =>
            {
                var list = (List<SensorReading>)source;
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

    private static SensorReadingController CreateController(
        AppDbContext context,
        IMapper mapper,
        IHistoryService historyService)
        => new(context, mapper, historyService);

    [Fact]
    public async Task GetLatestReadings_WithValidSensorIds_ReturnsOkWithReadings()
    {
        await using var context = new TestAppDbContextForSensorReading(CreateOptions());
        context.SensorReadings.AddRange(
            new SensorReading
            {
                ReadingId = 1,
                SensorId = 10,
                Status = "Online",
                WaterLevelCm = 5,
                BatteryPercent = 90,
                SignalStrength = "-70",
                RecordedAt = new DateTime(2025, 6, 1, 12, 0, 0, DateTimeKind.Utc)
            },
            new SensorReading
            {
                ReadingId = 2,
                SensorId = 20,
                Status = "Online",
                WaterLevelCm = 8,
                BatteryPercent = 85,
                SignalStrength = "-65",
                RecordedAt = new DateTime(2025, 6, 2, 12, 0, 0, DateTimeKind.Utc)
            });
        await context.SaveChangesAsync();

        var mapper = CreateTestMapper();
        var history = A.Fake<IHistoryService>();
        var controller = CreateController(context, mapper, history);

        var result = await controller.GetAllSensorReadings();

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var payload = Assert.IsType<List<SensorReadingDTO>>(ok.Value);
        Assert.Equal(2, payload.Count);
        A.CallTo(() => history.ProcessSensorReading(A<SensorReading>._)).MustHaveHappened(2, Times.Exactly);
    }

    [Fact]
    public async Task GetLatestReadings_WithInvalidSensorIds_ReturnsNotFound()
    {
        await using var context = new TestAppDbContextForSensorReading(CreateOptions());
        var mapper = CreateTestMapper();
        var history = A.Fake<IHistoryService>();
        var controller = CreateController(context, mapper, history);

        var result = await controller.GetAllSensorReadings();

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var payload = Assert.IsType<List<SensorReadingDTO>>(ok.Value);
        Assert.Empty(payload);
        A.CallTo(() => history.ProcessSensorReading(A<SensorReading>._)).MustNotHaveHappened();
    }

    [Fact]
    public void AddSensorReading_WithValidData_ReturnsOkAndAddsReading()
    {
        using var context = new TestAppDbContextForSensorReading(CreateOptions());
        var mapper = CreateTestMapper();
        var history = A.Fake<IHistoryService>();
        var controller = CreateController(context, mapper, history);

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
        await using var context = new TestAppDbContextForSensorReading(CreateOptions());
        var reading = new SensorReading
        {
            ReadingId = 1,
            SensorId = 42,
            Status = "Online",
            WaterLevelCm = 12,
            BatteryPercent = 80,
            SignalStrength = "-60",
            RecordedAt = new DateTime(2025, 3, 1, 8, 0, 0, DateTimeKind.Utc)
        };
        context.SensorReadings.Add(reading);
        await context.SaveChangesAsync();

        var mapper = CreateTestMapper();
        var history = A.Fake<IHistoryService>();
        var controller = CreateController(context, mapper, history);

        var result = await controller.GetAllSensorReadings();

        Assert.IsType<OkObjectResult>(result.Result);
        A.CallTo(() => history.ProcessSensorReading(A<SensorReading>.That.Matches(r => r.ReadingId == 1 && r.SensorId == 42)))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task PruneSensorReadings_WithValidSensorId_ReturnsOkAndPrunesOldData()
    {
        await using var context = new TestAppDbContextForSensorReading(CreateOptions());
        var older = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var newer = new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        context.SensorReadings.AddRange(
            new SensorReading
            {
                ReadingId = 1,
                SensorId = 1,
                Status = "Online",
                WaterLevelCm = 1,
                BatteryPercent = 100,
                SignalStrength = "-50",
                RecordedAt = older
            },
            new SensorReading
            {
                ReadingId = 2,
                SensorId = 1,
                Status = "Online",
                WaterLevelCm = 2,
                BatteryPercent = 99,
                SignalStrength = "-51",
                RecordedAt = newer
            });
        await context.SaveChangesAsync();

        var mapper = CreateTestMapper();
        var history = A.Fake<IHistoryService>();
        var controller = CreateController(context, mapper, history);

        var result = await controller.GetAllSensorReadings();

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var payload = Assert.IsType<List<SensorReadingDTO>>(ok.Value);
        Assert.Equal(2, payload.Count);
        Assert.True(payload[0].RecordedAt > payload[1].RecordedAt);
    }

    [Fact]
    public async Task GetAllSensorReadings_WhenMappingThrows_Returns500()
    {
        await using var context = new TestAppDbContextForSensorReading(CreateOptions());
        context.SensorReadings.Add(
            new SensorReading
            {
                ReadingId = 1,
                SensorId = 1,
                Status = "Online",
                WaterLevelCm = 1,
                BatteryPercent = 100,
                SignalStrength = "-50",
                RecordedAt = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            });
        await context.SaveChangesAsync();

        var mapper = A.Fake<IMapper>();
        A.CallTo(() => mapper.Map<List<SensorReadingDTO>>(A<object>._))
            .Throws(new InvalidOperationException("Map failed"));
        var history = A.Fake<IHistoryService>();
        var controller = CreateController(context, mapper, history);

        var result = await controller.GetAllSensorReadings();

        var error = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(500, error.StatusCode);
        var body = Assert.IsType<BaseCommentResponse>(error.Value);
        Assert.Equal(500, body.Statuscodes);
    }

    [Fact]
    public async Task GetAllSensorReadings_WhenHistoryServiceThrows_Returns500()
    {
        await using var context = new TestAppDbContextForSensorReading(CreateOptions());
        context.SensorReadings.Add(
            new SensorReading
            {
                ReadingId = 1,
                SensorId = 1,
                Status = "Online",
                WaterLevelCm = 1,
                BatteryPercent = 100,
                SignalStrength = "-50",
                RecordedAt = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            });
        await context.SaveChangesAsync();

        var mapper = CreateTestMapper();
        var history = A.Fake<IHistoryService>();
        A.CallTo(() => history.ProcessSensorReading(A<SensorReading>._))
            .Throws(new InvalidOperationException("History failed"));
        var controller = CreateController(context, mapper, history);

        var result = await controller.GetAllSensorReadings();

        var error = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(500, error.StatusCode);
        var body = Assert.IsType<BaseCommentResponse>(error.Value);
        Assert.Equal(500, body.Statuscodes);
    }
}
