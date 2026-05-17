using AutoMapper;
using Core.DTOs;
using Core.Interfaces;
using Core.Sharing;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using WebAPI.Errors;
using WebAPI.Helpers;

namespace WebAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ScheduleController : ControllerBase
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly IMapper _mapper;

        public ScheduleController(IUnitOfWork unitOfWork, IMapper mapper)
        {
            _mapper = mapper;
            _unitOfWork = unitOfWork;
        }

        [Authorize(Roles = "Staff")]
        [HttpPost("sensors/auto-schedules/{sensorId}")]
        public async Task<ActionResult> ExecuteAutoScheduleForSensor(int sensorId)
        {
            try
            {
                var sensorExists = await _unitOfWork.ManageSensorRepository.GetByIdAsync(sensorId);
                if (sensorExists == null)
                    return NotFound(new BaseCommentResponse(404, "Không tìm thấy sensor"));

                var result = await _unitOfWork.ManageMaintenanceScheduleRepository.AddAutoScheduleAsync(sensorId);
                if (!result)
                    return BadRequest(new BaseCommentResponse(400, "Sensor vẫn còn lịch bảo trì chưa Completed. Chỉ được tạo lịch mới khi lịch hiện tại đã Completed."));

                return Ok(new BaseCommentResponse(200, "Đã tạo lịch bảo trì auto cho sensor thành công"));
            }
            catch (Exception)
            {
                return StatusCode(500, new BaseCommentResponse(500, "Đã xảy ra lỗi máy chủ nội bộ!!!"));
            }
        }

        [Authorize(Roles = "Staff")]
        [HttpPost("schedules")]
        public async Task<ActionResult> CreateSchedule([FromBody] CreateMaintenanceScheduleDTO dto)
        {
            try
            {
                if (!ModelState.IsValid)
                    return BadRequest(new BaseCommentResponse(400, "Dữ liệu đầu vào không hợp lệ"));

                if (dto == null)
                    return BadRequest(new BaseCommentResponse(400, "Dữ liệu lịch bảo trì là bắt buộc"));

                var scheduleTypeError = ScheduleValidator.ValidateScheduleDates(dto.ScheduleType, dto.StartDate, dto.EndDate);
                if (scheduleTypeError != null)
                    return BadRequest(new BaseCommentResponse(400, scheduleTypeError));

                var result = await _unitOfWork.ManageMaintenanceScheduleRepository.AddNewScheduleAsync(dto);

                if (!result)
                    return BadRequest(new BaseCommentResponse(400, "Sensor vẫn còn lịch bảo trì chưa Completed. Chỉ được tạo lịch mới khi lịch hiện tại đã Completed."));

                return Ok(new BaseCommentResponse(200, "Tạo lịch bảo trì thành công"));
            }
            catch (Exception)
            {
                return StatusCode(500, new BaseCommentResponse(500, "Đã xảy ra lỗi máy chủ nội bộ!!!"));
            }
        }

        [Authorize(Roles = "Staff")]
        [HttpGet("staff-schedules")]
        public async Task<ActionResult> GetAllSchedules([FromQuery] int pagenumber = 1, [FromQuery] int pagesize = 10, [FromQuery] string? status = null, [FromQuery] string? type = null, [FromQuery] string? mode = null)
        {
            try
            {
                var schedules = await _unitOfWork.ManageMaintenanceScheduleRepository.GetAllSchedulesAsync(new EntityParam
                {
                    Pagenumber = pagenumber,
                    Pagesize = pagesize,
                    ScheduleStatus = status,
                    ScheduleType = type,
                    ScheduleMode = mode
                });

                var total = await _unitOfWork.ManageMaintenanceScheduleRepository.CountAsync();

                var result = _mapper.Map<List<ScheduleDTO>>(schedules);

                return Ok(new Pagination<ScheduleDTO>(pagesize, pagenumber, total, result));
            }
            catch (Exception ex)
            {
                return StatusCode(500, new BaseCommentResponse(500, "Đã xảy ra lỗi máy chủ nội bộ!!!"));
            }
        }

        [Authorize(Roles = "Staff,Technician")]
        [HttpPut("schedules/{id}")]
        public async Task<ActionResult> UpdateSchedule(int id, [FromBody] UpdateMaintenanceScheduleDTO dto)
        {
            try
            {
                if (!ModelState.IsValid)
                    return BadRequest(new BaseCommentResponse(400, "Dữ liệu đầu vào không hợp lệ"));
                if (dto == null)
                    return BadRequest(new BaseCommentResponse(400, "Dữ liệu lịch bảo trì là bắt buộc"));

                var existing = await _unitOfWork.ManageMaintenanceScheduleRepository.GetByIdAsync(id);
                if (existing == null)
                    return NotFound(new BaseCommentResponse(404, "Không tìm thấy lịch bảo trì"));

                var effectiveType = !string.IsNullOrWhiteSpace(dto.ScheduleType) ? dto.ScheduleType : existing.ScheduleType;
                var effectiveStart = dto.StartDate ?? existing.StartDate;
                var effectiveEnd = dto.EndDate ?? existing.EndDate;

                var scheduleTypeError = ScheduleValidator.ValidateScheduleDates(effectiveType, effectiveStart, effectiveEnd);
                if (scheduleTypeError != null)
                    return BadRequest(new BaseCommentResponse(400, scheduleTypeError));

                var result = await _unitOfWork.ManageMaintenanceScheduleRepository.UpdateScheduleAsync(id, dto);
                if (!result)
                    return BadRequest(new BaseCommentResponse(400, "Cập nhật lịch bảo trì không thành công"));
                return Ok(new BaseCommentResponse(200, "Cập nhật lịch bảo trì thành công"));
            }
            catch (Exception ex)
            {
                return StatusCode(500, new BaseCommentResponse(500, "Đã xảy ra lỗi máy chủ nội bộ!!!"));
            }
        }

        [Authorize(Roles = "Staff")]
        [HttpDelete("schedules/{id}")]
        public async Task<ActionResult> DeleteSchedule(int id)
        {
            try
            {
                if (id <= 0)
                    return BadRequest(new BaseCommentResponse(400, "ID người dùng không hợp lệ"));
                var result = await _unitOfWork.ManageMaintenanceScheduleRepository.DeleteScheduleAsync(id);
                if (!result)
                    return BadRequest(new BaseCommentResponse(400, "Xóa lịch bảo trì không thành công"));
                if (result == false)
                    return NotFound(new BaseCommentResponse(404, "Không tìm thấy lịch bảo trì"));
                return Ok(new BaseCommentResponse(200, "Xóa lịch bảo trì thành công"));
            }
            catch (Exception ex)
            {
                return StatusCode(500, new BaseCommentResponse(500, "Đã xảy ra lỗi máy chủ nội bộ!!!"));
            }
        }

        [Authorize(Roles = "Technician")]
        [HttpGet("technician-schedules")]
        public async Task<ActionResult> GetMySchedules([FromQuery] int pagenumber = 1, [FromQuery] int pazesize = 10, [FromQuery] string? status = null, [FromQuery] string? type = null)
        {
            try
            {
                var userIdString = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                                   ?? User.FindFirst("sub")?.Value;

                if (string.IsNullOrEmpty(userIdString) || !int.TryParse(userIdString, out var technicianId))
                {
                    return Unauthorized(new BaseCommentResponse(401, "Không xác định được danh tính kỹ thuật viên"));
                }

                var schedules = await _unitOfWork.ManageMaintenanceScheduleRepository.GetSchedulesByTechnicianAsync(technicianId, new EntityParam
                {
                    Pagenumber = pagenumber,
                    Pagesize = pazesize,
                    ScheduleStatus = status,
                    ScheduleType = type
                });

                var total = await _unitOfWork.ManageMaintenanceScheduleRepository.CountAsync(s => s.AssignedTechnicianId == technicianId);

                var result = _mapper.Map<List<ScheduleDTO>>(schedules);

                return Ok(new Pagination<ScheduleDTO>(pazesize, pagenumber, total, result));
            }
            catch (Exception ex)
            {
                return StatusCode(500, new BaseCommentResponse(500, "Đã xảy ra lỗi máy chủ nội bộ: " + ex.Message));
            }
        }

        [Authorize(Roles = "Technician")]
        [HttpPut("schedules/status/{id}")]
        public async Task<ActionResult> UpdateMyScheduleStatus(int id, [FromBody] UpdateScheduleStatusDTO dto)
        {
            try
            {
                if (dto == null || string.IsNullOrWhiteSpace(dto.Status))
                    return BadRequest(new BaseCommentResponse(400, "Trạng thái cập nhật không hợp lệ"));

                var userIdString = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                                   ?? User.FindFirst("sub")?.Value;

                if (string.IsNullOrEmpty(userIdString) || !int.TryParse(userIdString, out var technicianId))
                {
                    return Unauthorized(new BaseCommentResponse(401, "Không xác định được danh tính kỹ thuật viên"));
                }

                // Kiểm tra quyền sở hữu tại Controller
                var schedule = await _unitOfWork.ManageMaintenanceScheduleRepository.GetByIdAsync(id);
                if (schedule == null || schedule.AssignedTechnicianId != technicianId)
                {
                    return NotFound(new BaseCommentResponse(404, "Lịch bảo trì không tồn tại hoặc không được giao cho bạn"));
                }

                var result = await _unitOfWork.ManageMaintenanceScheduleRepository.UpdateScheduleStatusAsync(id, dto);

                if (!result)
                {
                    return BadRequest(new BaseCommentResponse(400, "Cập nhật trạng thái không thành công (trạng thái không hợp lệ)"));
                }

                return Ok(new BaseCommentResponse(200, "Cập nhật trạng thái thành công"));
            }
            catch (Exception ex)
            {
                return StatusCode(500, new BaseCommentResponse(500, "Đã xảy ra lỗi máy chủ nội bộ: " + ex.Message));
            }
        }
    }
}
