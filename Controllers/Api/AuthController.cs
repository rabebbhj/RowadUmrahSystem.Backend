using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RowadUmrahSystem.Web.Data;
using RowadUmrahSystem.Web.Models;
using RowadUmrahSystem.Web.Services;
using RowadUmrahSystem.Web.ViewModels.Api;

namespace RowadUmrahSystem.Web.Controllers.Api
{
    [ApiController]
    [Route("api/auth")]
    public class AuthController : ControllerBase
    {
        private const string MainAdminEmail = "admin@rowad.local";
        private const string EmailVerificationPurpose = "traveler-email-verification";
        private const string PasswordResetPurpose = "traveler-password-reset";
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly ApplicationDbContext _context;
        private readonly IAuthEmailSender _emailSender;

        public AuthController(
            SignInManager<ApplicationUser> signInManager,
            UserManager<ApplicationUser> userManager,
            ApplicationDbContext context,
            IAuthEmailSender emailSender)
        {
            _signInManager = signInManager;
            _userManager = userManager;
            _context = context;
            _emailSender = emailSender;
        }

        [HttpGet("me")]
        public async Task<ActionResult<AuthUserDto>> Me()
        {
            if (User?.Identity?.IsAuthenticated != true)
            {
                return Ok(new AuthUserDto(false, null, null, Array.Empty<string>()));
            }

            var user = await _userManager.GetUserAsync(User);
            var roles = user == null
                ? Array.Empty<string>()
                : (await _userManager.GetRolesAsync(user)).ToArray();

            return Ok(new AuthUserDto(
                true,
                user?.Email,
                user?.FullName,
                roles,
                user == null ? null : await GetAuthPermissionsAsync(user, roles)));
        }

        [HttpPost("login")]
        public async Task<ActionResult<LoginResponseDto>> Login([FromBody] LoginRequestDto request)
        {
            var user = await _userManager.FindByEmailAsync(request.Email)
                ?? await _userManager.FindByNameAsync(request.Email);

            if (user == null)
            {
                return Unauthorized(new LoginResponseDto(false, "Invalid email or password.", null));
            }

            if (!await _userManager.CheckPasswordAsync(user, request.Password))
            {
                return Unauthorized(new LoginResponseDto(false, "Invalid email or password.", null));
            }

            if (!user.EmailConfirmed)
            {
                await SendEmailVerificationCodeAsync(user, HttpContext.RequestAborted);
                return Unauthorized(new LoginResponseDto(false, "EMAIL_NOT_CONFIRMED", null));
            }

            var result = await _signInManager.PasswordSignInAsync(
                user.UserName ?? request.Email,
                request.Password,
                request.RememberMe,
                lockoutOnFailure: false);

            if (!result.Succeeded)
            {
                return Unauthorized(new LoginResponseDto(false, "Invalid email or password.", null));
            }

            if (!user.IsActive)
            {
                await _signInManager.SignOutAsync();
                return Unauthorized(new LoginResponseDto(false, "Account is inactive.", null));
            }

            var roles = (await _userManager.GetRolesAsync(user)).ToArray();
            return Ok(new LoginResponseDto(
                true,
                "Login successful.",
                new AuthUserDto(true, user.Email, user.FullName, roles, await GetAuthPermissionsAsync(user, roles))));
        }

        [HttpPost("register-traveler")]
        public async Task<ActionResult<LoginResponseDto>> RegisterTraveler([FromBody] RegisterTravelerRequestDto request)
        {
            var email = request.Email?.Trim() ?? string.Empty;
            var fullName = request.FullName?.Trim() ?? string.Empty;
            var phone = request.Phone?.Trim() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(fullName) ||
                string.IsNullOrWhiteSpace(email) ||
                string.IsNullOrWhiteSpace(phone) ||
                string.IsNullOrWhiteSpace(request.Password))
            {
                return BadRequest(new LoginResponseDto(false, "جميع الحقول مطلوبة.", null));
            }

            var existingUser = await _userManager.FindByEmailAsync(email);
            if (existingUser != null)
            {
                if (!existingUser.EmailConfirmed)
                {
                    await SendEmailVerificationCodeAsync(existingUser, HttpContext.RequestAborted);
                    return Ok(new LoginResponseDto(false, "EMAIL_NOT_CONFIRMED", null));
                }

                return Conflict(new LoginResponseDto(false, "يوجد حساب بهذا البريد الإلكتروني بالفعل.", null));
            }

