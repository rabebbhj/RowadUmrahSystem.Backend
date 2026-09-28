using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RowadUmrahSystem.Web.Data;
using RowadUmrahSystem.Web.Models;

namespace RowadUmrahSystem.Web.Controllers
{
    [Authorize(Roles = "Admin")]
    public class UsersController : Controller
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly ApplicationDbContext _context;

        private const string MainAdminEmail = "admin@rowad.local";

        public UsersController(
            UserManager<ApplicationUser> userManager,
            ApplicationDbContext context)
        {
            _userManager = userManager;
            _context = context;
        }

        public async Task<IActionResult> Index()
        {
            var users = await _userManager.Users
                .AsNoTracking()
                .OrderByDescending(u => u.CreatedAt)
                .ToListAsync();

            return View(users);
        }

        public IActionResult Create()
        {
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(
            string fullName,
            string email,
            string phoneNumber,
            string password)
        {
            fullName = fullName?.Trim() ?? string.Empty;
            email = email?.Trim() ?? string.Empty;
            phoneNumber = phoneNumber?.Trim() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(fullName) ||
                string.IsNullOrWhiteSpace(email) ||
                string.IsNullOrWhiteSpace(password))
            {
                ModelState.AddModelError("", "الرجاء تعبئة جميع الحقول المطلوبة.");
                return View();
            }

            var existingUser = await _userManager.FindByEmailAsync(email);

            if (existingUser != null)
            {
                ModelState.AddModelError("", "يوجد مستخدم مسجل بهذا الإيميل مسبقاً.");
                return View();
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

            var result = await _userManager.CreateAsync(user, password);

            if (result.Succeeded)
            {
                await _userManager.AddToRoleAsync(user, "Employee");

                var permissions = new UserPermission
                {
                    UserId = user.Id,

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

                    CanViewDocuments = true,
                    CanUploadDocuments = true,
                    CanArchiveDocuments = false,
                    CanRestoreDocuments = false,

                    CanViewBlocks = false,
                    CanBlockTravelers = false,
                    CanUnblockTravelers = false,

                    CanExportReports = false,
                    CanViewAuditLogs = false
                };

                _context.UserPermissions.Add(permissions);
                await _context.SaveChangesAsync();

                TempData["Success"] = "تم إنشاء المستخدم بنجاح.";

                return RedirectToAction(nameof(Index));
            }

            foreach (var error in result.Errors)
            {
                ModelState.AddModelError("", error.Description);
            }

            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ToggleActive(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
                return NotFound();

            var user = await _userManager.FindByIdAsync(id);

            if (user == null)
                return NotFound();

            if (IsMainAdmin(user))
            {
                TempData["Error"] = "لا يمكن تعطيل حساب المدير الأساسي.";
                return RedirectToAction(nameof(Index));
            }

            user.IsActive = !user.IsActive;

            await _userManager.UpdateAsync(user);

            TempData["Success"] = user.IsActive
                ? "تم تفعيل المستخدم بنجاح."
                : "تم تعطيل المستخدم بنجاح.";

            return RedirectToAction(nameof(Index));
        }

        public async Task<IActionResult> Permissions(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
                return NotFound();

            var user = await _userManager.FindByIdAsync(id);

            if (user == null)
                return NotFound();

            if (IsMainAdmin(user))
            {
                TempData["Error"] = "لا يمكن تعديل صلاحيات المدير الأساسي.";
                return RedirectToAction(nameof(Index));
            }

            var permissions = await _context.UserPermissions
                .FirstOrDefaultAsync(p => p.UserId == id);

            if (permissions == null)
            {
                permissions = new UserPermission
                {
                    UserId = id,

                    CanManageTravelers = false,
                    CanManageTrips = false,
                    CanManageBlocks = false,
                    CanViewReports = false,
                    CanManageUsers = false,

                    CanViewTravelers = false,
                    CanCreateTravelers = false,
                    CanEditTravelers = false,
                    CanArchiveTravelers = false,
                    CanRestoreTravelers = false,

                    CanViewTrips = false,
                    CanCreateTrips = false,

                    CanViewDocuments = false,
                    CanUploadDocuments = false,
                    CanArchiveDocuments = false,
                    CanRestoreDocuments = false,

                    CanViewBlocks = false,
                    CanBlockTravelers = false,
                    CanUnblockTravelers = false,

                    CanExportReports = false,
                    CanViewAuditLogs = false
                };

                _context.UserPermissions.Add(permissions);
                await _context.SaveChangesAsync();
            }

            ViewBag.UserFullName = user.FullName;
            ViewBag.UserEmail = user.Email;

            return View(permissions);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Permissions(UserPermission permissions)
        {
            if (permissions == null || string.IsNullOrWhiteSpace(permissions.UserId))
                return NotFound();

            var user = await _userManager.FindByIdAsync(permissions.UserId);

            if (user == null)
                return NotFound();

            if (IsMainAdmin(user))
            {
                TempData["Error"] = "لا يمكن تعديل صلاحيات المدير الأساسي.";
                return RedirectToAction(nameof(Index));
            }

            var existingPermissions = await _context.UserPermissions
                .FirstOrDefaultAsync(p => p.UserId == permissions.UserId);

            if (existingPermissions == null)
                return NotFound();

            existingPermissions.CanManageTravelers =
                permissions.CanCreateTravelers ||
                permissions.CanEditTravelers ||
                permissions.CanArchiveTravelers ||
                permissions.CanRestoreTravelers;

            existingPermissions.CanManageTrips =
                permissions.CanCreateTrips ||
                permissions.CanArchiveTrips ||
                permissions.CanRestoreTrips;

            existingPermissions.CanManageBlocks =
                permissions.CanBlockTravelers ||
                permissions.CanUnblockTravelers;

            existingPermissions.CanViewReports = permissions.CanViewReports;
            existingPermissions.CanManageUsers = permissions.CanManageUsers;

            existingPermissions.CanViewTravelers = permissions.CanViewTravelers;
            existingPermissions.CanCreateTravelers = permissions.CanCreateTravelers;
            existingPermissions.CanEditTravelers = permissions.CanEditTravelers;
            existingPermissions.CanArchiveTravelers = permissions.CanArchiveTravelers;
            existingPermissions.CanRestoreTravelers = permissions.CanRestoreTravelers;

            existingPermissions.CanViewTrips = permissions.CanViewTrips;
            existingPermissions.CanCreateTrips = permissions.CanCreateTrips;

            existingPermissions.CanViewDocuments = permissions.CanViewDocuments;
            existingPermissions.CanUploadDocuments = permissions.CanUploadDocuments;
            existingPermissions.CanArchiveDocuments = permissions.CanArchiveDocuments;
            existingPermissions.CanRestoreDocuments = permissions.CanRestoreDocuments;

            existingPermissions.CanViewBlocks = permissions.CanViewBlocks;
            existingPermissions.CanBlockTravelers = permissions.CanBlockTravelers;
            existingPermissions.CanUnblockTravelers = permissions.CanUnblockTravelers;

            existingPermissions.CanExportReports = permissions.CanExportReports;
            existingPermissions.CanViewAuditLogs = permissions.CanViewAuditLogs;

            await _context.SaveChangesAsync();

            TempData["Success"] = "تم تحديث صلاحيات المستخدم بنجاح.";

            return RedirectToAction(nameof(Index));
        }

        private static bool IsMainAdmin(ApplicationUser user)
        {
            return string.Equals(
                user.Email,
                MainAdminEmail,
                StringComparison.OrdinalIgnoreCase);
        }
    }
}
