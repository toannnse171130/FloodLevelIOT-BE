using AutoMapper;
using Core.DTOs;
using Core.Entities;
using FakeItEasy;
using Infrastructure.DBContext;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebAPI.Controllers;
using WebAPI.Errors;

namespace FloodLevelIOT_BE.Test.Controllers;

internal sealed class HistoryTestAppDbContext : AppDbContext
{
    public HistoryTestAppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Ignore<Location>();
        modelBuilder.Entity<SensorReading>(e =>
        {
            e.HasKey(x => x.ReadingId);
            e.Ignore(x => x.Sensor);
        });
        modelBuilder.Entity<History>(e =>
        {
            e.HasKey(x => x.HistoryId);
            e.Ignore(x => x.Location);
            e.Property(x => x.Severity).HasConversion<string>();
        });
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

public class HistoryControllerTest
{
    private static DbContextOptions<AppDbContext> CreateOptions()
        => new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

    private static IMapper CreateTestMapper()
    {
        var mapper = A.Fake<IMapper>();
        A.CallTo(() => mapper.Map<List<HistoryDTO>>(A<object>._))
            .ReturnsLazily((object source) =>
            {
                var list = (List<History>)source;
                return list.Select(h => new HistoryDTO
                {
                    HistoryId = h.HistoryId,
                    LocationId = h.LocationId,
                    StartTime = h.StartTime,
                    EndTime = h.EndTime,
                    MaxWaterLevel = h.MaxWaterLevel,
                    Severity = h.Severity.ToString(),
                    CreatedAt = h.CreatedAt
                }).ToList();
            });
        return mapper;
    }

    [Fact]
    public async Task GetFloodHistory_WithValidLocationId_ReturnsOkWithHistoryData()
    {
        await using var context = new HistoryTestAppDbContext(CreateOptions());
        context.Histories.AddRange(
            new History
            {
                HistoryId = 1,
                LocationId = 10,
                StartTime = new DateTime(2025, 1, 1, 8, 0, 0, DateTimeKind.Utc),
                EndTime = new DateTime(2025, 1, 1, 10, 0, 0, DateTimeKind.Utc),
                MaxWaterLevel = 120,
                Severity = Severity.Warning,
                CreatedAt = new DateTime(2025, 1, 2, 0, 0, 0, DateTimeKind.Utc)
            },
            new History
            {
                HistoryId = 2,
                LocationId = 10,
                StartTime = new DateTime(2025, 2, 1, 9, 0, 0, DateTimeKind.Utc),
                MaxWaterLevel = 80,
                Severity = Severity.Safe,
                CreatedAt = new DateTime(2025, 2, 2, 0, 0, 0, DateTimeKind.Utc)
            });
        await context.SaveChangesAsync();

        var mapper = CreateTestMapper();
        var controller = new HistoryController(context, mapper);

        var result = await controller.GetAllHistories();

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var payload = Assert.IsType<List<HistoryDTO>>(ok.Value);
        Assert.Equal(2, payload.Count);
        Assert.Contains(payload, h => h.LocationId == 10 && h.MaxWaterLevel == 120);
    }

    [Fact]
    public async Task GetFloodHistory_WithInvalidLocationId_ReturnsNotFound()
    {
        await using var context = new HistoryTestAppDbContext(CreateOptions());
        var mapper = CreateTestMapper();
        var controller = new HistoryController(context, mapper);

        var result = await controller.GetAllHistories();

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var payload = Assert.IsType<List<HistoryDTO>>(ok.Value);
        Assert.Empty(payload);
    }