            var user = new ApplicationUser
            {
                UserName = email,
                Email = email,
                PhoneNumber = phone,
                FullName = fullName,
                EmailConfirmed = false,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };

            var result = await _userManager.CreateAsync(user, request.Password);
            if (!result.Succeeded)
            {
                return BadRequest(new LoginResponseDto(false, BuildIdentityErrorMessage(result), null));
            }

            await _userManager.AddToRoleAsync(user, "Traveler");
            await SendEmailVerificationCodeAsync(user, HttpContext.RequestAborted);

            return Ok(new LoginResponseDto(false, "VERIFICATION_CODE_SENT", null));
        }

        [HttpPost("verify-email")]
        public async Task<ActionResult<LoginResponseDto>> VerifyEmail([FromBody] VerifyEmailRequestDto request)
        {
            var user = await _userManager.FindByEmailAsync(request.Email?.Trim() ?? string.Empty);
            if (user == null)
            {
                return BadRequest(new LoginResponseDto(false, "رمز التحقق غير صحيح.", null));
            }

            var isValid = await _userManager.VerifyUserTokenAsync(
                user,
                TokenOptions.DefaultEmailProvider,
                EmailVerificationPurpose,
                NormalizeCode(request.Code));

            if (!isValid)
            {
                return BadRequest(new LoginResponseDto(false, "رمز التحقق غير صحيح أو منتهي الصلاحية.", null));
            }

            user.EmailConfirmed = true;
            user.IsActive = true;
            await _userManager.UpdateAsync(user);
            await _signInManager.SignInAsync(user, request.RememberMe);

            var roles = (await _userManager.GetRolesAsync(user)).ToArray();
            return Ok(new LoginResponseDto(
                true,
                "تم تفعيل الحساب بنجاح.",
                new AuthUserDto(true, user.Email, user.FullName, roles, await GetAuthPermissionsAsync(user, roles))));
        }

        [HttpPost("resend-email-code")]
        public async Task<ActionResult<LoginResponseDto>> ResendEmailCode([FromBody] ResendEmailCodeRequestDto request)
        {
            var user = await _userManager.FindByEmailAsync(request.Email?.Trim() ?? string.Empty);
            if (user != null && !user.EmailConfirmed)
            {
                await SendEmailVerificationCodeAsync(user, HttpContext.RequestAborted);
            }

            return Ok(new LoginResponseDto(false, "إذا كان البريد صحيحاً، تم إرسال رمز التحقق.", null));
        }

        [HttpPost("forgot-password")]
        public async Task<ActionResult<LoginResponseDto>> ForgotPassword([FromBody] ForgotPasswordRequestDto request)
        {
            var user = await _userManager.FindByEmailAsync(request.Email?.Trim() ?? string.Empty);
            if (user != null && user.IsActive)
            {
                var code = await _userManager.GenerateUserTokenAsync(
                    user,
                    TokenOptions.DefaultEmailProvider,
                    PasswordResetPurpose);

                await _emailSender.SendOtpAsync(
                    user.Email ?? request.Email ?? string.Empty,
                    "رمز إعادة تعيين كلمة المرور",
                    code,
                    HttpContext.RequestAborted);
            }

            return Ok(new LoginResponseDto(false, "إذا كان البريد صحيحاً، تم إرسال رمز إعادة التعيين.", null));
        }

        [HttpPost("reset-password")]
        public async Task<ActionResult<LoginResponseDto>> ResetPassword([FromBody] ResetPasswordRequestDto request)
        {
            var user = await _userManager.FindByEmailAsync(request.Email?.Trim() ?? string.Empty);
            if (user == null)
            {
                return BadRequest(new LoginResponseDto(false, "رمز إعادة التعيين غير صحيح.", null));
            }

            var isValid = await _userManager.VerifyUserTokenAsync(
                user,
                TokenOptions.DefaultEmailProvider,
                PasswordResetPurpose,
                NormalizeCode(request.Code));

            if (!isValid)
            {
                return BadRequest(new LoginResponseDto(false, "رمز إعادة التعيين غير صحيح أو منتهي الصلاحية.", null));
            }

            var resetToken = await _userManager.GeneratePasswordResetTokenAsync(user);
            var result = await _userManager.ResetPasswordAsync(user, resetToken, request.Password);
            if (!result.Succeeded)
            {
                return BadRequest(new LoginResponseDto(false, BuildIdentityErrorMessage(result), null));
            }

            return Ok(new LoginResponseDto(true, "تم تغيير كلمة المرور بنجاح.", null));
        }

