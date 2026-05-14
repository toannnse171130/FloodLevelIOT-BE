using AutoMapper;
using Core.DTOs;
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

        [HttpGet("histories")]
        public async Task<ActionResult> GetAllHistories(
            [FromQuery] int pagenumber = 1,
            [FromQuery] int pagesize = 20,
            [FromQuery] string? search = null)
        {
            try
            {
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
    }
}
