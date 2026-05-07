using AutoMapper;
using Core.DTOs;
using Core.Entities;
using Core.Interfaces;
using Core.Services;
using Infrastructure.DBContext;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.ComponentModel.DataAnnotations;
using WebAPI.Errors;

namespace WebAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AuthController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly ITokenService _tokenService;
        private readonly IUnitOfWork _unitOfWork;
        private readonly IConfiguration _configuration;
        private readonly INotificationService _notificationService;
        private readonly IMapper _mapper;

        public AuthController(AppDbContext context, ITokenService tokenService, IUnitOfWork unitOfWork, IConfiguration configuration, INotificationService notificationService, IMapper mapper)
        {
            _context = context;
            _tokenService = tokenService;
            _unitOfWork = unitOfWork;
            _configuration = configuration;
            _notificationService = notificationService;
            _mapper = mapper;
        }

        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginDTO login)
        {
            try
            {
                if (!ModelState.IsValid)
                    return BadRequest(new BaseCommentResponse(400, "Dữ liệu đầu vào không hợp lệ"));

                if (login == null)
                    return BadRequest(new BaseCommentResponse(400, "Thiếu thông tin đăng nhập"));

                if (string.IsNullOrWhiteSpace(login.Email))
                    return BadRequest(new BaseCommentResponse(400, "Email là bắt buộc"));

                if (!new EmailAddressAttribute().IsValid(login.Email))
                    return BadRequest(new BaseCommentResponse(400, "Định dạng email không hợp lệ"));

                if (string.IsNullOrWhiteSpace(login.Password))
                    return BadRequest(new BaseCommentResponse(400, "Mật khẩu là bắt buộc"));

                var authFailMsg = "Email hoặc mật khẩu không đúng";

                var log = await _context.Users
                    .Include(u => u.Role)
                    .FirstOrDefaultAsync(u => u.Email == login.Email);

                if (log == null)
                    return Unauthorized(new BaseCommentResponse(401, authFailMsg));

                if(!PasswordHelper.VerifyPassword(login.Password, log.PasswordHash))
                    return Unauthorized(new BaseCommentResponse(401, authFailMsg));

                if (!log.IsActive)
                    return Unauthorized(new BaseCommentResponse(401, "Tài khoản chưa xác nhận OTP"));

                var roleName = log.Role?.RoleName ?? string.Empty;
                var token = _tokenService.CreateToken(log, roleName);

                return Ok(new {token});
            }
            catch (Exception ex)
            {
                return StatusCode(500, new BaseCommentResponse(500, $"Đã xảy ra lỗi máy chủ nội bộ!!! Chi tiết: {ex.Message}"));
            }
        }

        [HttpPost("logout")]
        [Authorize]
        public async Task<IActionResult> Logout()
        {
            try
            {
                return Ok(new BaseCommentResponse(200, "Đăng xuất thành công"));
            }
            catch (Exception ex)
            {
                return StatusCode(500, new BaseCommentResponse(500, $"Đã xảy ra lỗi máy chủ nội bộ!!! Chi tiết: {ex.Message}"));
            }
        }

        [HttpPost("register")]
        public async Task<IActionResult> Register([FromBody] RegisterDTO dto)
        {
            try
            {
                if (!ModelState.IsValid)
                    return BadRequest(new BaseCommentResponse(400, "Dữ liệu đầu vào không hợp lệ"));

                if (dto == null)
                    return BadRequest(new BaseCommentResponse(400, "Thiếu thông tin đăng ký"));

                if (string.IsNullOrWhiteSpace(dto.Email))
                    return BadRequest(new BaseCommentResponse(400, "Email là bắt buộc"));

                if (!new EmailAddressAttribute().IsValid(dto.Email))
                    return BadRequest(new BaseCommentResponse(400, "Định dạng email không hợp lệ"));

                if (string.IsNullOrWhiteSpace(dto.Password))
                    return BadRequest(new BaseCommentResponse(400, "Mật khẩu là bắt buộc"));

                var existedEmail = await _context.Users
                    .FirstOrDefaultAsync(u => u.Email == dto.Email);

                var defaultRoleId = 3;
                User user;

                if (existedEmail != null)
                {
                    if (existedEmail.IsActive)
                        return BadRequest(new BaseCommentResponse(400, "Email đã tồn tại và đã được kích hoạt"));

                    // Reuse inactive account — update credentials and activate
                    user = existedEmail;
                    user.FullName = dto.FullName ?? string.Empty;
                    user.PhoneNumber = dto.PhoneNumber;
                    user.PasswordHash = PasswordHelper.HashPassword(dto.Password);
                    user.IsActive = true;
                    _context.Users.Update(user);
                }
                else
                {
                    user = new User
                    {
                        FullName = dto.FullName ?? string.Empty,
                        Email = dto.Email.Trim(),
                        PhoneNumber = dto.PhoneNumber,
                        PasswordHash = PasswordHelper.HashPassword(dto.Password),
                        RoleId = defaultRoleId,
                        IsActive = true,
                        CreatedAt = DateTime.UtcNow
                    };
                    _context.Users.Add(user);
                }

                await _context.SaveChangesAsync();

                return Ok(new BaseCommentResponse(200, "Đăng ký thành công. Vui lòng đăng nhập."));
            }
            catch (Exception ex)
            {
                return StatusCode(500, new BaseCommentResponse(500, $"Đã xảy ra lỗi máy chủ nội bộ!!! Chi tiết: {ex.Message}"));
            }
        }

        [HttpPost("verify-email-otp")]
        public async Task<IActionResult> VerifyEmailOtp([FromBody] VerifyEmailOtpDTO dto)
        {
            try
            {
                if (!ModelState.IsValid)
                    return BadRequest(new BaseCommentResponse(400, "Dữ liệu đầu vào không hợp lệ"));

                if (dto == null)
                    return BadRequest(new BaseCommentResponse(400, "Thiếu thông tin xác nhận OTP"));

                if (string.IsNullOrWhiteSpace(dto.Email))
                    return BadRequest(new BaseCommentResponse(400, "Email là bắt buộc"));

                if (string.IsNullOrWhiteSpace(dto.Otp))
                    return BadRequest(new BaseCommentResponse(400, "OTP là bắt buộc"));

                var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == dto.Email);
                if (user == null)
                    return NotFound(new BaseCommentResponse(404, "Không tìm thấy người dùng"));

                if (user.IsActive)
                    return Ok(new BaseCommentResponse(200, "Tài khoản đã được xác nhận trước đó"));

                if (user.EmailOtpExpiredAt == null || user.EmailOtpExpiredAt < DateTime.UtcNow)
                    return BadRequest(new BaseCommentResponse(400, "OTP đã hết hạn"));

                var inputHash = OtpHelper.HashOtpSha256(dto.Otp.Trim());
                if (!string.Equals(inputHash, user.EmailOtpHash, StringComparison.OrdinalIgnoreCase))
                    return BadRequest(new BaseCommentResponse(400, "OTP không đúng"));

                user.IsActive = true;
                user.EmailOtpHash = null;
                user.EmailOtpExpiredAt = null;

                await _context.SaveChangesAsync();

                return Ok(new BaseCommentResponse(200, "Xác nhận OTP thành công. Tài khoản đã được kích hoạt."));
            }
            catch (Exception ex)
            {
                return StatusCode(500, new BaseCommentResponse(500, "Đã xảy ra lỗi máy chủ nội bộ!!!"));
            }
        }

        [HttpPost("forgot-password")]
        public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordDTO dto)
        {
            try
            {
                if (!ModelState.IsValid)
                    return BadRequest(new BaseCommentResponse(400, "Dữ liệu đầu vào không hợp lệ"));

                var successMsg = "Nếu email tồn tại trong hệ thống, mã OTP sẽ được gửi.";

                var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == dto.Email);
                if (user == null)
                    return Ok(new BaseCommentResponse(200, successMsg));

                var otp = OtpHelper.GenerateOtp6();
                var otpHash = OtpHelper.HashOtpSha256(otp);

                user.EmailOtpHash = otpHash;
                user.EmailOtpExpiredAt = DateTime.UtcNow.AddMinutes(10);

                await _context.SaveChangesAsync();

                try
                {
                    var subject = "Mã OTP đặt lại mật khẩu";
                    var body =
                        $@"Xin chào {user.FullName},

                        Mã OTP để đặt lại mật khẩu của bạn là: {otp}
                        Mã có hiệu lực trong 10 phút.

                        Trân trọng.";
                    await _notificationService.SendEmailAsync(user.Email!, subject, body);
                }
                catch (Exception)
                {
                    // Email failed but don't reveal user existence
                }

                return Ok(new BaseCommentResponse(200, successMsg));
            }
            catch (Exception ex)
            {
                return StatusCode(500, new BaseCommentResponse(500, "Đã xảy ra lỗi máy chủ nội bộ!!!"));
            }
        }

        [HttpPost("reset-password")]
        public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordDTO dto)
        {
            try
            {
                if (!ModelState.IsValid)
                    return BadRequest(new BaseCommentResponse(400, "Dữ liệu đầu vào không hợp lệ"));

                var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == dto.Email);
                if (user == null)
                    return NotFound(new BaseCommentResponse(404, "Không tìm thấy người dùng với email này"));

                if (user.EmailOtpHash == null || user.EmailOtpExpiredAt < DateTime.UtcNow)
                    return BadRequest(new BaseCommentResponse(400, "OTP không hợp lệ hoặc đã hết hạn"));

                var inputHash = OtpHelper.HashOtpSha256(dto.Otp);
                if (!string.Equals(inputHash, user.EmailOtpHash, StringComparison.OrdinalIgnoreCase))
                    return BadRequest(new BaseCommentResponse(400, "OTP không đúng"));

                user.PasswordHash = PasswordHelper.HashPassword(dto.NewPassword);
                user.EmailOtpHash = null;
                user.EmailOtpExpiredAt = null;

                await _context.SaveChangesAsync();

                try
                {
                    var subject = "Thông báo đổi mật khẩu thành công";
                    var body =
                        $@"Xin chào {user.FullName},

                        Mật khẩu của bạn đã được đổi thành công.

                        Trân trọng.";
                    await _notificationService.SendEmailAsync(user.Email!, subject, body);
                }
                catch (Exception ex)
                {
                    var msg = "Lỗi gửi Mail: " + ex.Message;
                    return StatusCode(500, new BaseCommentResponse(500, msg));
                }

                return Ok(new BaseCommentResponse(200, "Đặt lại mật khẩu thành công."));
            }
            catch (Exception ex)
            {
                return StatusCode(500, new BaseCommentResponse(500, "Đã xảy ra lỗi máy chủ nội bộ!!!"));
            }
        }

        [Authorize]
        [HttpPost("change-password")]
        public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordDTO dto)
        {
            try
            {
                if (!ModelState.IsValid)
                    return BadRequest(new BaseCommentResponse(400, "Dữ liệu đầu vào không hợp lệ"));

                var email = User.FindFirstValue(ClaimTypes.Email);
                if (string.IsNullOrEmpty(email))
                    return Unauthorized(new BaseCommentResponse(401, "Người dùng chưa đăng nhập hoặc token không hợp lệ"));

                var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == email);
                if (user == null)
                    return NotFound(new BaseCommentResponse(404, "Không tìm thấy người dùng"));

                if (!PasswordHelper.VerifyPassword(dto.CurrentPassword, user.PasswordHash))
                    return BadRequest(new BaseCommentResponse(400, "Mật khẩu hiện tại không chính xác"));

                user.PasswordHash = PasswordHelper.HashPassword(dto.NewPassword);
                await _context.SaveChangesAsync();

                try
                {
                    var subject = "Thông báo đổi mật khẩu thành công";
                    var body =
                        $@"Xin chào {user.FullName},

                        Mật khẩu của bạn đã được thay đổi thành công theo yêu cầu.

                        Trân trọng.";
                    await _notificationService.SendEmailAsync(user.Email!, subject, body);
                }
                catch (Exception ex)
                {
                    var msg = "Lỗi gửi Mail: " + ex.Message;
                    return StatusCode(500, new BaseCommentResponse(500, msg));
                }

                return Ok(new BaseCommentResponse(200, "Đổi mật khẩu thành công."));
            }
            catch (Exception ex)
            {
                return StatusCode(500, new BaseCommentResponse(500, "Đã xảy ra lỗi máy chủ nội bộ!!!"));
            }
        }

        [Authorize(Roles = "Citizen")]
        [HttpPut("profile/{id}")]
        public async Task<ActionResult> UpdateProfile(int id, [FromBody] UpdateProfileDTO dto)
        {
            try
            {
                if (id <= 0)
                    return BadRequest(new BaseCommentResponse(400, "ID người dùng không hợp lệ"));
                var result = await _unitOfWork.ManageUserRepository.UpdateProfileAsync(id, dto);
                if (!result)
                    return NotFound(new BaseCommentResponse(404, "Không tìm thấy người dùng"));
                return Ok(new BaseCommentResponse(200, "Cập nhật thông tin cá nhân thành công"));
            }
            catch (Exception ex)
            {
                return StatusCode(500, new BaseCommentResponse(500, "Đã xảy ra lỗi máy chủ nội bộ!!!"));
            }
        }

        [Authorize(Roles = "Citizen")]
        [HttpGet("profile/{id}")]
        public async Task<ActionResult> Profile(int id)
        {
            try
            {
                if (id <= 0)
                    return BadRequest(new BaseCommentResponse(400, "ID người dùng không hợp lệ"));

                var profile = await _unitOfWork.ManageUserRepository.GetByIdAsync(id);

                if (profile == null)
                    return NotFound(new BaseCommentResponse(404, "Không tìm thấy tài khoản"));

                var result = _mapper.Map<ProfileDTO>(profile);
                return Ok(result);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new BaseCommentResponse(500, "Đã xảy ra lỗi máy chủ nội bộ!!!"));
            }
        }
    }
}