        [HttpPost("logout")]
        public async Task<IActionResult> Logout()
        {
            await HttpContext.SignOutAsync(IdentityConstants.ApplicationScheme);
            return Ok();
        }

        private async Task<AuthPermissionsDto> GetAuthPermissionsAsync(ApplicationUser user, IReadOnlyCollection<string> roles)
        {
            if (IsMainAdmin(user))
            {
                return CreateFullPermissions();
            }

            var permissions = await _context.UserPermissions
                .AsNoTracking()
                .FirstOrDefaultAsync(permission => permission.UserId == user.Id);

            if (permissions == null)
            {
                return CreateEmptyPermissions();
            }

            var canViewAccounting =
                permissions.CanViewAccounting ||
                permissions.CanManageAccounting ||
                permissions.CanManageChartOfAccounts ||
                permissions.CanManageJournalEntries ||
                permissions.CanManageInvoices ||
                permissions.CanManageReceiptVouchers ||
                permissions.CanManagePaymentVouchers ||
                permissions.CanManageExpenses ||
                permissions.CanManageBanks ||
                permissions.CanViewFinancialReports;

            return new AuthPermissionsDto(
                permissions.CanAccessDashboard,
                permissions.CanViewNotifications,
                permissions.CanManageUsers,
                permissions.CanViewTravelers,
                permissions.CanCreateTravelers,
                permissions.CanEditTravelers,
                permissions.CanArchiveTravelers,
                permissions.CanRestoreTravelers,
                permissions.CanViewTrips,
                permissions.CanCreateTrips,
                permissions.CanArchiveTrips,
                permissions.CanRestoreTrips,
                permissions.CanViewDocuments,
                permissions.CanUploadDocuments,
                permissions.CanArchiveDocuments,
                permissions.CanRestoreDocuments,
                permissions.CanViewBlocks,
                permissions.CanBlockTravelers,
                permissions.CanUnblockTravelers,
                permissions.CanViewReports,
                permissions.CanExportReports,
                permissions.CanViewAuditLogs,
                canViewAccounting,
                permissions.CanManageAccounting,
                permissions.CanManageChartOfAccounts || permissions.CanManageAccounting,
                permissions.CanManageJournalEntries || permissions.CanManageAccounting,
                permissions.CanManageInvoices || permissions.CanManageAccounting,
                permissions.CanManageReceiptVouchers || permissions.CanManageAccounting,
                permissions.CanManagePaymentVouchers || permissions.CanManageAccounting,
                permissions.CanManageExpenses || permissions.CanManageAccounting,
                permissions.CanManageBanks || permissions.CanManageAccounting,
                permissions.CanViewFinancialReports || permissions.CanManageAccounting);
        }

        private static AuthPermissionsDto CreateFullPermissions()
        {
            return new AuthPermissionsDto(
                true, true, true, true, true, true, true, true,
                true, true, true, true, true, true, true, true,
                true, true, true, true, true, true, true, true,
                true, true, true, true, true, true, true, true);
        }

        private static AuthPermissionsDto CreateEmptyPermissions()
        {
            return new AuthPermissionsDto(
                false, false, false, false, false, false, false, false,
                false, false, false, false, false, false, false, false,
                false, false, false, false, false, false, false, false,
                false, false, false, false, false, false, false, false);
        }

        private static bool IsMainAdmin(ApplicationUser user)
        {
            return string.Equals(user.Email, MainAdminEmail, StringComparison.OrdinalIgnoreCase);
        }

        private async Task SendEmailVerificationCodeAsync(ApplicationUser user, CancellationToken cancellationToken)
        {
            var code = await _userManager.GenerateUserTokenAsync(
                user,
                TokenOptions.DefaultEmailProvider,
                EmailVerificationPurpose);

            await _emailSender.SendOtpAsync(
                user.Email ?? user.UserName ?? string.Empty,
                "رمز تفعيل حساب رواد العمرة",
                code,
                cancellationToken);
        }

        private static string NormalizeCode(string code)
        {
            return new string((code ?? string.Empty).Where(char.IsDigit).ToArray());
        }

        private static string BuildIdentityErrorMessage(IdentityResult result)
        {
            return string.Join(" ", result.Errors.Select(error => error.Description));
        }
    }
}
