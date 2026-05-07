using AutoMapper;
using Core.DTOs;
using Infrastructure.DBContext;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebAPI.Errors;

namespace WebAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class HistoryController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly IMapper _mapper;

        public HistoryController(AppDbContext context, IMapper mapper)
        {
            _context = context;
            _mapper = mapper;
        }

        [HttpGet("histories")]
        public async Task<ActionResult<List<HistoryDTO>>> GetAllHistories()
        {
            try
            {
                var histories = await _context.Histories
                    .OrderByDescending(e => e.Severity)
                    .ThenByDescending(e => e.CreatedAt)
                    .ToListAsync();

                var result = _mapper.Map<List<HistoryDTO>>(histories);
                return Ok(result);
            }
            catch (Exception)
            {
                return StatusCode(500, new BaseCommentResponse(500, "Đã xảy ra lỗi máy chủ nội bộ!!!"));
            }
        }
    }
}

