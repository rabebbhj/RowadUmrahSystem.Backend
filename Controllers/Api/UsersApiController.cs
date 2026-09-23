using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RowadUmrahSystem.Web.Data;
using RowadUmrahSystem.Web.Models;
using RowadUmrahSystem.Web.ViewModels.Api;

namespace RowadUmrahSystem.Web.Controllers.Api
{
    [ApiController]
    [Authorize(Roles = "Admin")]
    [Route("api/users")]
    public class UsersApiController : ControllerBase
    {
        private const string MainAdminEmail = "admin@rowad.local";
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly ApplicationDbContext _context;

        public UsersApiController(
            UserManager<ApplicationUser> userManager,
            ApplicationDbContext context)
        {
            _userManager = userManager;
            _context = context;
        }

        [HttpGet]
        public async Task<ActionResult<IReadOnlyList<UserListItemDto>>> GetAll()
        {
            var users = await _userManager.Users
                .AsNoTracking()
                .OrderByDescending(user => user.CreatedAt)
                .ToListAsync();

            var items = new List<UserListItemDto>(users.Count);

            foreach (var user in users)
            {
                var roles = await _userManager.GetRolesAsync(user);
                if (!IsCompanyUser(roles))
                {
                    continue;
                }

                items.Add(new UserListItemDto(
                    user.Id,
                    user.FullName,
                    user.UserName ?? user.Email ?? string.Empty,
                    user.Email ?? string.Empty,
                    user.PhoneNumber,
                    user.IsActive,
                    user.CreatedAt,
                    user.LastLoginAt,
                    IsMainAdmin(user),
                    roles.ToArray(),
                    await _context.UserPermissions.AnyAsync(permission => permission.UserId == user.Id)));
            }

            return Ok(items);
        }

        [HttpPost]
        public async Task<ActionResult<UserListItemDto>> Create([FromBody] UserCreateRequestDto request)
        {
            var fullName = request.FullName?.Trim() ?? string.Empty;
            var email = request.Email?.Trim() ?? string.Empty;
            var phoneNumber = request.PhoneNumber?.Trim();

            if (string.IsNullOrWhiteSpace(fullName) ||
                string.IsNullOrWhiteSpace(email) ||
                string.IsNullOrWhiteSpace(request.Password))
            {
                return BadRequest("Please fill all required fields.");
            }

            var existingUser = await _userManager.FindByEmailAsync(email);
            if (existingUser != null)
            {
                return Conflict("A user with this email already exists.");
            }

            var user = new ApplicationUser
            {
                FullName = fullName,
                UserName = email,
                Email = email,
                PhoneNumber = phoneNumber,
                EmailConfirmed = true,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };

            var result = await _userManager.CreateAsync(user, request.Password);
            if (!result.Succeeded)
            {
                return BadRequest(string.Join(" ", result.Errors.Select(error => error.Description)));
            }

            await _userManager.AddToRoleAsync(user, "Employee");

            _context.UserPermissions.Add(CreateDefaultPermissions(user.Id));
            await _context.SaveChangesAsync();

            return Ok(await MapUserAsync(user));
        }

        [HttpPost("{id}/toggle-active")]
        public async Task<ActionResult<UserListItemDto>> ToggleActive(string id)
        {
            var user = await _userManager.FindByIdAsync(id);
            if (user == null)
            {
                return NotFound();
            }

            if (!await IsCompanyUserAsync(user))
            {
                return NotFound();
            }

            if (IsMainAdmin(user))
            {
                return BadRequest("The main admin account cannot be disabled.");
            }

            user.IsActive = !user.IsActive;
            await _userManager.UpdateAsync(user);

            return Ok(await MapUserAsync(user));
        }

        [HttpGet("{id}/permissions")]
        public async Task<ActionResult<UserPermissionsDto>> GetPermissions(string id)
        {
            var user = await _userManager.FindByIdAsync(id);
            if (user == null)
            {
                return NotFound();
            }

            if (!await IsCompanyUserAsync(user))
            {
                return NotFound();
            }

            var permissions = await EnsurePermissionsAsync(id);
            return Ok(MapPermissions(user, permissions));
        }

