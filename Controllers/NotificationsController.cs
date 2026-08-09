using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RowadUmrahSystem.Web.Data;
using RowadUmrahSystem.Web.Models;

namespace RowadUmrahSystem.Web.Controllers
{
    [Authorize]
    public class NotificationsController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;

        public NotificationsController(
            ApplicationDbContext context,
            UserManager<ApplicationUser> userManager)
        {
            _context = context;
            _userManager = userManager;
        }

        public async Task<IActionResult> Index(string? filter)
        {
            var user = await _userManager.GetUserAsync(User);

            if (user == null)
                return Unauthorized();

            var query = _context.UserNotifications
                .Where(n => n.UserId == user.Id && !n.IsDeleted)
                .Include(n => n.Notification)
                .AsQueryable();

            if (filter == "unread")
            {
                query = query.Where(n => !n.IsRead);
            }

            ViewBag.Filter = filter;

            var notifications = await query
                .OrderByDescending(n => n.Notification.CreatedAt)
                .ToListAsync();

            return View(notifications);
        }

        [HttpGet]
        public async Task<IActionResult> MyNotifications()
        {
            var user = await _userManager.GetUserAsync(User);

            if (user == null)
                return Unauthorized();

            var items = await _context.UserNotifications
                .Where(n => n.UserId == user.Id && !n.IsDeleted)
                .Include(n => n.Notification)
                .OrderByDescending(n => n.Notification.CreatedAt)
                .Take(5)
                .Select(n => new
                {
                    id = n.Id,
                    title = n.Notification.Title,
                    message = n.Notification.Message,
                    type = n.Notification.Type,
                    url = n.Notification.Url,
                    isRead = n.IsRead,
                    createdAt = n.Notification.CreatedAt
                })
                .ToListAsync();

            var unreadCount = await _context.UserNotifications
                .CountAsync(n =>
                    n.UserId == user.Id &&
                    !n.IsRead &&
                    !n.IsDeleted);

            return Json(new
            {
                unreadCount,
                items
            });
        }

        public async Task<IActionResult> Open(int id)
        {
            var user = await _userManager.GetUserAsync(User);

            if (user == null)
                return Unauthorized();

            var item = await _context.UserNotifications
                .Include(n => n.Notification)
                .FirstOrDefaultAsync(n =>
                    n.Id == id &&
                    n.UserId == user.Id &&
                    !n.IsDeleted);

            if (item == null)
                return NotFound();

            item.IsRead = true;
            item.ReadAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            if (!string.IsNullOrWhiteSpace(item.Notification.Url))
            {
                return Redirect(item.Notification.Url);
            }

            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        public async Task<IActionResult> MarkAllAsRead()
        {
            var user = await _userManager.GetUserAsync(User);

            if (user == null)
                return Unauthorized();

            var unread = await _context.UserNotifications
                .Where(n =>
                    n.UserId == user.Id &&
                    !n.IsRead &&
                    !n.IsDeleted)
                .ToListAsync();

            foreach (var item in unread)
            {
                item.IsRead = true;
                item.ReadAt = DateTime.UtcNow;
            }

            await _context.SaveChangesAsync();

            return Ok();
        }

        [HttpPost]
        public async Task<IActionResult> MarkAsRead(int id)
        {
            var user = await _userManager.GetUserAsync(User);

            if (user == null)
                return Unauthorized();

            var item = await _context.UserNotifications
                .FirstOrDefaultAsync(n =>
                    n.Id == id &&
                    n.UserId == user.Id &&
                    !n.IsDeleted);

            if (item == null)
                return NotFound();

            item.IsRead = true;
            item.ReadAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            return Ok();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            var user = await _userManager.GetUserAsync(User);

            if (user == null)
                return Unauthorized();

            var item = await _context.UserNotifications
                .FirstOrDefaultAsync(n =>
                    n.Id == id &&
                    n.UserId == user.Id &&
                    !n.IsDeleted);

            if (item == null)
                return NotFound();

            item.IsDeleted = true;
            item.DeletedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            TempData["Success"] = "تم حذف الإشعار";

            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteAll()
        {
            var user = await _userManager.GetUserAsync(User);

            if (user == null)
                return Unauthorized();

            var items = await _context.UserNotifications
                .Where(n => n.UserId == user.Id && !n.IsDeleted)
                .ToListAsync();

            foreach (var item in items)
            {
                item.IsDeleted = true;
                item.DeletedAt = DateTime.UtcNow;
            }

            await _context.SaveChangesAsync();

            TempData["Warning"] = "تم حذف جميع الإشعارات";

            return RedirectToAction(nameof(Index));
        }
    }
}