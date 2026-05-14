using AutoMapper;
using Core.DTOs;
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
    public class SensorReadingController : ControllerBase
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly IMapper _mapper;

        public SensorReadingController(IUnitOfWork unitOfWork, IMapper mapper)
        {
            _unitOfWork = unitOfWork;
            _mapper = mapper;
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

        /// <summary>
        /// Lấy danh sách sensor readings có phân trang và lọc theo sensorId.
        /// </summary>
        [HttpGet("sensor-readings")]
        public async Task<ActionResult> GetAllSensorReadings(
            [FromQuery] int pagenumber = 1,
            [FromQuery] int pagesize = 20,
            [FromQuery] int? sensorId = null)
        {
            try
            {
                if (pagenumber <= 0 || pagesize <= 0)
                    return BadRequest(new BaseCommentResponse(400, "Số trang và kích thước trang phải lớn hơn 0"));

                var param = new EntityParam
                {
                    Pagenumber = pagenumber,
                    Pagesize = pagesize,
                    SensorId = sensorId
                };

                var readings = await _unitOfWork.SensorReadingRepository.GetAllAsync(param);
                var total = await _unitOfWork.SensorReadingRepository.CountAsync(sensorId);
                var result = _mapper.Map<List<SensorReadingDTO>>(readings);

                return Ok(new Pagination<SensorReadingDTO>(pagesize, pagenumber, total, result));
            }
            catch (Exception ex)
            {
                return StatusCode(500, new BaseCommentResponse(500, $"Đã xảy ra lỗi máy chủ nội bộ: {ex.Message}"));
            }
        }
    }
}
