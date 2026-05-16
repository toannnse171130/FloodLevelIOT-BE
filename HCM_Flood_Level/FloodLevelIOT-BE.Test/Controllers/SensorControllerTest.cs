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

public class SensorControllerTest
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IMapper _mapper;
    private readonly ISensorRepository _sensorRepository;
    private readonly IScheduleRepository _scheduleRepository;
    private readonly IRequestRepository _requestRepository;

    public SensorControllerTest()
    {
        _unitOfWork = A.Fake<IUnitOfWork>();
        _mapper = A.Fake<IMapper>();
        _sensorRepository = A.Fake<ISensorRepository>();
        _scheduleRepository = A.Fake<IScheduleRepository>();
        _requestRepository = A.Fake<IRequestRepository>();

        A.CallTo(() => _unitOfWork.ManageSensorRepository).Returns(_sensorRepository);
        A.CallTo(() => _unitOfWork.ManageMaintenanceScheduleRepository).Returns(_scheduleRepository);
        A.CallTo(() => _unitOfWork.ManageRequestRepository).Returns(_requestRepository);
    }

    // Get All Devices Tests (3 tests)
    [Fact]
    public async Task GetAllDevices_WithValidPagination_ReturnsOkWithPaginatedSensors()
    {
        var sensors = new List<Sensor> { new() { SensorId = 1, SensorName = "S1" } };
        var readings = new List<SensorReading> { new() { SensorId = 1, WaterLevelCm = 12 } };
        var mapped = new List<ManageSensorDTO> { new() { SensorId = 1, SensorName = "S1" } };
        A.CallTo(() => _sensorRepository.GetAllSensorsAsync(A<EntityParam>._)).Returns(sensors);
        A.CallTo(() => _sensorRepository.CountAsync()).Returns(1);
        A.CallTo(() => _sensorRepository.GetLatestReadingsForSensorIdsAsync(A<IEnumerable<int>>._)).Returns(readings);
        A.CallTo(() => _mapper.Map<List<ManageSensorDTO>>(A<object>._, A<Action<IMappingOperationOptions<object, List<ManageSensorDTO>>>>._))
            .Returns(mapped);
        var controller = new SensorController(_unitOfWork, _mapper);

        var result = await controller.GetAllDevices(1, 10, null);

        var ok = Assert.IsType<OkObjectResult>(result);
        var page = Assert.IsType<Pagination<ManageSensorDTO>>(ok.Value);
        Assert.Equal(1, page.TotalCount);
    }

    [Fact]
    public async Task GetAllDevices_WithInvalidPageNumber_ReturnsBadRequest()
    {
        var controller = new SensorController(_unitOfWork, _mapper);

        var result = await controller.GetAllDevices(0, 10, null);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task GetAllDevices_WithInvalidPageSize_ReturnsBadRequest()
    {
        var controller = new SensorController(_unitOfWork, _mapper);

        var result = await controller.GetAllDevices(1, 0, null);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task GetAllDevices_WithNoResults_ReturnsOkWithEmptyPagination()
    {
        A.CallTo(() => _sensorRepository.GetAllSensorsAsync(A<EntityParam>._)).Returns(new List<Sensor>());
        A.CallTo(() => _sensorRepository.CountAsync()).Returns(0);
        A.CallTo(() => _sensorRepository.GetLatestReadingsForSensorIdsAsync(A<IEnumerable<int>>._)).Returns(new List<SensorReading>());
        A.CallTo(() => _mapper.Map<List<ManageSensorDTO>>(A<object>._, A<Action<IMappingOperationOptions<object, List<ManageSensorDTO>>>>._))
            .Returns(new List<ManageSensorDTO>());
        var controller = new SensorController(_unitOfWork, _mapper);

        var result = await controller.GetAllDevices(1, 10, "non-existent");

        var ok = Assert.IsType<OkObjectResult>(result);
        var page = Assert.IsType<Pagination<ManageSensorDTO>>(ok.Value);
        Assert.Equal(0, page.TotalCount);
        Assert.Empty(page.Data);
    }

    [Fact]
    public async Task GetAllDevices_WithInternalServerError_Returns500()
    {
        A.CallTo(() => _sensorRepository.GetAllSensorsAsync(A<EntityParam>._)).Throws(new Exception("DB Error"));
        var controller = new SensorController(_unitOfWork, _mapper);

        var result = await controller.GetAllDevices(1, 10, null);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(500, objectResult.StatusCode);
    }

    [Fact]
    public async Task GetAllDevices_WithSearchFilter_ReturnsOkWithFilteredSensors()
    {
        var sensors = new List<Sensor> { new() { SensorId = 2, SensorName = "Flood Sensor" } };
        A.CallTo(() => _sensorRepository.GetAllSensorsAsync(A<EntityParam>.That.Matches(p => p.Search == "flood")))
            .Returns(sensors);
        A.CallTo(() => _sensorRepository.CountAsync()).Returns(1);
        A.CallTo(() => _sensorRepository.GetLatestReadingsForSensorIdsAsync(A<IEnumerable<int>>._)).Returns(new List<SensorReading>());
        A.CallTo(() => _mapper.Map<List<ManageSensorDTO>>(A<object>._, A<Action<IMappingOperationOptions<object, List<ManageSensorDTO>>>>._))
            .Returns(new List<ManageSensorDTO> { new() { SensorId = 2, SensorName = "Flood Sensor" } });
        var controller = new SensorController(_unitOfWork, _mapper);

        var result = await controller.GetAllDevices(1, 10, "flood");

        Assert.IsType<OkObjectResult>(result);
    }

    // Get Device By ID Tests (3 tests)
    [Fact]
    public async Task GetDeviceById_WithValidId_ReturnsOkWithSensorDetails()
    {
        var sensor = new Sensor { SensorId = 1, SensorName = "S1", SensorCode = "D1" };
        var sensorDto = new SensorDTO { SensorId = 1, SensorName = "S1", SensorCode = "D1", Protocol = "MQTT", SensorType = "Water", InstalledByStaff = "Tech", Location = new LocationDTO() };
        A.CallTo(() => _sensorRepository.GetByIdAsync(1, A<System.Linq.Expressions.Expression<Func<Sensor, object>>[]>.Ignored))
            .Returns(sensor);
        A.CallTo(() => _sensorRepository.GetLatestReadingsForSensorIdsAsync(A<IEnumerable<int>>._)).Returns(new List<SensorReading>());
        A.CallTo(() => _scheduleRepository.GetBySensorIdAsync(1)).Returns(new List<MaintenanceSchedule>());
        A.CallTo(() => _requestRepository.GetBySensorIdAsync(1)).Returns(new List<MaintenanceRequest>());
        A.CallTo(() => _mapper.Map<SensorDTO>(A<object>._, A<Action<IMappingOperationOptions<object, SensorDTO>>>._)).Returns(sensorDto);
        A.CallTo(() => _mapper.Map<List<ScheduleDTO>>(A<object>._)).Returns(new List<ScheduleDTO>());
        A.CallTo(() => _mapper.Map<List<RequestDTO>>(A<object>._)).Returns(new List<RequestDTO>());
        var controller = new SensorController(_unitOfWork, _mapper);

        var result = await controller.GetDeviceById(1);

        var ok = Assert.IsType<OkObjectResult>(result);
        var payload = Assert.IsType<SensorDTO>(ok.Value);
        Assert.Equal(1, payload.SensorId);
    }

    [Fact]
    public async Task GetDeviceById_WithInvalidId_ReturnsBadRequest()
    {
        var controller = new SensorController(_unitOfWork, _mapper);

        var result = await controller.GetDeviceById(0);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task GetDeviceById_WithNonexistentId_ReturnsNotFound()
    {
        A.CallTo(() => _sensorRepository.GetByIdAsync(99, A<System.Linq.Expressions.Expression<Func<Sensor, object>>[]>.Ignored))
            .Returns((Sensor)null!);
        var controller = new SensorController(_unitOfWork, _mapper);

        var result = await controller.GetDeviceById(99);

        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task GetDeviceById_WithNegativeId_ReturnsBadRequest()
    {
        var controller = new SensorController(_unitOfWork, _mapper);

        var result = await controller.GetDeviceById(-1);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        var response = Assert.IsType<BaseCommentResponse>(badRequest.Value);
        Assert.Equal("ID thiết bị không hợp lệ", response.Message);
    }

    [Fact]
    public async Task GetDeviceById_WithMaxIntId_ReturnsNotFound()
    {
        A.CallTo(() => _sensorRepository.GetByIdAsync(int.MaxValue, A<System.Linq.Expressions.Expression<Func<Sensor, object>>[]>.Ignored))
            .Returns((Sensor)null!);
        var controller = new SensorController(_unitOfWork, _mapper);

        var result = await controller.GetDeviceById(int.MaxValue);

        var notFound = Assert.IsType<NotFoundObjectResult>(result);
        var response = Assert.IsType<BaseCommentResponse>(notFound.Value);
        Assert.Equal("Không tìm thấy thiết bị", response.Message);
    }

    [Fact]
    public async Task GetDeviceById_WithInternalServerError_Returns500()
    {
        A.CallTo(() => _sensorRepository.GetByIdAsync(1, A<System.Linq.Expressions.Expression<Func<Sensor, object>>[]>.Ignored))
            .Throws(new Exception("DB connection failed"));
        var controller = new SensorController(_unitOfWork, _mapper);

        var result = await controller.GetDeviceById(1);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(500, objectResult.StatusCode);
        var response = Assert.IsType<BaseCommentResponse>(objectResult.Value);
        Assert.Contains("Đã xảy ra lỗi máy chủ nội bộ", response.Message);
    }

    // Create Device Tests (2 tests)
    [Fact]
    public async Task CreateDevice_WithValidData_ReturnsOkAndCreatesSensor()
    {
        A.CallTo(() => _sensorRepository.AddNewSensorAsync(A<CreateSensorDTO>._)).Returns(1);
        var controller = new SensorController(_unitOfWork, _mapper);
        var dto = new CreateSensorDTO
        {
            SensorCode = "D001",
            SensorName = "S1",
            SensorType = "Water",
            Latitude = 10.0m,
            Longitude = 106.0m
        };

        var result = await controller.CreateDevice(dto);

        Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public async Task CreateDevice_WithMissingRequiredFields_ReturnsBadRequest()
    {
        var controller = new SensorController(_unitOfWork, _mapper);

        var result = await controller.CreateDevice(new CreateSensorDTO { SensorCode = "", SensorName = "S1", SensorType = "Water", Latitude = 10m, Longitude = 106m });

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task CreateDevice_WithInvalidCoordinates_ReturnsBadRequest()
    {
        var controller = new SensorController(_unitOfWork, _mapper);

        var result = await controller.CreateDevice(new CreateSensorDTO { SensorCode = "D1", SensorName = "S1", SensorType = "Water", Latitude = 0, Longitude = 0 });

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task CreateDevice_WithDuplicateSensorCode_ReturnsBadRequest()
    {
        A.CallTo(() => _sensorRepository.AddNewSensorAsync(A<CreateSensorDTO>._)).Returns(0);
        var controller = new SensorController(_unitOfWork, _mapper);
        var dto = new CreateSensorDTO { SensorCode = "DUP", SensorName = "S1", SensorType = "Water", Latitude = 10m, Longitude = 106m };

        var result = await controller.CreateDevice(dto);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task CreateDevice_WithMissingName_ReturnsBadRequest()
    {
        var controller = new SensorController(_unitOfWork, _mapper);

        var result = await controller.CreateDevice(new CreateSensorDTO { SensorCode = "D1", SensorName = "", SensorType = "Water", Latitude = 10m, Longitude = 106m });

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task CreateDevice_WithMissingType_ReturnsBadRequest()
    {
        var controller = new SensorController(_unitOfWork, _mapper);

        var result = await controller.CreateDevice(new CreateSensorDTO { SensorCode = "D1", SensorName = "S1", SensorType = "", Latitude = 10m, Longitude = 106m });

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task CreateDevice_WithInternalServerError_Returns500()
    {
        A.CallTo(() => _sensorRepository.AddNewSensorAsync(A<CreateSensorDTO>._)).Throws(new Exception("DB Error"));
        var controller = new SensorController(_unitOfWork, _mapper);
        var dto = new CreateSensorDTO { SensorCode = "ERR", SensorName = "S1", SensorType = "Water", Latitude = 10m, Longitude = 106m };

        var result = await controller.CreateDevice(dto);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(500, objectResult.StatusCode);
    }

    // Update Device Tests (2 tests)
    [Fact]
    public async Task UpdateDevice_WithValidData_ReturnsOkAndUpdatesSensor()
    {
        A.CallTo(() => _sensorRepository.GetByIdAsync(1)).Returns(new Sensor { SensorId = 1 });
        A.CallTo(() => _sensorRepository.UpdateSensorAsync(1, A<UpdateSensorDTO>._)).Returns(true);
        var controller = new SensorController(_unitOfWork, _mapper);

        var result = await controller.UpdateDevice(1, new UpdateSensorDTO { SensorName = "Updated" });

        Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public async Task UpdateDevice_WithInvalidId_ReturnsBadRequest()
    {
        var controller = new SensorController(_unitOfWork, _mapper);

        var result = await controller.UpdateDevice(0, new UpdateSensorDTO { SensorName = "Updated" });

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task UpdateDevice_WithNonexistentId_ReturnsNotFound()
    {
        A.CallTo(() => _sensorRepository.GetByIdAsync(999)).Returns((Sensor)null!);
        var controller = new SensorController(_unitOfWork, _mapper);

        var result = await controller.UpdateDevice(999, new UpdateSensorDTO { SensorName = "Updated" });

        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task UpdateDevice_WithNoFieldsToUpdate_ReturnsBadRequest()
    {
        var controller = new SensorController(_unitOfWork, _mapper);

        var result = await controller.UpdateDevice(1, new UpdateSensorDTO());

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task UpdateDevice_WithDuplicateSensorCode_ReturnsBadRequest()
    {
        A.CallTo(() => _sensorRepository.GetByIdAsync(1)).Returns(new Sensor { SensorId = 1 });
        A.CallTo(() => _sensorRepository.UpdateSensorAsync(1, A<UpdateSensorDTO>._)).Returns(false);
        var controller = new SensorController(_unitOfWork, _mapper);

        var result = await controller.UpdateDevice(1, new UpdateSensorDTO { SensorCode = "DUP" });

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        var response = Assert.IsType<BaseCommentResponse>(badRequest.Value);
        Assert.Contains("Cập nhật thiết bị không thành công", response.Message);
    }

    [Fact]
    public async Task UpdateDevice_WithInternalServerError_Returns500()
    {
        A.CallTo(() => _sensorRepository.GetByIdAsync(1)).Throws(new Exception("Fatal DB Error"));
        var controller = new SensorController(_unitOfWork, _mapper);

        var result = await controller.UpdateDevice(1, new UpdateSensorDTO { SensorName = "Err" });

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(500, objectResult.StatusCode);
    }

    // Delete Device Tests (2 tests)
    [Fact]
    public async Task DeleteDevice_WithValidId_ReturnsOkAndDeletesSensor()
    {
        A.CallTo(() => _sensorRepository.DeleteSensorAsync(1)).Returns(true);
        var controller = new SensorController(_unitOfWork, _mapper);

        var result = await controller.DeleteDevice(1);

        Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public async Task DeleteDevice_WithInvalidId_ReturnsBadRequest()
    {
        var controller = new SensorController(_unitOfWork, _mapper);

        var result = await controller.DeleteDevice(0);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task DeleteDevice_WithNonexistentId_ReturnsNotFound()
    {
        A.CallTo(() => _sensorRepository.DeleteSensorAsync(999)).Returns((bool?)null);
        var controller = new SensorController(_unitOfWork, _mapper);

        var result = await controller.DeleteDevice(999);

        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task DeleteDevice_WithRelatedMaintenanceRecords_ReturnsConflict()
    {
        A.CallTo(() => _sensorRepository.DeleteSensorAsync(2)).Returns(false);
        var controller = new SensorController(_unitOfWork, _mapper);

        var result = await controller.DeleteDevice(2);

        var conflict = Assert.IsType<ConflictObjectResult>(result);
        var response = Assert.IsType<BaseCommentResponse>(conflict.Value);
        Assert.Equal(409, response.Statuscodes);
    }
}