        [HttpPut("{id}/permissions")]
        public async Task<ActionResult<UserPermissionsDto>> UpdatePermissions(string id, [FromBody] UserPermissionsDto request)
        {
            var user = await _userManager.FindByIdAsync(id);
            if (user == null)
            {
                return NotFound();
            }

            if (!await IsCompanyUserAsync(user))
            {
                return NotFound();
            }

            if (IsMainAdmin(user))
            {
                return BadRequest("The main admin permissions cannot be changed.");
            }

            var permissions = await EnsurePermissionsAsync(id);

            permissions.CanManageUsers = request.CanManageUsers;
            permissions.CanViewTravelers = request.CanViewTravelers;
            permissions.CanCreateTravelers = request.CanCreateTravelers;
            permissions.CanEditTravelers = request.CanEditTravelers;
            permissions.CanArchiveTravelers = request.CanArchiveTravelers;
            permissions.CanRestoreTravelers = request.CanRestoreTravelers;
            permissions.CanViewTrips = request.CanViewTrips;
            permissions.CanCreateTrips = request.CanCreateTrips;
            permissions.CanArchiveTrips = request.CanArchiveTrips;
            permissions.CanRestoreTrips = request.CanRestoreTrips;
            permissions.CanViewDocuments = request.CanViewDocuments;
            permissions.CanUploadDocuments = request.CanUploadDocuments;
            permissions.CanArchiveDocuments = request.CanArchiveDocuments;
            permissions.CanRestoreDocuments = request.CanRestoreDocuments;
            permissions.CanViewBlocks = request.CanViewBlocks;
            permissions.CanBlockTravelers = request.CanBlockTravelers;
            permissions.CanUnblockTravelers = request.CanUnblockTravelers;
            permissions.CanViewReports = request.CanViewReports;
            permissions.CanExportReports = request.CanExportReports;
            permissions.CanViewAuditLogs = request.CanViewAuditLogs;
            permissions.CanViewAccounting = request.CanViewAccounting;
            permissions.CanManageAccounting = request.CanManageAccounting;
            permissions.CanManageChartOfAccounts = request.CanManageChartOfAccounts;
            permissions.CanManageJournalEntries = request.CanManageJournalEntries;
            permissions.CanManageInvoices = request.CanManageInvoices;
            permissions.CanManageReceiptVouchers = request.CanManageReceiptVouchers;
            permissions.CanManagePaymentVouchers = request.CanManagePaymentVouchers;
            permissions.CanManageExpenses = request.CanManageExpenses;
            permissions.CanManageBanks = request.CanManageBanks;
            permissions.CanViewFinancialReports = request.CanViewFinancialReports;

            permissions.CanManageTravelers =
                permissions.CanViewTravelers ||
                permissions.CanCreateTravelers ||
                permissions.CanEditTravelers ||
                permissions.CanArchiveTravelers ||
                permissions.CanRestoreTravelers;

            permissions.CanManageTrips =
                permissions.CanViewTrips ||
                permissions.CanCreateTrips;

            permissions.CanManageBlocks =
                permissions.CanViewBlocks ||
                permissions.CanBlockTravelers ||
                permissions.CanUnblockTravelers;

            await _context.SaveChangesAsync();

            return Ok(MapPermissions(user, permissions));
        }

        private async Task<UserPermission> EnsurePermissionsAsync(string userId)
        {
            var permissions = await _context.UserPermissions
                .FirstOrDefaultAsync(permission => permission.UserId == userId);

            if (permissions != null)
            {
                return permissions;
            }

            permissions = CreateDefaultPermissions(userId);
            _context.UserPermissions.Add(permissions);
            await _context.SaveChangesAsync();
            return permissions;
        }

        private async Task<UserListItemDto> MapUserAsync(ApplicationUser user)
        {
            var roles = await _userManager.GetRolesAsync(user);

            return new UserListItemDto(
                user.Id,
                user.FullName,
                user.UserName ?? user.Email ?? string.Empty,
                user.Email ?? string.Empty,
                user.PhoneNumber,
                user.IsActive,
                user.CreatedAt,
                user.LastLoginAt,
                IsMainAdmin(user),
                roles.ToArray(),
                await _context.UserPermissions.AnyAsync(permission => permission.UserId == user.Id));
        }

