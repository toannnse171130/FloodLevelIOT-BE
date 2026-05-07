using AutoMapper;
using Core.DTOs;
using Infrastructure.DBContext;
using Microsoft.AspNetCore.Authorization;
using Core.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebAPI.Errors;

namespace WebAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class SensorReadingController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly IMapper _mapper;
        private readonly IHistoryService _historyService;

        public SensorReadingController(AppDbContext context, IMapper mapper, IHistoryService historyService)
        {
            _context = context;
            _mapper = mapper;
            _historyService = historyService;
        }

        /// <summary>
        /// Lấy 10 MQTT message gần nhất từ HiveMQ (debug).
        /// </summary>
        [HttpGet("mqtt-log")]
        public ActionResult<List<string>> GetMqttLog()
        {
            var messages = MqttSubscriberService.GetRecentMessages();
            return Ok(new { count = messages.Count, messages });
        }

        [HttpGet("sensor-readings")]
        public async Task<ActionResult<List<SensorReadingDTO>>> GetAllSensorReadings()
        {
            try
            {
                var readings = await _context.SensorReadings
                    .OrderByDescending(r => r.RecordedAt)
                    .ToListAsync();

                var result = _mapper.Map<List<SensorReadingDTO>>(readings);

                // Process each reading through the HistoryService
                foreach (var reading in readings)
                {
                    await _historyService.ProcessSensorReading(reading);
                }

                return Ok(result);
            }
            catch (Exception)
            {
                return StatusCode(500, new BaseCommentResponse(500, "Đã xảy ra lỗi máy chủ nội bộ!!!"));
            }
        }
    }
}

