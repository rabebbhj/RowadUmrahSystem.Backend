using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.SignalR;
using RowadUmrahSystem.Web.Models;
using RowadUmrahSystem.Web.Services;

namespace RowadUmrahSystem.Web.Hubs
{
    public class DashboardHub : Hub
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly PermissionService _permissionService;

        public DashboardHub(
            UserManager<ApplicationUser> userManager,
            PermissionService permissionService)
        {
            _userManager = userManager;
            _permissionService = permissionService;
        }

        public override async Task OnConnectedAsync()
        {
            var user = Context.User;

            if (user?.Identity?.IsAuthenticated == true)
            {
                var appUser = await _userManager.GetUserAsync(user);

                if (appUser != null)
                {
                    if (await _userManager.IsInRoleAsync(appUser, "Admin"))
                    {
                        await Groups.AddToGroupAsync(Context.ConnectionId, "Admins");
                    }

                    if (await _permissionService.HasPermissionAsync(user, "Travelers"))
                    {
                        await Groups.AddToGroupAsync(Context.ConnectionId, "Travelers");
                    }

                    if (await _permissionService.HasPermissionAsync(user, "Trips"))
                    {
                        await Groups.AddToGroupAsync(Context.ConnectionId, "Trips");
                    }

                    if (await _permissionService.HasPermissionAsync(user, "Blocks"))
                    {
                        await Groups.AddToGroupAsync(Context.ConnectionId, "Blocks");
                    }

                    if (await _permissionService.HasPermissionAsync(user, "Users"))
                    {
                        await Groups.AddToGroupAsync(Context.ConnectionId, "Users");
                    }

                    if (await _permissionService.HasPermissionAsync(user, "Reports"))
                    {
                        await Groups.AddToGroupAsync(Context.ConnectionId, "Reports");
                    }
                }
            }

            await base.OnConnectedAsync();
        }
    }
}