        private static UserPermissionsDto MapPermissions(ApplicationUser user, UserPermission permissions)
        {
            return new UserPermissionsDto
            {
                Id = permissions.Id,
                UserId = user.Id,
                UserFullName = user.FullName,
                UserEmail = user.Email ?? string.Empty,
                CanManageUsers = permissions.CanManageUsers,
                CanViewTravelers = permissions.CanViewTravelers,
                CanCreateTravelers = permissions.CanCreateTravelers,
                CanEditTravelers = permissions.CanEditTravelers,
                CanArchiveTravelers = permissions.CanArchiveTravelers,
                CanRestoreTravelers = permissions.CanRestoreTravelers,
                CanViewTrips = permissions.CanViewTrips,
                CanCreateTrips = permissions.CanCreateTrips,
                CanArchiveTrips = permissions.CanArchiveTrips,
                CanRestoreTrips = permissions.CanRestoreTrips,
                CanViewDocuments = permissions.CanViewDocuments,
                CanUploadDocuments = permissions.CanUploadDocuments,
                CanArchiveDocuments = permissions.CanArchiveDocuments,
                CanRestoreDocuments = permissions.CanRestoreDocuments,
                CanViewBlocks = permissions.CanViewBlocks,
                CanBlockTravelers = permissions.CanBlockTravelers,
                CanUnblockTravelers = permissions.CanUnblockTravelers,
                CanViewReports = permissions.CanViewReports,
                CanExportReports = permissions.CanExportReports,
                CanViewAuditLogs = permissions.CanViewAuditLogs,
                CanViewAccounting = permissions.CanViewAccounting,
                CanManageAccounting = permissions.CanManageAccounting,
                CanManageChartOfAccounts = permissions.CanManageChartOfAccounts,
                CanManageJournalEntries = permissions.CanManageJournalEntries,
                CanManageInvoices = permissions.CanManageInvoices,
                CanManageReceiptVouchers = permissions.CanManageReceiptVouchers,
                CanManagePaymentVouchers = permissions.CanManagePaymentVouchers,
                CanManageExpenses = permissions.CanManageExpenses,
                CanManageBanks = permissions.CanManageBanks,
                CanViewFinancialReports = permissions.CanViewFinancialReports
            };
        }

        private static UserPermission CreateDefaultPermissions(string userId)
        {
            return new UserPermission
            {
                UserId = userId,
                CanManageTravelers = true,
                CanManageTrips = true,
                CanManageBlocks = false,
                CanViewReports = false,
                CanManageUsers = false,
                CanViewTravelers = true,
                CanCreateTravelers = true,
                CanEditTravelers = true,
                CanArchiveTravelers = false,
                CanRestoreTravelers = false,
                CanViewTrips = true,
                CanCreateTrips = true,
                CanArchiveTrips = false,
                CanRestoreTrips = false,
                CanViewDocuments = true,
                CanUploadDocuments = true,
                CanArchiveDocuments = false,
                CanRestoreDocuments = false,
                CanViewBlocks = false,
                CanBlockTravelers = false,
                CanUnblockTravelers = false,
                CanExportReports = false,
                CanViewAuditLogs = false,
                CanViewAccounting = false,
                CanManageAccounting = false,
                CanManageChartOfAccounts = false,
                CanManageJournalEntries = false,
                CanManageInvoices = false,
                CanManageReceiptVouchers = false,
                CanManagePaymentVouchers = false,
                CanManageExpenses = false,
                CanManageBanks = false,
                CanViewFinancialReports = false
            };
        }

        private static bool IsMainAdmin(ApplicationUser user)
        {
            return string.Equals(user.Email, MainAdminEmail, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsCompanyUser(IEnumerable<string> roles)
        {
            return roles.Any(role =>
                string.Equals(role, "Admin", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(role, "Employee", StringComparison.OrdinalIgnoreCase));
        }

        private async Task<bool> IsCompanyUserAsync(ApplicationUser user)
        {
            return IsCompanyUser(await _userManager.GetRolesAsync(user));
        }
    }
}
