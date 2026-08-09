using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using RowadUmrahSystem.Web.Data;
using RowadUmrahSystem.Web.Hubs;
using RowadUmrahSystem.Web.Models;

namespace RowadUmrahSystem.Web.Services
{
    public class NotificationService
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IHubContext<DashboardHub> _hubContext;

        public NotificationService(
            ApplicationDbContext context,
            UserManager<ApplicationUser> userManager,
            IHubContext<DashboardHub> hubContext)
        {
            _context = context;
            _userManager = userManager;
            _hubContext = hubContext;
        }

        public async Task NotifyAdminsAsync(string title, string message, string type = "info", string? url = null)
        {
            await CreateAndSendAsync("Admins", title, message, type, url);
        }

        public async Task NotifyTravelersAsync(string title, string message, string type = "info", string? url = null)
        {
            await CreateAndSendAsync("Travelers", title, message, type, url);
        }

        public async Task NotifyTripsAsync(string title, string message, string type = "info", string? url = null)
        {
            await CreateAndSendAsync("Trips", title, message, type, url);
        }

        public async Task NotifyBlocksAsync(string title, string message, string type = "warning", string? url = null)
        {
            await CreateAndSendAsync("Blocks", title, message, type, url);
        }

        public async Task NotifyUsersAsync(string title, string message, string type = "info", string? url = null)
        {
            await CreateAndSendAsync("Users", title, message, type, url);
        }

        private async Task CreateAndSendAsync(
            string targetGroup,
            string title,
            string message,
            string type,
            string? url)
        {
            var notification = new Notification
            {
                Title = title,
                Message = message,
                Type = type,
                TargetGroup = targetGroup,
                Url = url,
                CreatedAt = DateTime.UtcNow
            };

            _context.Notifications.Add(notification);
            await _context.SaveChangesAsync();

            var targetUsers = await GetTargetUsersAsync(targetGroup);

            foreach (var user in targetUsers)
            {
                _context.UserNotifications.Add(new UserNotification
                {
                    UserId = user.Id,
                    NotificationId = notification.Id,
                    IsRead = false,
                    IsDeleted = false
                });
            }

            await _context.SaveChangesAsync();

            await _hubContext.Clients.Group(targetGroup).SendAsync(
                "ReceiveNotification",
                new
                {
                    title,
                    message,
                    type,
                    url
                });
        }

        private async Task<List<ApplicationUser>> GetTargetUsersAsync(string targetGroup)
        {
            var users = await _context.Users
                .Where(u => u.IsActive)
                .ToListAsync();

            var result = new List<ApplicationUser>();

            foreach (var user in users)
            {
                if (await _userManager.IsInRoleAsync(user, "Admin"))
                {
                    result.Add(user);
                    continue;
                }

                var hasPermission = targetGroup switch
                {
                    "Travelers" => await HasPermissionAsync(user.Id, "Travelers"),
                    "Trips" => await HasPermissionAsync(user.Id, "Trips"),
                    "Blocks" => await HasPermissionAsync(user.Id, "Blocks"),
                    "Users" => await HasPermissionAsync(user.Id, "Users"),
                    "Reports" => await HasPermissionAsync(user.Id, "Reports"),
                    "Admins" => false,
                    _ => false
                };

                if (hasPermission)
                {
                    result.Add(user);
                }
            }

            return result
                .GroupBy(u => u.Id)
                .Select(g => g.First())
                .ToList();
        }

        private async Task<bool> HasPermissionAsync(string userId, string permission)
        {
            var permissions = await _context.UserPermissions
                .FirstOrDefaultAsync(p => p.UserId == userId);

            if (permissions == null)
                return false;

            return permission switch
            {
                "Travelers" => permissions.CanManageTravelers,
                "Trips" => permissions.CanManageTrips,
                "Blocks" => permissions.CanManageBlocks,
                "Users" => permissions.CanManageUsers,
                "Reports" => permissions.CanViewReports,
                _ => false
            };
        }
    }
}