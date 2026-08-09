using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
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

        public AuthController(
            SignInManager<ApplicationUser> signInManager,
            UserManager<ApplicationUser> userManager)
        {
            _signInManager = signInManager;
            _userManager = userManager;
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
                roles));
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

            var roles = await _userManager.GetRolesAsync(user);
            return Ok(new LoginResponseDto(
                true,
                "Login successful.",
                new AuthUserDto(true, user.Email, user.FullName, roles.ToArray())));
        }

        [HttpPost("logout")]
        public async Task<IActionResult> Logout()
        {
            await HttpContext.SignOutAsync(IdentityConstants.ApplicationScheme);
            return Ok();
        }
    }
}
