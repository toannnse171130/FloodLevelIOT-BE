using AutoMapper;
using Core.DTOs;
using Core.Entities;
using Core.Interfaces;
using Core.Services;
using FakeItEasy;
using Infrastructure.DBContext;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using System.Security.Claims;
using WebAPI.Controllers;
using WebAPI.Errors;

namespace FloodLevelIOT_BE.Test.Controllers;

public class AuthControllerTest
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IUserRepository _userRepository;
    private readonly IMapper _mapper;
    private readonly ITokenService _tokenService;
    private readonly IConfiguration _configuration;
    private readonly INotificationService _notificationService;

    public AuthControllerTest()
    {
        _configuration = A.Fake<IConfiguration>();
        _mapper = A.Fake<IMapper>();
        _notificationService = A.Fake<INotificationService>();
        _tokenService = A.Fake<ITokenService>();
        _unitOfWork = A.Fake<IUnitOfWork>();
        _userRepository = A.Fake<IUserRepository>();
        A.CallTo(() => _unitOfWork.ManageUserRepository).Returns(_userRepository);
    }

    private static AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }

    // Login Tests (4 tests)
    private AuthController CreateController(AppDbContext context)
        => new(context, _tokenService, _unitOfWork, _configuration, _notificationService, _mapper);

    [Fact]
    public async Task Login_WithValidCredentials_ReturnsOkWithToken()
    {
        using var context = CreateContext();
        var role = new Role { RoleId = 3, RoleName = "Citizen" };
        context.Roles.Add(role);
        var user = new User { FullName = "A", Email = "a@test.com", PasswordHash = PasswordHelper.HashPassword("123456"), RoleId = 3, IsActive = true };
        context.Users.Add(user);
        await context.SaveChangesAsync();
        A.CallTo(() => _tokenService.CreateToken(A<User>._, "Citizen")).Returns("token-abc");
        var controller = CreateController(context);

        var result = await controller.Login(new LoginDTO { Email = "a@test.com", Password = "123456" });

        var ok = Assert.IsType<OkObjectResult>(result);
        var token = ok.Value!.GetType().GetProperty("token")!.GetValue(ok.Value) as string;
        Assert.Equal("token-abc", token);
    }

    [Fact]
    public async Task Login_WithInvalidEmail_ReturnsUnauthorized()
    {
        using var context = CreateContext();
        var controller = CreateController(context);

        var result = await controller.Login(new LoginDTO { Email = "no@test.com", Password = "123456" });

        Assert.IsType<UnauthorizedObjectResult>(result);
    }

    [Fact]
    public async Task Login_WithInvalidPassword_ReturnsUnauthorized()
    {
        using var context = CreateContext();
        context.Roles.Add(new Role { RoleId = 3, RoleName = "Citizen" });
        context.Users.Add(new User { FullName = "A", Email = "a@test.com", PasswordHash = PasswordHelper.HashPassword("correct"), RoleId = 3, IsActive = true });
        await context.SaveChangesAsync();
        var controller = CreateController(context);

        var result = await controller.Login(new LoginDTO { Email = "a@test.com", Password = "wrong" });

        Assert.IsType<UnauthorizedObjectResult>(result);
    }

    [Fact]
    public async Task Login_WithMissingEmail_ReturnsBadRequest()
    {
        using var context = CreateContext();
        var controller = CreateController(context);

        var result = await controller.Login(new LoginDTO { Email = "", Password = "123456" });

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task Login_WithMissingPassword_ReturnsBadRequest()
    {
        using var context = CreateContext();
        var controller = CreateController(context);

        var result = await controller.Login(new LoginDTO { Email = "a@test.com", Password = "" });

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task Login_WithNullLoginDto_ReturnsBadRequest()
    {
        using var context = CreateContext();
        var controller = CreateController(context);

        var result = await controller.Login(null!);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task Login_WithInvalidEmailFormat_ReturnsBadRequest()
    {
        using var context = CreateContext();
        var controller = CreateController(context);

        var result = await controller.Login(new LoginDTO { Email = "invalid-email", Password = "123456" });

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        var response = Assert.IsType<BaseCommentResponse>(badRequest.Value);
        Assert.Equal("Định dạng email không hợp lệ", response.Message);
    }

    [Fact]
    public async Task Login_WithInactiveAccount_ReturnsUnauthorized()
    {
        using var context = CreateContext();
        var role = new Role { RoleId = 3, RoleName = "Citizen" };
        context.Roles.Add(role);
        var user = new User { FullName = "A", Email = "inactive@test.com", PasswordHash = PasswordHelper.HashPassword("123456"), RoleId = 3, IsActive = false };
        context.Users.Add(user);
        await context.SaveChangesAsync();
        var controller = CreateController(context);

        var result = await controller.Login(new LoginDTO { Email = "inactive@test.com", Password = "123456" });

        var unauthorized = Assert.IsType<UnauthorizedObjectResult>(result);
        var response = Assert.IsType<BaseCommentResponse>(unauthorized.Value);
        Assert.Equal("Tài khoản chưa xác nhận OTP", response.Message);
    }

    // Register Tests (4 tests)
    [Fact]
    public async Task Register_WithValidData_ReturnsOkAndCreatesUser()
    {
        using var context = CreateContext();
        var controller = CreateController(context);
        var dto = new RegisterDTO { FullName = "A", Email = "new@test.com", Password = "123456", PhoneNumber = "0123" };

        var result = await controller.Register(dto);

        Assert.IsType<OkObjectResult>(result);
        var created = await context.Users.FirstOrDefaultAsync(x => x.Email == "new@test.com");
        Assert.NotNull(created);
        Assert.True(created!.IsActive);
    }

    [Fact]
    public async Task Register_WithExistingActiveEmail_ReturnsBadRequest()
    {
        using var context = CreateContext();
        context.Users.Add(new User { FullName = "A", Email = "exist@test.com", PasswordHash = PasswordHelper.HashPassword("123"), IsActive = true, RoleId = 3 });
        await context.SaveChangesAsync();
        var controller = CreateController(context);

        var result = await controller.Register(new RegisterDTO { FullName = "B", Email = "exist@test.com", Password = "456" });

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task Register_WithExistingInactiveEmail_UpdatesAndActivatesUser()
    {
        using var context = CreateContext();
        context.Users.Add(new User { FullName = "Old", Email = "inactive@test.com", PasswordHash = PasswordHelper.HashPassword("old"), IsActive = false, RoleId = 3 });
        await context.SaveChangesAsync();
        var controller = CreateController(context);

        var result = await controller.Register(new RegisterDTO { FullName = "New Name", Email = "inactive@test.com", Password = "newpass", PhoneNumber = "999" });

        Assert.IsType<OkObjectResult>(result);
        var updated = await context.Users.FirstAsync(x => x.Email == "inactive@test.com");
        Assert.True(updated.IsActive);
        Assert.Equal("New Name", updated.FullName);
        Assert.True(PasswordHelper.VerifyPassword("newpass", updated.PasswordHash));
    }

    [Fact]
    public async Task Register_WithMissingEmail_ReturnsBadRequest()
    {
        using var context = CreateContext();
        var controller = CreateController(context);

        var result = await controller.Register(new RegisterDTO { FullName = "A", Email = "", Password = "123456" });

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task Register_WithMissingPassword_ReturnsBadRequest()
    {
        using var context = CreateContext();
        var controller = CreateController(context);

        var result = await controller.Register(new RegisterDTO { FullName = "A", Email = "a@test.com", Password = "" });

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task Register_WithInvalidEmailFormat_ReturnsBadRequest()
    {
        using var context = CreateContext();
        var controller = CreateController(context);

        var result = await controller.Register(new RegisterDTO { FullName = "A", Email = "invalid-email", Password = "password" });

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        var response = Assert.IsType<BaseCommentResponse>(badRequest.Value);
        Assert.Equal("Định dạng email không hợp lệ", response.Message);
    }

    // OTP & Password Reset Tests (4 tests)
    [Fact]
    public async Task VerifyEmailOtp_WithCorrectOtp_ReturnsOkAndActivatesAccount()
    {
        using var context = CreateContext();
        var otp = "123456";
        context.Users.Add(new User
        {
            FullName = "A",
            Email = "a@test.com",
            PasswordHash = "hash",
            IsActive = false,
            EmailOtpHash = OtpHelper.HashOtpSha256(otp),
            EmailOtpExpiredAt = DateTime.UtcNow.AddMinutes(5),
            RoleId = 3
        });
        await context.SaveChangesAsync();
        var controller = CreateController(context);

        var result = await controller.VerifyEmailOtp(new VerifyEmailOtpDTO { Email = "a@test.com", Otp = otp });

        Assert.IsType<OkObjectResult>(result);
        var user = await context.Users.FirstAsync(x => x.Email == "a@test.com");
        Assert.True(user.IsActive);
    }

    [Fact]
    public async Task VerifyEmailOtp_WithExpiredOtp_ReturnsBadRequest()
    {
        using var context = CreateContext();
        context.Users.Add(new User
        {
            FullName = "A",
            Email = "a@test.com",
            PasswordHash = "hash",
            IsActive = false,
            EmailOtpHash = OtpHelper.HashOtpSha256("123456"),
            EmailOtpExpiredAt = DateTime.UtcNow.AddMinutes(-1),
            RoleId = 3
        });
        await context.SaveChangesAsync();
        var controller = CreateController(context);

        var result = await controller.VerifyEmailOtp(new VerifyEmailOtpDTO { Email = "a@test.com", Otp = "123456" });

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task VerifyEmailOtp_WithIncorrectOtp_ReturnsBadRequest()
    {
        using var context = CreateContext();
        context.Users.Add(new User
        {
            FullName = "A",
            Email = "a@test.com",
            PasswordHash = "hash",
            IsActive = false,
            EmailOtpHash = OtpHelper.HashOtpSha256("123456"),
            EmailOtpExpiredAt = DateTime.UtcNow.AddMinutes(5),
            RoleId = 3
        });
        await context.SaveChangesAsync();
        var controller = CreateController(context);

        var result = await controller.VerifyEmailOtp(new VerifyEmailOtpDTO { Email = "a@test.com", Otp = "000000" });

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task VerifyEmailOtp_WithNonexistentUser_ReturnsNotFound()
    {
        using var context = CreateContext();
        var controller = CreateController(context);

        var result = await controller.VerifyEmailOtp(new VerifyEmailOtpDTO { Email = "none@test.com", Otp = "123456" });

        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task VerifyEmailOtp_WithAlreadyActiveAccount_ReturnsOkWithNotification()
    {
        using var context = CreateContext();
        context.Users.Add(new User
        {
            FullName = "A",
            Email = "active@test.com",
            PasswordHash = "hash",
            IsActive = true,
            RoleId = 3
        });
        await context.SaveChangesAsync();
        var controller = CreateController(context);

        var result = await controller.VerifyEmailOtp(new VerifyEmailOtpDTO { Email = "active@test.com", Otp = "123456" });

        var ok = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<BaseCommentResponse>(ok.Value);
        Assert.Equal("Tài khoản đã được xác nhận trước đó", response.Message);
    }

    [Fact]
    public async Task VerifyEmailOtp_WithMissingEmail_ReturnsBadRequest()
    {
        using var context = CreateContext();
        var controller = CreateController(context);

        var result = await controller.VerifyEmailOtp(new VerifyEmailOtpDTO { Email = "", Otp = "123456" });

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task VerifyEmailOtp_WithMissingOtp_ReturnsBadRequest()
    {
        using var context = CreateContext();
        var controller = CreateController(context);

        var result = await controller.VerifyEmailOtp(new VerifyEmailOtpDTO { Email = "a@test.com", Otp = "" });

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task ForgotPassword_WithExistingEmail_ReturnsOkAndSendsOtp()
    {
        using var context = CreateContext();
        context.Users.Add(new User { FullName = "A", Email = "a@test.com", PasswordHash = "hash", RoleId = 3 });
        await context.SaveChangesAsync();
        var controller = CreateController(context);

        var result = await controller.ForgotPassword(new ForgotPasswordDTO { Email = "a@test.com" });

        Assert.IsType<OkObjectResult>(result);
        var user = await context.Users.FirstAsync(x => x.Email == "a@test.com");
        Assert.False(string.IsNullOrWhiteSpace(user.EmailOtpHash));
        Assert.NotNull(user.EmailOtpExpiredAt);
        A.CallTo(() => _notificationService.SendEmailAsync("a@test.com", A<string>._, A<string>._))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task ForgotPassword_WithNonexistentEmail_ReturnsOkWithoutRevealing()
    {
        using var context = CreateContext();
        var controller = CreateController(context);

        var result = await controller.ForgotPassword(new ForgotPasswordDTO { Email = "none@test.com" });

        Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public async Task ForgotPassword_WithMissingEmail_ReturnsBadRequest()
    {
        using var context = CreateContext();
        var controller = CreateController(context);
        controller.ModelState.AddModelError("Email", "The Email field is required.");

        var result = await controller.ForgotPassword(new ForgotPasswordDTO { Email = "" });

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task ForgotPassword_WithInvalidEmailFormat_ReturnsBadRequest()
    {
        using var context = CreateContext();
        var controller = CreateController(context);
        controller.ModelState.AddModelError("Email", "The Email field is not a valid e-mail address.");

        var result = await controller.ForgotPassword(new ForgotPasswordDTO { Email = "invalid-email" });

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task ResetPassword_WithValidOtp_ReturnsOkAndChangesPassword()
    {
        using var context = CreateContext();
        var otp = "654321";
        context.Users.Add(new User
        {
            FullName = "A",
            Email = "a@test.com",
            PasswordHash = PasswordHelper.HashPassword("oldpass"),
            EmailOtpHash = OtpHelper.HashOtpSha256(otp),
            EmailOtpExpiredAt = DateTime.UtcNow.AddMinutes(5),
            RoleId = 3
        });
        await context.SaveChangesAsync();
        var controller = CreateController(context);

        var result = await controller.ResetPassword(new ResetPasswordDTO { Email = "a@test.com", Otp = otp, NewPassword = "newpass" });

        Assert.IsType<OkObjectResult>(result);
        var user = await context.Users.FirstAsync(x => x.Email == "a@test.com");
        Assert.True(PasswordHelper.VerifyPassword("newpass", user.PasswordHash));
        Assert.Null(user.EmailOtpHash);
    }

    [Fact]
    public async Task ResetPassword_WithExpiredOtp_ReturnsBadRequest()
    {
        using var context = CreateContext();
        context.Users.Add(new User
        {
            FullName = "A",
            Email = "a@test.com",
            PasswordHash = "hash",
            EmailOtpHash = OtpHelper.HashOtpSha256("111111"),
            EmailOtpExpiredAt = DateTime.UtcNow.AddMinutes(-1),
            RoleId = 3
        });
        await context.SaveChangesAsync();
        var controller = CreateController(context);

        var result = await controller.ResetPassword(new ResetPasswordDTO { Email = "a@test.com", Otp = "111111", NewPassword = "newpass" });

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task ResetPassword_WithIncorrectOtp_ReturnsBadRequest()
    {
        using var context = CreateContext();
        context.Users.Add(new User
        {
            FullName = "A",
            Email = "a@test.com",
            PasswordHash = "hash",
            EmailOtpHash = OtpHelper.HashOtpSha256("111111"),
            EmailOtpExpiredAt = DateTime.UtcNow.AddMinutes(10),
            RoleId = 3
        });
        await context.SaveChangesAsync();
        var controller = CreateController(context);

        var result = await controller.ResetPassword(new ResetPasswordDTO { Email = "a@test.com", Otp = "999999", NewPassword = "newpass" });

        Assert.IsType<BadRequestObjectResult>(result);
    }

    // Change Password Tests (2 tests)
    [Fact]
    public async Task ChangePassword_WithCorrectCurrentPassword_ReturnsOkAndChangesPassword()
    {
        using var context = CreateContext();
        context.Users.Add(new User { FullName = "A", Email = "a@test.com", PasswordHash = PasswordHelper.HashPassword("oldpass"), RoleId = 3 });
        await context.SaveChangesAsync();
        var controller = CreateController(context);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(
                    new ClaimsIdentity(new[] { new Claim(ClaimTypes.Email, "a@test.com") }, "test"))
            }
        };

        var result = await controller.ChangePassword(new ChangePasswordDTO { CurrentPassword = "oldpass", NewPassword = "newpass" });

        Assert.IsType<OkObjectResult>(result);
        var user = await context.Users.FirstAsync(x => x.Email == "a@test.com");
        Assert.True(PasswordHelper.VerifyPassword("newpass", user.PasswordHash));
    }

    [Fact]
    public async Task ChangePassword_WithIncorrectCurrentPassword_ReturnsBadRequest()
    {
        using var context = CreateContext();
        context.Users.Add(new User { FullName = "A", Email = "a@test.com", PasswordHash = PasswordHelper.HashPassword("oldpass"), RoleId = 3 });
        await context.SaveChangesAsync();
        var controller = CreateController(context);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(
                    new ClaimsIdentity(new[] { new Claim(ClaimTypes.Email, "a@test.com") }, "test"))
            }
        };

        var result = await controller.ChangePassword(new ChangePasswordDTO { CurrentPassword = "wrong", NewPassword = "newpass" });

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task ChangePassword_WithoutAuthorization_ReturnsUnauthorized()
    {
        using var context = CreateContext();
        var controller = CreateController(context);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity())
            }
        };

        var result = await controller.ChangePassword(new ChangePasswordDTO { CurrentPassword = "old", NewPassword = "newpass" });

        Assert.IsType<UnauthorizedObjectResult>(result);
    }

    [Fact]
    public async Task ChangePassword_WithShortNewPassword_ReturnsBadRequest()
    {
        using var context = CreateContext();
        var controller = CreateController(context);
        controller.ModelState.AddModelError("NewPassword", "The field NewPassword must be a string with a minimum length of 6.");

        var result = await controller.ChangePassword(new ChangePasswordDTO { CurrentPassword = "old", NewPassword = "123" });

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task ChangePassword_WithMissingCurrentPassword_ReturnsBadRequest()
    {
        using var context = CreateContext();
        var controller = CreateController(context);
        controller.ModelState.AddModelError("CurrentPassword", "The CurrentPassword field is required.");

        var result = await controller.ChangePassword(new ChangePasswordDTO { CurrentPassword = "", NewPassword = "newpass" });

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task ChangePassword_WithNonexistentUser_ReturnsNotFound()
    {
        using var context = CreateContext();
        var controller = CreateController(context);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(
                    new ClaimsIdentity(new[] { new Claim(ClaimTypes.Email, "none@test.com") }, "test"))
            }
        };

        var result = await controller.ChangePassword(new ChangePasswordDTO { CurrentPassword = "old", NewPassword = "newpass" });

        Assert.IsType<NotFoundObjectResult>(result);
    }

    // Profile Tests (3 tests)
    [Fact]
    public async Task UpdateProfile_WithValidId_ReturnsOkAndUpdatesProfile()
    {
        using var context = CreateContext();
        A.CallTo(() => _userRepository.UpdateProfileAsync(1, A<UpdateProfileDTO>._)).Returns(true);
        var controller = CreateController(context);

        var result = await controller.UpdateProfile(1, new UpdateProfileDTO { FullName = "A" });

        Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public async Task UpdateProfile_WithInvalidId_ReturnsBadRequest()
    {
        using var context = CreateContext();
        var controller = CreateController(context);

        var result = await controller.UpdateProfile(0, new UpdateProfileDTO { FullName = "A" });

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task UpdateProfile_WithNonexistentId_ReturnsNotFound()
    {
        using var context = CreateContext();
        A.CallTo(() => _userRepository.UpdateProfileAsync(99, A<UpdateProfileDTO>._)).Returns(false);
        var controller = CreateController(context);

        var result = await controller.UpdateProfile(99, new UpdateProfileDTO { FullName = "A" });

        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task GetProfile_WithValidId_ReturnsOkWithProfile()
    {
        using var context = CreateContext();
        var user = new User { UserId = 1, FullName = "A", Email = "a@test.com", PhoneNumber = "123", PasswordHash = "hash", RoleId = 3 };
        var profile = new ProfileDTO { UserId = 1, FullName = "A", Email = "a@test.com", PhoneNumber = "123" };
        A.CallTo(() => _userRepository.GetByIdAsync(1, A<System.Linq.Expressions.Expression<Func<User, object>>[]>.Ignored))
            .Returns(user);
        A.CallTo(() => _mapper.Map<ProfileDTO>(user)).Returns(profile);
        var controller = CreateController(context);

        var result = await controller.Profile(1);

        var ok = Assert.IsType<OkObjectResult>(result);
        var payload = Assert.IsType<ProfileDTO>(ok.Value);
        Assert.Equal(profile.UserId, payload.UserId);
        Assert.Equal(profile.Email, payload.Email);
    }

    [Fact]
    public async Task GetProfile_WithInvalidId_ReturnsBadRequest()
    {
        using var context = CreateContext();
        var controller = CreateController(context);

        var result = await controller.Profile(0);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task GetProfile_WithNonexistentId_ReturnsNotFound()
    {
        using var context = CreateContext();
        A.CallTo(() => _userRepository.GetByIdAsync(99, A<System.Linq.Expressions.Expression<Func<User, object>>[]>.Ignored))
            .Returns((User)null!);
        var controller = CreateController(context);

        var result = await controller.Profile(99);

        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task Logout_WhenAuthorized_ReturnsOk()
    {
        using var context = CreateContext();
        var controller = CreateController(context);

        var result = await controller.Logout();

        var ok = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<BaseCommentResponse>(ok.Value);
        Assert.Equal(200, response.Statuscodes);
    }

    [Fact]
    public async Task Logout_WhenNotAuthorized_ReturnsOkRegardless()
    {
        using var context = CreateContext();
        var controller = CreateController(context);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity()) }
        };

        var result = await controller.Logout();

        var ok = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<BaseCommentResponse>(ok.Value);
        Assert.Equal(200, response.Statuscodes);
        Assert.Equal("Đăng xuất thành công", response.Message);
    }
}
