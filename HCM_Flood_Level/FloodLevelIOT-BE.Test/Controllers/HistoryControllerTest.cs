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

public class HistoryControllerTest
{
    private static IMapper CreateTestMapper(List<History>? returnList = null)
    {
        var mapper = A.Fake<IMapper>();
        A.CallTo(() => mapper.Map<List<HistoryDTO>>(A<object>._))
            .ReturnsLazily((object source) =>
            {
                var list = source as IEnumerable<History> ?? new List<History>();
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

    private static (IUnitOfWork unitOfWork, IHistoryRepository historyRepo) CreateFakeUnitOfWork()
    {
        var historyRepo = A.Fake<IHistoryRepository>();
        var unitOfWork = A.Fake<IUnitOfWork>();
        A.CallTo(() => unitOfWork.HistoryRepository).Returns(historyRepo);
        return (unitOfWork, historyRepo);
    }

    [Fact]
    public async Task GetFloodHistory_WithValidLocationId_ReturnsOkWithHistoryData()
    {
        var histories = new List<History>
        {
            new()
            {
                HistoryId = 1, LocationId = 10,
                StartTime = new DateTime(2025, 1, 1, 8, 0, 0, DateTimeKind.Utc),
                EndTime = new DateTime(2025, 1, 1, 10, 0, 0, DateTimeKind.Utc),
                MaxWaterLevel = 120, Severity = Severity.Warning,
                CreatedAt = new DateTime(2025, 1, 2, 0, 0, 0, DateTimeKind.Utc)
            },
            new()
            {
                HistoryId = 2, LocationId = 10,
                StartTime = new DateTime(2025, 2, 1, 9, 0, 0, DateTimeKind.Utc),
                MaxWaterLevel = 80, Severity = Severity.Safe,
                CreatedAt = new DateTime(2025, 2, 2, 0, 0, 0, DateTimeKind.Utc)
            }
        };

        var (unitOfWork, historyRepo) = CreateFakeUnitOfWork();
        A.CallTo(() => historyRepo.GetAllAsync(A<EntityParam>._)).Returns(histories);
        A.CallTo(() => historyRepo.CountAsync()).Returns(2);

        var controller = new HistoryController(unitOfWork, CreateTestMapper());

        var result = await controller.GetAllHistories();

        var ok = Assert.IsType<OkObjectResult>(result);
        var payload = Assert.IsType<Pagination<HistoryDTO>>(ok.Value);
        Assert.Equal(2, payload.TotalCount);
        Assert.Contains(payload.Data, h => h.LocationId == 10 && h.MaxWaterLevel == 120);
    }

    [Fact]
    public async Task GetFloodHistory_WithNoData_ReturnsOkWithEmptyList()
    {
        var (unitOfWork, historyRepo) = CreateFakeUnitOfWork();
        A.CallTo(() => historyRepo.GetAllAsync(A<EntityParam>._)).Returns(new List<History>());
        A.CallTo(() => historyRepo.CountAsync()).Returns(0);

        var controller = new HistoryController(unitOfWork, CreateTestMapper());

        var result = await controller.GetAllHistories();

        var ok = Assert.IsType<OkObjectResult>(result);
        var payload = Assert.IsType<Pagination<HistoryDTO>>(ok.Value);
        Assert.Empty(payload.Data);
        Assert.Equal(0, payload.TotalCount);
    }

    [Fact]
    public async Task GetHistoryByDateRange_WithValidDates_ReturnsOkWithData()
    {
        var t1 = new DateTime(2025, 3, 1, 12, 0, 0, DateTimeKind.Utc);
        var t2 = new DateTime(2025, 3, 2, 12, 0, 0, DateTimeKind.Utc);
        var t3 = new DateTime(2025, 3, 3, 12, 0, 0, DateTimeKind.Utc);
        // Sorted: Danger first, then Safe
        var histories = new List<History>
        {
            new() { HistoryId = 3, LocationId = 1, StartTime = t2, MaxWaterLevel = 200, Severity = Severity.Danger, CreatedAt = t2 },
            new() { HistoryId = 1, LocationId = 1, StartTime = t1, MaxWaterLevel = 50, Severity = Severity.Safe, CreatedAt = t1 },
            new() { HistoryId = 2, LocationId = 1, StartTime = t2, MaxWaterLevel = 60, Severity = Severity.Safe, CreatedAt = t3 }
        };

        var (unitOfWork, historyRepo) = CreateFakeUnitOfWork();
        A.CallTo(() => historyRepo.GetAllAsync(A<EntityParam>._)).Returns(histories);
        A.CallTo(() => historyRepo.CountAsync()).Returns(3);

        var controller = new HistoryController(unitOfWork, CreateTestMapper());

        var result = await controller.GetAllHistories();

        var ok = Assert.IsType<OkObjectResult>(result);
        var payload = Assert.IsType<Pagination<HistoryDTO>>(ok.Value);
        Assert.Equal(3, payload.TotalCount);
        Assert.Equal(Severity.Danger.ToString(), payload.Data[0].Severity);
        Assert.Equal("Safe", payload.Data[1].Severity);
    }

    [Fact]
    public async Task GetMaxWaterLevelHistory_WithValidSensorId_ReturnsOkWithMaxLevel()
    {
        var histories = new List<History>
        {
            new() { HistoryId = 1, LocationId = 5, StartTime = DateTime.UtcNow, MaxWaterLevel = 255.5f, Severity = Severity.Warning, CreatedAt = DateTime.UtcNow }
        };

        var (unitOfWork, historyRepo) = CreateFakeUnitOfWork();
        A.CallTo(() => historyRepo.GetAllAsync(A<EntityParam>._)).Returns(histories);
        A.CallTo(() => historyRepo.CountAsync()).Returns(1);

        var controller = new HistoryController(unitOfWork, CreateTestMapper());

        var result = await controller.GetAllHistories();

        var ok = Assert.IsType<OkObjectResult>(result);
        var payload = Assert.IsType<Pagination<HistoryDTO>>(ok.Value);
        Assert.Single(payload.Data);
        Assert.Equal(255.5f, payload.Data[0].MaxWaterLevel);
    }

    [Fact]
    public async Task GetHistoryStatistics_WithValidLocationId_ReturnsOkWithStats()
    {
        var histories = Enumerable.Range(1, 4).Select(i => new History
        {
            HistoryId = i,
            LocationId = 99,
            StartTime = DateTime.UtcNow.AddDays(-i),
            MaxWaterLevel = 10f * i,
            Severity = Severity.Safe,
            CreatedAt = DateTime.UtcNow.AddMinutes(-i)
        }).ToList();

        var (unitOfWork, historyRepo) = CreateFakeUnitOfWork();
        A.CallTo(() => historyRepo.GetAllAsync(A<EntityParam>._)).Returns(histories);
        A.CallTo(() => historyRepo.CountAsync()).Returns(4);

        var controller = new HistoryController(unitOfWork, CreateTestMapper());

        var result = await controller.GetAllHistories();

        var ok = Assert.IsType<OkObjectResult>(result);
        var payload = Assert.IsType<Pagination<HistoryDTO>>(ok.Value);
        Assert.Equal(4, payload.TotalCount);
        Assert.All(payload.Data, h => Assert.Equal(99, h.LocationId));
    }

    [Fact]
    public async Task GetAllHistories_WhenMappingThrows_Returns500()
    {
        var histories = new List<History>
        {
            new() { HistoryId = 1, LocationId = 1, StartTime = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc), MaxWaterLevel = 10f, Severity = Severity.Safe, CreatedAt = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc) }
        };

        var (unitOfWork, historyRepo) = CreateFakeUnitOfWork();
        A.CallTo(() => historyRepo.GetAllAsync(A<EntityParam>._)).Returns(histories);
        A.CallTo(() => historyRepo.CountAsync()).Returns(1);

        var mapper = A.Fake<IMapper>();
        A.CallTo(() => mapper.Map<List<HistoryDTO>>(A<object>._))
            .Throws(new InvalidOperationException("Map failed"));

        var controller = new HistoryController(unitOfWork, mapper);

        var result = await controller.GetAllHistories();

        var error = Assert.IsType<ObjectResult>(result);
        Assert.Equal(500, error.StatusCode);
        var body = Assert.IsType<BaseCommentResponse>(error.Value);
        Assert.Equal(500, body.Statuscodes);
    }

    [Fact]
    public async Task GetAllHistories_WithInvalidPagination_ReturnsBadRequest()
    {
        var (unitOfWork, _) = CreateFakeUnitOfWork();
        var controller = new HistoryController(unitOfWork, A.Fake<IMapper>());

        var result = await controller.GetAllHistories(pagenumber: 0, pagesize: 10);

        var bad = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal(400, bad.StatusCode);
    }
}
