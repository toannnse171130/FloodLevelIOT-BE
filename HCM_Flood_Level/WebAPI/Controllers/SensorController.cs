using AutoMapper;
using Core.DTOs;
using Core.Entities;
using Core.Interfaces;
using Core.Sharing;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WebAPI.Errors;
using WebAPI.Helpers;

namespace WebAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class SensorController : ControllerBase
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly IMapper _mapper;

        //24/03
        private readonly ISensorRepository _repo;

        public SensorController(IUnitOfWork unitOfWork, IMapper mapper)
        {
            _unitOfWork = unitOfWork;
            _mapper = mapper;
        }

        [HttpGet("devices")]
        public async Task<ActionResult> GetAllDevices(
            [FromQuery] int pagenumber = 1,
            [FromQuery] int pagesize = 10,
            [FromQuery] string? search = null)
        {
            try
            {
                if (pagenumber <= 0 || pagesize <= 0)
                    return BadRequest(new BaseCommentResponse(400, "Số trang và kích thước trang phải lớn hơn 0"));

                var sensors = await _unitOfWork.ManageSensorRepository.GetAllSensorsAsync(new EntityParam
                {
                    Pagenumber = pagenumber,
                    Pagesize = pagesize,
                    Search = search
                });

                var total = await _unitOfWork.ManageSensorRepository.CountAsync();

                var sensorIds = sensors.Select(s => s.SensorId).ToList();
                var latestReadings = await _unitOfWork.ManageSensorRepository.GetLatestReadingsForSensorIdsAsync(sensorIds);
                var readingsBySensor = latestReadings.Where(r => r != null).ToDictionary(r => r.SensorId, r => r);

                var result = _mapper.Map<List<ManageSensorDTO>>(sensors, opts =>
                {
                    opts.Items["LatestReadings"] = readingsBySensor;
                });

                return Ok(new Pagination<ManageSensorDTO>(pagesize, pagenumber, total, result));
            }
            catch (Exception ex)
            {
                return StatusCode(500, new BaseCommentResponse(500, $"Đã xảy ra lỗi máy chủ nội bộ: {ex.Message}"));
            }
        }

        [HttpGet("devices/{id}")]
        public async Task<ActionResult> GetDeviceById(int id)
        {
            try
            {
                if (id <= 0)
                    return BadRequest(new BaseCommentResponse(400, "ID thiết bị không hợp lệ"));

                var sensor = await _unitOfWork.ManageSensorRepository.GetByIdAsync(id,
                    s => s.Location,
                    s => s.Technician);

                if (sensor == null)
                    return NotFound(new BaseCommentResponse(404, "Không tìm thấy thiết bị"));

                var latestReadings = await _unitOfWork.ManageSensorRepository.GetLatestReadingsForSensorIdsAsync(new List<int> { sensor.SensorId });
                var readingsBySensor = latestReadings.Where(r => r != null).ToDictionary(r => r.SensorId, r => r);

                var schedules = await _unitOfWork.ManageMaintenanceScheduleRepository.GetBySensorIdAsync(id);
                var requests = await _unitOfWork.ManageRequestRepository.GetBySensorIdAsync(id);

                var result = _mapper.Map<SensorDTO>(sensor, opts =>
                {
                    opts.Items["LatestReadings"] = readingsBySensor;
                });

                result.Schedule = _mapper.Map<List<ScheduleDTO>>(schedules);
                result.Request = _mapper.Map<List<RequestDTO>>(requests);

                return Ok(result);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new BaseCommentResponse(500, $"Đã xảy ra lỗi máy chủ nội bộ: {ex.Message}"));
            }
        }

        [HttpPost("devices")]
        public async Task<ActionResult> CreateDevice([FromBody] CreateSensorDTO dto)
        {
            try
            {
                if (!ModelState.IsValid)
                    return BadRequest(new BaseCommentResponse(400, "Dữ liệu đầu vào không hợp lệ"));

                if (dto == null)
                    return BadRequest(new BaseCommentResponse(400, "Dữ liệu thiết bị là bắt buộc"));

                if (string.IsNullOrWhiteSpace(dto.SensorCode))
                    return BadRequest(new BaseCommentResponse(400, "Mã thiết bị là bắt buộc"));

                if (string.IsNullOrWhiteSpace(dto.SensorName))
                    return BadRequest(new BaseCommentResponse(400, "Tên thiết bị là bắt buộc"));

                if (string.IsNullOrWhiteSpace(dto.SensorType))
                    return BadRequest(new BaseCommentResponse(400, "Loại thiết bị là bắt buộc"));

                if (dto.Latitude == 0 || dto.Longitude == 0)
                    return BadRequest(new BaseCommentResponse(400, "Vĩ độ và kinh độ là bắt buộc"));

                var sensorId = await _unitOfWork.ManageSensorRepository.AddNewSensorAsync(dto);

                if (sensorId == 0)
                    return BadRequest(new BaseCommentResponse(400, "Tạo thiết bị không thành công."));

                return Ok(new { StatusCode = 200, Message = "Tạo thiết bị thành công", SensorId = sensorId });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new BaseCommentResponse(500, $"Đã xảy ra lỗi máy chủ nội bộ: {ex.Message}"));
            }
        }

        [HttpPut("devices/{id}")]
        public async Task<ActionResult> UpdateDevice(int id, [FromBody] UpdateSensorDTO dto)
        {
            try
            {
                if (id <= 0)
                    return BadRequest(new BaseCommentResponse(400, "ID thiết bị không hợp lệ"));

                if (dto == null)
                    return BadRequest(new BaseCommentResponse(400, "Cần cập nhật dữ liệu"));

                if (!dto.PlaceId.HasValue &&
                    !dto.TechnicianId.HasValue &&
                    string.IsNullOrEmpty(dto.Specification) &&
                    string.IsNullOrEmpty(dto.SensorCode) &&
                    string.IsNullOrEmpty(dto.SensorName) &&
                    string.IsNullOrEmpty(dto.Protocol) &&
                    string.IsNullOrEmpty(dto.SensorType) &&
                    !dto.WarningThreshold.HasValue &&
                    !dto.DangerThreshold.HasValue &&
                    !dto.MaxLevel.HasValue)
                {
                    return BadRequest(new BaseCommentResponse(400, "Cần cung cấp ít nhất một trường để cập nhật"));
                }

                var existing = await _unitOfWork.ManageSensorRepository.GetByIdAsync(id);
                if (existing == null)
                    return NotFound(new BaseCommentResponse(404, "Không tìm thấy thiết bị"));

                var result = await _unitOfWork.ManageSensorRepository.UpdateSensorAsync(id, dto);

                if (!result)
                    return BadRequest(new BaseCommentResponse(400, "Cập nhật thiết bị không thành công. Vui lòng kiểm tra vị trí, người lắp hoặc mã thiết bị."));

                return Ok(new BaseCommentResponse(200, "Cập nhật thiết bị thành công"));
            }
            catch (Exception ex)
            {
                return StatusCode(500, new BaseCommentResponse(500, $"Đã xảy ra lỗi máy chủ nội bộ: {ex.Message}"));
            }
        }

        [HttpDelete("devices/{id}")]
        public async Task<ActionResult> DeleteDevice(int id)
        {
            try
            {
                if (id <= 0)
                    return BadRequest(new BaseCommentResponse(400, "ID thiết bị không hợp lệ"));

                var result = await _unitOfWork.ManageSensorRepository.DeleteSensorAsync(id);

                if (result == null)
                    return NotFound(new BaseCommentResponse(404, "Không tìm thấy thiết bị"));

                if (result == false)
                    return Conflict(new BaseCommentResponse(409, "Không thể xóa thiết bị vì còn lịch bảo trì hoặc yêu cầu bảo trì liên quan."));

                return Ok(new BaseCommentResponse(200, "Đã gỡ thiết bị khỏi hệ thống thành công"));
            }
            catch (Exception ex)
            {
                return StatusCode(500, new BaseCommentResponse(500, $"Đã xảy ra lỗi máy chủ nội bộ: {ex.Message}"));
            }
        }

        //[HttpPut("{id}/threshold")]
        //public async Task<IActionResult> UpdateThreshold(int id, UpdateThresholdDTO dto)
        //{
        //    var sensor = await _repo.GetById(id);
        //    if (sensor == null) return NotFound();

        //    sensor.WarningThreshold = dto.Warning;
        //    sensor.DangerThreshold = dto.Danger;

        //    await _repo.Update(sensor);

        //    return Ok();
        //}



    }
}