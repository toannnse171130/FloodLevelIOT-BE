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
    public class RequestController : ControllerBase
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly IMapper _mapper;

        public RequestController(IUnitOfWork unitOfWork, IMapper mapper)
        {
            _unitOfWork = unitOfWork;
            _mapper = mapper;
        }

        [Authorize(Roles = "Staff")]
        [HttpPost("staff-requests")]
        public async Task<IActionResult> StaffCreateRequestAsync([FromBody] StaffCreateRequestDTO dto)
        {
            try
            {
                if (!ModelState.IsValid)
                    return BadRequest(new BaseCommentResponse(400, "Dữ liệu đầu vào không hợp lệ"));
                if (dto == null)
                    return BadRequest(new BaseCommentResponse(400, "Dữ liệu yêu cầu bảo trì là bắt buộc"));
                var result = await _unitOfWork.ManageRequestRepository.StaffCreateRequestAsync(dto);
                if (!result)
                    return BadRequest(new BaseCommentResponse(400, "Tạo yêu cầu bảo trì không thành công"));
                return Ok(new BaseCommentResponse(200, "Tạo yêu cầu bảo trì thành công"));
            }
            catch (Exception ex)
            {
                return StatusCode(400, new BaseCommentResponse(400, ex.Message));
            }
        }

        [Authorize(Roles = "Staff")]
        [HttpGet("staff-requests")]
        public async Task<ActionResult> StaffGetRequestAsync([FromQuery] int pagenumber = 1, [FromQuery] int pagesize = 10, [FromQuery] string? status = null, [FromQuery] string? priority = null)
        {
            try
            {
                var requests = await _unitOfWork.ManageRequestRepository.StaffGetRequestAsync(new EntityParam
                {
                    Pagenumber = pagenumber,
                    Pagesize = pagesize,
                    RequestStatus = status,
                    RequestPriority = priority
                });

                var total = await _unitOfWork.ManageRequestRepository.CountAsync();

                var result = _mapper.Map<List<RequestDTO>>(requests);

                return Ok(new Pagination<RequestDTO>(pagesize, pagenumber, total, result));
            }
            catch (Exception ex)
            {
                return StatusCode(500, new BaseCommentResponse(500, "Đã xảy ra lỗi máy chủ nội bộ: " + ex.Message));
            }
        }

        [Authorize(Roles = "Staff")]
        [HttpPut("staff-requests/{id}")]
        public async Task<IActionResult> StaffUpdateRequestAsync(int id, [FromBody] StaffUpdateRequestDTO dto)
        {
            try
            {
                if (dto == null)
                    return BadRequest(new BaseCommentResponse(400, "Dữ liệu cập nhật là bắt buộc"));
                var result = await _unitOfWork.ManageRequestRepository.StaffUpdateRequestAsync(id, dto);
                if (!result)
                    return NotFound(new BaseCommentResponse(404, "Yêu cầu bảo trì không tồn tại"));
                return Ok(new BaseCommentResponse(200, "Cập nhật yêu cầu bảo trì thành công"));
            }
            catch (Exception ex)
            {
                return StatusCode(400, new BaseCommentResponse(400, ex.Message));
            }
        }

        [Authorize(Roles = "Staff")]
        [HttpDelete("staff-requests/{id}")]
        public async Task<IActionResult> StaffDeleteRequestAsync(int id)
        {
            try
            {
                var result = await _unitOfWork.ManageRequestRepository.StaffDeleteRequestAsync(id);
                if (!result)
                    return NotFound(new BaseCommentResponse(404, "Yêu cầu bảo trì không tồn tại"));
                return Ok(new BaseCommentResponse(200, "Xóa yêu cầu bảo trì thành công"));
            }
            catch (Exception ex)
            {
                return StatusCode(400, new BaseCommentResponse(400, ex.Message));
            }
        }

        [Authorize(Roles = "Technician")]
        [HttpPut("technician-requests/status/{id}")]
        public async Task<IActionResult> TechnicianUpdateStatusAsync(int id, [FromBody] TechnicianUpdateStatusDTO dto)
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
                var request = await _unitOfWork.ManageRequestRepository.GetByIdAsync(id);
                if (request == null || request.AssignedTechnicianTo != technicianId)
                {
                    return NotFound(new BaseCommentResponse(404, "Yêu cầu bảo trì không tồn tại hoặc không được giao cho bạn"));
                }

                var result = await _unitOfWork.ManageRequestRepository.TechnicianUpdateStatusAsync(id, dto);
                if (!result)
                    return BadRequest(new BaseCommentResponse(400, "Cập nhật trạng thái không thành công"));

                return Ok(new BaseCommentResponse(200, "Cập nhật trạng thái thành công"));
            }
            catch (Exception ex)
            {
                return StatusCode(500, new BaseCommentResponse(500, "Đã xảy ra lỗi máy chủ nội bộ: " + ex.Message));
            }
        }

        [Authorize(Roles = "Technician")]
        [HttpGet("technician-requests")]
        public async Task<ActionResult> TechnicianGetRequestAsync([FromQuery] int pagenumber = 1, [FromQuery] int pazesize = 10, [FromQuery] string? status = null, [FromQuery] string? priority = null)
        {
            try
            {
                var userIdString = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                                   ?? User.FindFirst("sub")?.Value;

                if (string.IsNullOrEmpty(userIdString) || !int.TryParse(userIdString, out var technicianId))
                {
                    return Unauthorized(new BaseCommentResponse(401, "Không xác định được danh tính kỹ thuật viên"));
                }

                var requests = await _unitOfWork.ManageRequestRepository.TechnicianGetRequestAsync(technicianId, new EntityParam
                {
                    Pagenumber = pagenumber,
                    Pagesize = pazesize,
                    RequestStatus = status,
                    RequestPriority = priority
                });

                var total = await _unitOfWork.ManageRequestRepository.CountAsync(r => r.AssignedTechnicianTo == technicianId);
                
                var result = _mapper.Map<List<RequestDTO>>(requests);

                return Ok(new Pagination<RequestDTO>(pazesize, pagenumber, total, result));
            }
            catch (Exception ex)
            {
                return StatusCode(500, new BaseCommentResponse(500, "Đã xảy ra lỗi máy chủ nội bộ: " + ex.Message));
            }
        }
    }
}
