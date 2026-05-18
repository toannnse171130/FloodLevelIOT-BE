using AutoMapper;
using Core.DTOs;
using Core.Entities;
using Core.Interfaces;
using Core.Sharing;
using Microsoft.AspNetCore.Mvc;
using WebAPI.Errors;
using WebAPI.Helpers;

namespace WebAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class HistoryController : ControllerBase
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly IMapper _mapper;

        public HistoryController(IUnitOfWork unitOfWork, IMapper mapper)
        {
            _unitOfWork = unitOfWork;
            _mapper = mapper;
        }

        /// <summary>
        /// Mobile: ?hours=24&amp;limit=0 — trả về mảng, mới nhất trước.
        /// Admin: phân trang pagenumber/pagesize.
        /// </summary>
        [HttpGet("histories")]
        public async Task<ActionResult> GetAllHistories(
            [FromQuery] int? placeId = null,
            [FromQuery] int hours = 0,
            [FromQuery] int limit = 0,
            [FromQuery] int pagenumber = 1,
            [FromQuery] int pagesize = 20,
            [FromQuery] string? search = null)
        {
            try
            {
                if (hours > 0 || placeId.HasValue || limit > 0)
                {
                    var filtered = await _unitOfWork.HistoryRepository.GetFilteredAsync(placeId, hours, limit);
                    var list = _mapper.Map<List<HistoryDTO>>(filtered);
                    return Ok(list);
                }

                if (pagenumber <= 0 || pagesize <= 0)
                    return BadRequest(new BaseCommentResponse(400, "Số trang và kích thước trang phải lớn hơn 0"));

                var param = new EntityParam
                {
                    Pagenumber = pagenumber,
                    Pagesize = pagesize,
                    Search = search
                };

                var histories = await _unitOfWork.HistoryRepository.GetAllAsync(param);
                var total = await _unitOfWork.HistoryRepository.CountAsync();
                var result = _mapper.Map<List<HistoryDTO>>(histories);

                return Ok(new Pagination<HistoryDTO>(pagesize, pagenumber, total, result));
            }
            catch (Exception ex)
            {
                return StatusCode(500, new BaseCommentResponse(500, $"Đã xảy ra lỗi máy chủ nội bộ: {ex.Message}"));
            }
        }

        [HttpPost]
        public async Task<ActionResult> CreateHistory([FromBody] HistoryDTO dto)
        {
            try
            {
                var history = new History
                {
                    LocationId = dto.LocationId,
                    StartTime = dto.StartTime,
                    EndTime = dto.EndTime,
                    MaxWaterLevel = dto.MaxWaterLevel,
                    Severity = Enum.TryParse<Severity>(dto.Severity, out var parsedSeverity)
                        ? parsedSeverity
                        : Severity.Safe,
                    CreatedAt = DateTime.UtcNow
                };

                await _unitOfWork.HistoryRepository.AddAsync(history);
                return Ok(new BaseCommentResponse(200, "Tạo lịch sử thành công"));
            }
            catch (Exception ex)
            {
                return StatusCode(500, new BaseCommentResponse(500, $"Đã xảy ra lỗi máy chủ nội bộ: {ex.Message}"));
            }
        }
    }
}
