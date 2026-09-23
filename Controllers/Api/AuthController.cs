using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RowadUmrahSystem.Web.Data;
using RowadUmrahSystem.Web.Models;
using RowadUmrahSystem.Web.ViewModels.Api;

namespace RowadUmrahSystem.Web.Controllers.Api
{
    [ApiController]
    [Route("api/auth")]
    public class AuthController : ControllerBase
    {
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly ApplicationDbContext _context;

        public AuthController(
            SignInManager<ApplicationUser> signInManager,
            UserManager<ApplicationUser> userManager,
            ApplicationDbContext context)
        {
            _signInManager = signInManager;
            _userManager = userManager;
            _context = context;
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

        [HttpPost("logout")]
        public async Task<IActionResult> Logout()
        {
            await HttpContext.SignOutAsync(IdentityConstants.ApplicationScheme);
            return Ok();
        }

        private async Task<AuthPermissionsDto> GetAuthPermissionsAsync(ApplicationUser user, IReadOnlyCollection<string> roles)
        {
            if (roles.Contains("Admin"))
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

            var canAccessDashboard =
                permissions.CanViewTravelers ||
                permissions.CanManageTravelers ||
                permissions.CanViewTrips ||
                permissions.CanManageTrips ||
                permissions.CanViewDocuments ||
                permissions.CanViewBlocks ||
                permissions.CanManageBlocks ||
                permissions.CanViewAuditLogs ||
                permissions.CanManageUsers ||
                permissions.CanViewReports ||
                canViewAccounting;

            return new AuthPermissionsDto(
                canAccessDashboard,
                permissions.CanManageUsers,
                permissions.CanViewTravelers || permissions.CanManageTravelers,
                permissions.CanCreateTravelers || permissions.CanManageTravelers,
                permissions.CanEditTravelers || permissions.CanManageTravelers,
                permissions.CanArchiveTravelers || permissions.CanManageTravelers,
                permissions.CanRestoreTravelers || permissions.CanManageTravelers,
                permissions.CanViewTrips || permissions.CanManageTrips,
                permissions.CanCreateTrips || permissions.CanManageTrips,
                permissions.CanArchiveTrips || permissions.CanManageTrips,
                permissions.CanRestoreTrips || permissions.CanManageTrips,
                permissions.CanViewDocuments || permissions.CanManageTravelers,
                permissions.CanUploadDocuments || permissions.CanManageTravelers,
                permissions.CanArchiveDocuments || permissions.CanManageTravelers,
                permissions.CanRestoreDocuments || permissions.CanManageTravelers,
                permissions.CanViewBlocks || permissions.CanManageBlocks,
                permissions.CanBlockTravelers || permissions.CanManageBlocks,
                permissions.CanUnblockTravelers || permissions.CanManageBlocks,
                permissions.CanViewReports,
                permissions.CanExportReports || permissions.CanViewReports,
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
                true, true, true, true, true, true, true, true, true, true,
                true, true, true, true, true, true, true, true, true, true,
                true, true, true, true, true, true, true, true, true, true,
                true);
        }

        private static AuthPermissionsDto CreateEmptyPermissions()
        {
            return new AuthPermissionsDto(
                false, false, false, false, false, false, false, false, false, false,
                false, false, false, false, false, false, false, false, false, false,
                false, false, false, false, false, false, false, false, false, false,
                false);
        }
    }
}
