using RowadUmrahSystem.Web.Data;
using RowadUmrahSystem.Web.Models;
using System.Security.Claims;

namespace RowadUmrahSystem.Web.Services
{
    public class AuditService
    {
        private readonly ApplicationDbContext _context;
        private readonly IHttpContextAccessor _httpContextAccessor;

        public AuditService(
            ApplicationDbContext context,
            IHttpContextAccessor httpContextAccessor)
        {
            _context = context;
            _httpContextAccessor = httpContextAccessor;
        }

        public async Task LogAsync(
            string action,
            string entityName,
            int? travelerId,
            string? passportNumber,
            string? travelerName,
            string? details)
        {
            var user = _httpContextAccessor.HttpContext?.User;

            var userId = user?.FindFirstValue(ClaimTypes.NameIdentifier);
            var employeeName = user?.Identity?.Name ?? "Unknown";

            var ipAddress =
    _httpContextAccessor.HttpContext?
    .Connection
    .RemoteIpAddress?
    .ToString();

            var userAgent =
                _httpContextAccessor.HttpContext?
                .Request
                .Headers["User-Agent"]
                .ToString();


            var log = new AuditLog
            {
                UserId = userId,
                EmployeeName = employeeName,
                Action = action,
                EntityName = entityName,
                TravelerId = travelerId,
                PassportNumber = passportNumber,
                TravelerName = travelerName,
                Details = details,
                CreatedAt = DateTime.Now,
                IpAddress = ipAddress,
                UserAgent = userAgent,
            };

            _context.AuditLogs.Add(log);
            await _context.SaveChangesAsync();
        }
    }
}