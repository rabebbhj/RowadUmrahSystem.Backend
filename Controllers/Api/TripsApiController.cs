using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RowadUmrahSystem.Web.Data;
using RowadUmrahSystem.Web.Models;
using RowadUmrahSystem.Web.Services;
using RowadUmrahSystem.Web.ViewModels.Api;

namespace RowadUmrahSystem.Web.Controllers.Api
{
    [ApiController]
    [Authorize]
    [Route("api/trips")]
    public class TripsApiController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly PermissionService _permissionService;

        public TripsApiController(
            ApplicationDbContext context,
            PermissionService permissionService)
        {
            _context = context;
            _permissionService = permissionService;
        }

        [HttpGet]
        public async Task<ActionResult<IReadOnlyList<TripListItemDto>>> GetAll(
            [FromQuery] string? search = null,
            [FromQuery] bool includeDeleted = false)
        {
            if (!await CanViewTrips())
                return Forbid();

            var query = _context.Trips
                .AsNoTracking()
                .Include(x => x.Traveler)
                .AsQueryable();

            if (!includeDeleted)
            {
                query = query.Where(x => !x.IsDeleted && !x.Traveler.IsDeleted);
            }

            if (!string.IsNullOrWhiteSpace(search))
            {
                query = query.Where(x =>
                    x.TripType.Contains(search) ||
                    (x.Notes != null && x.Notes.Contains(search)) ||
                    x.Traveler.FullName.Contains(search) ||
                    x.Traveler.PassportNumber.Contains(search));
            }

            var trips = await query
                .OrderByDescending(x => x.TripDate)
                .ThenByDescending(x => x.CreatedAt)
                .Select(x => new TripListItemDto(
                    x.Id,
                    x.TravelerId,
                    x.Traveler.FullName,
                    x.Traveler.PassportNumber,
                    x.TripType,
                    x.TripDate,
                    x.Notes,
                    x.CreatedAt,
                    x.IsDeleted,
                    x.DeletedAt,
                    x.DeletedBy))
                .ToListAsync();

            return Ok(trips);
        }

        [HttpPost]
        public async Task<ActionResult<TripListItemDto>> Create([FromBody] TripCreateRequestDto request)
        {
            if (!await CanCreateTrips())
                return Forbid();

            if (request.TravelerId <= 0)
                return BadRequest("Traveler is required.");

            if (request.TripDate == default)
                return BadRequest("Trip date is required.");

            var traveler = await _context.Travelers
                .FirstOrDefaultAsync(x => x.Id == request.TravelerId && !x.IsDeleted);

            if (traveler == null)
                return NotFound("Traveler not found.");

            if (traveler.IsBlocked)
                return BadRequest("Cannot create a trip for a blocked traveler.");

            var tripDate = request.TripDate.Date;

            var duplicateTrip = await _context.Trips.AnyAsync(x =>
                !x.IsDeleted &&
                x.TravelerId == traveler.Id &&
                x.TripType == "Umrah" &&
                x.TripDate.Date == tripDate);

            if (duplicateTrip)
                return Conflict("This traveler already has a trip on the same date.");

            var trip = new Trip
            {
                TravelerId = traveler.Id,
                TripType = "Umrah",
                TripDate = tripDate,
                Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim(),
                CreatedAt = DateTime.UtcNow,
                IsDeleted = false
            };

            _context.Trips.Add(trip);
            await _context.SaveChangesAsync();

            traveler.UmrahCount = await _context.Trips
                .CountAsync(x => x.TravelerId == traveler.Id && !x.IsDeleted);

            await _context.SaveChangesAsync();

            var createdTrip = await _context.Trips
                .AsNoTracking()
                .Include(x => x.Traveler)
                .FirstAsync(x => x.Id == trip.Id);

            return Ok(MapTrip(createdTrip));
        }

        [HttpPost("{id:int}/archive")]
        public async Task<ActionResult<TripListItemDto>> Archive(int id)
        {
            if (!await CanArchiveTrips())
                return Forbid();

            var trip = await _context.Trips
                .Include(x => x.Traveler)
                .FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);

            if (trip == null)
                return NotFound();

            trip.IsDeleted = true;
            trip.DeletedAt = DateTime.UtcNow;
            trip.DeletedBy = User.Identity?.Name ?? "Unknown";

            await _context.SaveChangesAsync();

            if (trip.Traveler != null)
            {
                trip.Traveler.UmrahCount = await _context.Trips
                    .CountAsync(x => x.TravelerId == trip.TravelerId && !x.IsDeleted);

                await _context.SaveChangesAsync();
            }

            return Ok(MapTrip(trip));
        }

        [HttpPost("{id:int}/restore")]
        public async Task<ActionResult<TripListItemDto>> Restore(int id)
        {
            if (!await CanRestoreTrips())
                return Forbid();

            var trip = await _context.Trips
                .Include(x => x.Traveler)
                .FirstOrDefaultAsync(x => x.Id == id && x.IsDeleted);

            if (trip == null)
                return NotFound();

            if (trip.Traveler == null || trip.Traveler.IsDeleted)
                return BadRequest("Cannot restore a trip for an archived traveler.");

            trip.IsDeleted = false;
            trip.DeletedAt = null;
            trip.DeletedBy = null;

            await _context.SaveChangesAsync();

            trip.Traveler.UmrahCount = await _context.Trips
                .CountAsync(x => x.TravelerId == trip.TravelerId && !x.IsDeleted);

            await _context.SaveChangesAsync();

            return Ok(MapTrip(trip));
        }

        private async Task<bool> CanViewTrips()
        {
            return await _permissionService.HasPermissionAsync(User, "Trips.View");
        }

        private async Task<bool> CanCreateTrips()
        {
            return await _permissionService.HasPermissionAsync(User, "Trips.Create");
        }

        private async Task<bool> CanArchiveTrips()
        {
            return await _permissionService.HasPermissionAsync(User, "Trips.Archive");
        }

        private async Task<bool> CanRestoreTrips()
        {
            return await _permissionService.HasPermissionAsync(User, "Trips.Restore");
        }

        private static TripListItemDto MapTrip(Trip trip)
        {
            return new TripListItemDto(
                trip.Id,
                trip.TravelerId,
                trip.Traveler?.FullName ?? string.Empty,
                trip.Traveler?.PassportNumber ?? string.Empty,
                trip.TripType,
                trip.TripDate,
                trip.Notes,
                trip.CreatedAt,
                trip.IsDeleted,
                trip.DeletedAt,
                trip.DeletedBy);
        }
    }
}