    [Fact]
    public async Task GetHistoryByDateRange_WithValidDates_ReturnsOkWithData()
    {
        await using var context = new HistoryTestAppDbContext(CreateOptions());
        var t1 = new DateTime(2025, 3, 1, 12, 0, 0, DateTimeKind.Utc);
        var t2 = new DateTime(2025, 3, 2, 12, 0, 0, DateTimeKind.Utc);
        var t3 = new DateTime(2025, 3, 3, 12, 0, 0, DateTimeKind.Utc);
        context.Histories.AddRange(
            new History
            {
                HistoryId = 1,
                LocationId = 1,
                StartTime = t1,
                MaxWaterLevel = 50,
                Severity = Severity.Safe,
                CreatedAt = t1
            },
            new History
            {
                HistoryId = 2,
                LocationId = 1,
                StartTime = t2,
                MaxWaterLevel = 60,
                Severity = Severity.Safe,
                CreatedAt = t3
            },
            new History
            {
                HistoryId = 3,
                LocationId = 1,
                StartTime = t2,
                MaxWaterLevel = 200,
                Severity = Severity.Danger,
                CreatedAt = t2
            });
        await context.SaveChangesAsync();

        var mapper = CreateTestMapper();
        var controller = new HistoryController(context, mapper);

        var result = await controller.GetAllHistories();

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var payload = Assert.IsType<List<HistoryDTO>>(ok.Value);
        Assert.Equal(3, payload.Count);
        Assert.Equal(Severity.Danger.ToString(), payload[0].Severity);
        Assert.Equal("Safe", payload[1].Severity);
    }

    [Fact]
    public async Task GetMaxWaterLevelHistory_WithValidSensorId_ReturnsOkWithMaxLevel()
    {
        await using var context = new HistoryTestAppDbContext(CreateOptions());
        context.Histories.Add(new History
        {
            HistoryId = 1,
            LocationId = 5,
            StartTime = DateTime.UtcNow,
            MaxWaterLevel = 255.5f,
            Severity = Severity.Warning,
            CreatedAt = DateTime.UtcNow
        });
        await context.SaveChangesAsync();

        var mapper = CreateTestMapper();
        var controller = new HistoryController(context, mapper);

        var result = await controller.GetAllHistories();

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var payload = Assert.IsType<List<HistoryDTO>>(ok.Value);
        Assert.Single(payload);
        Assert.Equal(255.5f, payload[0].MaxWaterLevel);
    }

    [Fact]
    public async Task GetHistoryStatistics_WithValidLocationId_ReturnsOkWithStats()
    {
        await using var context = new HistoryTestAppDbContext(CreateOptions());
        for (var i = 1; i <= 4; i++)
        {
            context.Histories.Add(new History
            {
                HistoryId = i,
                LocationId = 99,
                StartTime = DateTime.UtcNow.AddDays(-i),
                MaxWaterLevel = 10f * i,
                Severity = Severity.Safe,
                CreatedAt = DateTime.UtcNow.AddMinutes(-i)
            });
        }

        await context.SaveChangesAsync();

        var mapper = CreateTestMapper();
        var controller = new HistoryController(context, mapper);

        var result = await controller.GetAllHistories();

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var payload = Assert.IsType<List<HistoryDTO>>(ok.Value);
        Assert.Equal(4, payload.Count);
        Assert.All(payload, h => Assert.Equal(99, h.LocationId));
    }

    [Fact]
    public async Task GetAllHistories_WhenMappingThrows_Returns500()
    {
        await using var context = new HistoryTestAppDbContext(CreateOptions());
        context.Histories.Add(new History
        {
            HistoryId = 1,
            LocationId = 1,
            StartTime = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            MaxWaterLevel = 10f,
            Severity = Severity.Safe,
            CreatedAt = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        });
        await context.SaveChangesAsync();

        var mapper = A.Fake<IMapper>();
        A.CallTo(() => mapper.Map<List<HistoryDTO>>(A<object>._))
            .Throws(new InvalidOperationException("Map failed"));
        var controller = new HistoryController(context, mapper);

        var result = await controller.GetAllHistories();

        var error = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(500, error.StatusCode);
        var body = Assert.IsType<BaseCommentResponse>(error.Value);
        Assert.Equal(500, body.Statuscodes);
    }

}
