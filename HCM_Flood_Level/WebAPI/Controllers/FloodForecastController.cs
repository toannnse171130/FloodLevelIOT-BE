using Core.DTOs;
using Core.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WebAPI.Errors;

namespace WebAPI.Controllers
{
    [Route("api/citizen/flood-forecast")]
    [ApiController]
    [Authorize(Roles = "Citizen")]
    public class FloodForecastController : ControllerBase
    {
        private readonly IFloodForecastService _floodForecastService;

        public FloodForecastController(IFloodForecastService floodForecastService)
        {
            _floodForecastService = floodForecastService;
        }

        [HttpPost("run")]
        public async Task<IActionResult> RunForecast([FromBody] FloodForecastRequestDto dto, CancellationToken cancellationToken)
        {
            if (dto == null)
                return BadRequest(new BaseCommentResponse(400, "Payload không hợp lệ."));

            if (dto.Latitude is < -90 or > 90 || dto.Longitude is < -180 or > 180)
                return BadRequest(new BaseCommentResponse(400, "Tọa độ không hợp lệ (lat: -90..90, lon: -180..180)."));

            try
            {
                var radiusKm = dto.RadiusKm <= 0 ? 3.0 : dto.RadiusKm;
                var result = await _floodForecastService.RunForecastForCitizenAsync(
                    dto.Latitude,
                    dto.Longitude,
                    radiusKm,
                    dto.DataDaysBack,
                    cancellationToken);
                if (result == null)
                    return BadRequest(new BaseCommentResponse(400, "Không thể khởi tạo mô hình dự báo (tọa độ không hợp lệ)."));

                return Ok(result);
            }
            catch (InvalidOperationException ex)
            {
                return StatusCode(502, new BaseCommentResponse(502, ex.Message));
            }
            catch (Exception ex)
            {
                return StatusCode(500, new BaseCommentResponse(500, "Đã xảy ra lỗi máy chủ nội bộ!!!"));
            }
        }
    }
}
