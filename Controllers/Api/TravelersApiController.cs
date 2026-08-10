using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RowadUmrahSystem.Web.Data;
using RowadUmrahSystem.Web.ViewModels.Api;
using Microsoft.AspNetCore.Authorization;
using RowadUmrahSystem.Web.Models;
using RowadUmrahSystem.Web.Services;

namespace RowadUmrahSystem.Web.Controllers.Api
{
    [ApiController]
    [Authorize]
    [Route("api/travelers")]
    public class TravelersApiController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly IWebHostEnvironment _environment;
        private readonly PassportOcrService _passportOcrService;

        public TravelersApiController(
            ApplicationDbContext context,
            IWebHostEnvironment environment,
            PassportOcrService passportOcrService)
        {
            _context = context;
            _environment = environment;
            _passportOcrService = passportOcrService;
        }

        public sealed record PassportOcrResponseDto(
            string PassportNumber,
            string FullName,
            string Nationality,
            string Gender,
            DateTime? DateOfBirth,
            DateTime? PassportExpiryDate,
            string Mode,
            string Message);

        [HttpGet]
        public async Task<ActionResult<IReadOnlyList<TravelerListItemDto>>> GetAll(
            [FromQuery] string? search = null,
            [FromQuery] bool includeDeleted = false,
            [FromQuery] bool onlyActive = false)
        {
            var query = _context.Travelers
                .AsNoTracking()
                .AsQueryable();

            if (onlyActive)
            {
                query = query.Where(x => !x.IsDeleted && !x.IsBlocked);
            }
            else if (!includeDeleted)
            {
                query = query.Where(x => !x.IsDeleted);
            }

            if (!string.IsNullOrWhiteSpace(search))
            {
                query = query.Where(x =>
                    x.PassportNumber.Contains(search) ||
                    x.FullName.Contains(search) ||
                    x.Nationality.Contains(search) ||
                    x.PhoneNumber.Contains(search) ||
                    (x.Email != null && x.Email.Contains(search)));
            }

            var travelers = await query
                .OrderByDescending(x => x.CreatedAt)
                .Select(x => new TravelerListItemDto(
                    x.Id,
                    x.PassportNumber,
                    x.FullName,
                    x.Nationality,
                    x.Gender,
                    x.DateOfBirth,
                    x.Email,
                    x.PhoneNumber,
                    x.UmrahCount,
                    x.IsBlocked,
                    x.IsDeleted,
                    x.BlockReason,
                    x.BlockedAt,
                    x.Notes,
                    x.PassportImagePath,
                    x.PassportExpiryDate,
                    x.CreatedAt,
                    x.Trips.Count,
                    x.DeletedAt,
                    x.DeletedBy))
                .ToListAsync();

            return Ok(travelers);
        }

        [HttpGet("{id:int}")]
        public async Task<ActionResult<TravelerDetailDto>> GetById(int id)
        {
            var traveler = await _context.Travelers
                .AsNoTracking()
                .Include(t => t.Trips)
                .Include(t => t.Documents.Where(d => !d.IsDeleted))
                .FirstOrDefaultAsync(t => t.Id == id && !t.IsDeleted);

            if (traveler == null)
            {
                return NotFound();
            }

            return Ok(MapDetail(traveler));
        }

        [HttpPost]
        [Consumes("multipart/form-data")]
        public async Task<ActionResult<TravelerDetailDto>> Create(
            [FromForm] TravelerUpsertRequestDto request,
            [FromForm] IFormFile? passportImage)
        {
            if (string.IsNullOrWhiteSpace(request.PassportNumber) ||
                string.IsNullOrWhiteSpace(request.FullName) ||
                string.IsNullOrWhiteSpace(request.Nationality) ||
                string.IsNullOrWhiteSpace(request.Gender) ||
                string.IsNullOrWhiteSpace(request.PhoneNumber))
            {
                return BadRequest("الرجاء تعبئة جميع الحقول الأساسية.");
            }

            if (string.IsNullOrWhiteSpace(request.PassportImagePath) && passportImage == null)
            {
                return BadRequest("الرجاء رفع صورة الجواز أو توفير مسارها.");
            }

            var existingTraveler = await _context.Travelers
                .FirstOrDefaultAsync(t => t.PassportNumber == request.PassportNumber && !t.IsDeleted);

            if (existingTraveler != null)
            {
                return Conflict(existingTraveler.IsBlocked
                    ? $"هذا المسافر محظور. سبب الحظر: {existingTraveler.BlockReason}"
                    : "هذا المسافر مسجل مسبقاً بنفس رقم الجواز.");
            }

            var traveler = new Traveler
            {
                PassportNumber = request.PassportNumber.Trim(),
                PassportImagePath = await SavePassportImageAsync(passportImage, request.PassportImagePath),
                FullName = request.FullName.Trim(),
                Nationality = request.Nationality.Trim(),
                Gender = request.Gender.Trim(),
                DateOfBirth = request.DateOfBirth,
                Email = string.IsNullOrWhiteSpace(request.Email) ? null : request.Email.Trim(),
                ResidenceNumber = string.IsNullOrWhiteSpace(request.ResidenceNumber) ? null : request.ResidenceNumber.Trim(),
                PassportExpiryDate = request.PassportExpiryDate,
                PhoneNumber = request.PhoneNumber.Trim(),
                Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim(),
                UmrahCount = 1,
                IsBlocked = request.IsBlocked,
                BlockReason = string.IsNullOrWhiteSpace(request.BlockReason) ? null : request.BlockReason.Trim(),
                BlockedAt = request.IsBlocked ? DateTime.UtcNow : null,
                CreatedAt = DateTime.UtcNow
            };

            _context.Travelers.Add(traveler);
            await _context.SaveChangesAsync();

            return CreatedAtAction(nameof(GetById), new { id = traveler.Id }, await MapDetailAsync(traveler.Id));
        }

        [AllowAnonymous]
        [HttpPost("read-passport")]
        [Consumes("multipart/form-data")]
        public async Task<ActionResult<PassportOcrResponseDto>> ReadPassport([FromForm] IFormFile? passportImage)
        {
            if (passportImage == null)
            {
                return BadRequest("Ø§Ù„Ø±Ø¬Ø§Ø¡ ØªØ­Ù…ÙŠÙ„ ØµÙˆØ±Ø© Ø§Ù„Ø¬ÙˆØ§Ø².");
            }

            var validation = _passportOcrService.ValidateImage(passportImage);
            if (!validation.IsValid)
            {
                return BadRequest(validation.Message);
            }

            try
            {
                var savedPath = await SavePassportImageAsync(passportImage, null);
                if (string.IsNullOrWhiteSpace(savedPath))
                {
                    return StatusCode(StatusCodes.Status500InternalServerError, "ÙØ´Ù„ Ø§Ø®ØªØ²Ø§Ù† ØµÙˆØ±Ø© Ø§Ù„Ø¬ÙˆØ§Ø².");
                }

                var result = await _passportOcrService.ReadPassportAsync(savedPath);
                return Ok(MapPassportOcrResponse(result, "ready", "ØªÙ… Ø§Ø³ØªØ®Ø±Ø§Ø¬ Ø¨ÙŠØ§Ù†Ø§Øª Ø§Ù„Ø¬ÙˆØ§Ø² Ø¨Ù†Ø¬Ø§Ø­."));
            }
            catch (InvalidOperationException)
            {
                return Ok(CreateDemoPassportResponse("demo", "ØªÙ… ØªÙˆÙ„ÙŠØ¯ Ø¨ÙŠØ§Ù†Ø§Øª ØªØ¬Ø±ÙŠØ¨ÙŠØ© Ù„ØªØµÙ„ Ø§Ù„Ø³ÙŠØ±Ù Ø¯ÙˆÙ† Ù…Ù†ØµØ© Ø®Ø§Ø±Ø¬ÙŠØ©."));
            }
            catch
            {
                return Ok(CreateDemoPassportResponse("demo", "Ø¹Ù„Ù‰ Ù…Ø§ Û¿Ù… Ø§ÙØ¥ØªØ§Ø­Ø©ØŒ ØªÙ… Ø§Ø³ØªØ®Ø¯Ø§Ù… Ø¨ÙŠØ§Ù†Ø§Øª ØªØ¬Ø±ÙŠØ¨ÙŠØ©."));
            }
        }

        [HttpPut("{id:int}")]
        [Consumes("multipart/form-data")]
        public async Task<ActionResult<TravelerDetailDto>> Update(
            int id,
            [FromForm] TravelerUpsertRequestDto request,
            [FromForm] IFormFile? passportImage)
        {
            var traveler = await _context.Travelers
                .FirstOrDefaultAsync(t => t.Id == id && !t.IsDeleted);

            if (traveler == null)
            {
                return NotFound();
            }

            var duplicate = await _context.Travelers
                .FirstOrDefaultAsync(t => t.Id != id && t.PassportNumber == request.PassportNumber && !t.IsDeleted);

            if (duplicate != null)
            {
                return Conflict("هذا المسافر مسجل مسبقاً بنفس رقم الجواز.");
            }

            traveler.PassportNumber = request.PassportNumber.Trim();
            traveler.PassportImagePath = await SavePassportImageAsync(passportImage, request.PassportImagePath) ?? traveler.PassportImagePath;
            traveler.FullName = request.FullName.Trim();
            traveler.Nationality = request.Nationality.Trim();
            traveler.Gender = request.Gender.Trim();
            traveler.DateOfBirth = request.DateOfBirth;
            traveler.Email = string.IsNullOrWhiteSpace(request.Email) ? null : request.Email.Trim();
            traveler.ResidenceNumber = string.IsNullOrWhiteSpace(request.ResidenceNumber) ? null : request.ResidenceNumber.Trim();
            traveler.PassportExpiryDate = request.PassportExpiryDate;
            traveler.PhoneNumber = request.PhoneNumber.Trim();
            traveler.Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim();
            traveler.IsBlocked = request.IsBlocked;
            traveler.BlockReason = string.IsNullOrWhiteSpace(request.BlockReason) ? null : request.BlockReason.Trim();
            traveler.BlockedAt = request.IsBlocked ? traveler.BlockedAt ?? DateTime.UtcNow : null;

            await _context.SaveChangesAsync();

            return Ok(await MapDetailAsync(traveler.Id));
        }

        [HttpPost("{id:int}/block")]
        public async Task<ActionResult<TravelerDetailDto>> Block(int id, [FromBody] TravelerBlockRequestDto request)
        {
            var traveler = await _context.Travelers
                .FirstOrDefaultAsync(t => t.Id == id && !t.IsDeleted);

            if (traveler == null)
            {
                return NotFound();
            }

            traveler.IsBlocked = true;
            traveler.BlockReason = request.BlockReason?.Trim();
            traveler.BlockedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            return Ok(await MapDetailAsync(traveler.Id));
        }

        [HttpPost("{id:int}/unblock")]
        public async Task<ActionResult<TravelerDetailDto>> Unblock(int id)
        {
            var traveler = await _context.Travelers
                .FirstOrDefaultAsync(t => t.Id == id && !t.IsDeleted);

            if (traveler == null)
            {
                return NotFound();
            }

            traveler.IsBlocked = false;
            traveler.BlockReason = null;
            traveler.BlockedAt = null;

            await _context.SaveChangesAsync();

            return Ok(await MapDetailAsync(traveler.Id));
        }

        [HttpPost("{id:int}/delete")]
        public async Task<IActionResult> Delete(int id)
        {
            var traveler = await _context.Travelers.FirstOrDefaultAsync(t => t.Id == id);
            if (traveler == null)
            {
                return NotFound();
            }

            traveler.IsDeleted = true;
            traveler.DeletedAt = DateTime.UtcNow;
            traveler.DeletedBy = User.Identity?.Name ?? "Unknown";

            await _context.SaveChangesAsync();

            return NoContent();
        }

        [HttpPost("{id:int}/restore")]
        public async Task<IActionResult> Restore(int id)
        {
            var traveler = await _context.Travelers.FirstOrDefaultAsync(t => t.Id == id);
            if (traveler == null)
            {
                return NotFound();
            }

            traveler.IsDeleted = false;
            traveler.DeletedAt = null;
            traveler.DeletedBy = null;

            await _context.SaveChangesAsync();

            return NoContent();
        }

        private async Task<string?> SavePassportImageAsync(IFormFile? passportImage, string? existingPath)
        {
            if (passportImage == null || passportImage.Length == 0)
            {
                return string.IsNullOrWhiteSpace(existingPath) ? null : existingPath;
            }

            var uploadsFolder = Path.Combine(_environment.WebRootPath, "uploads", "passports");
            Directory.CreateDirectory(uploadsFolder);

            var fileName = $"{Guid.NewGuid()}{Path.GetExtension(passportImage.FileName)}";
            var filePath = Path.Combine(uploadsFolder, fileName);

            await using var stream = new FileStream(filePath, FileMode.Create);
            await passportImage.CopyToAsync(stream);

            return $"/uploads/passports/{fileName}";
        }

        private async Task<TravelerDetailDto> MapDetailAsync(int id)
        {
            var traveler = await _context.Travelers
                .AsNoTracking()
                .Include(t => t.Trips)
                .Include(t => t.Documents.Where(d => !d.IsDeleted))
                .FirstAsync(t => t.Id == id);

            return MapDetail(traveler);
        }

        private static TravelerDetailDto MapDetail(Traveler traveler)
        {
            return new TravelerDetailDto(
                traveler.Id,
                traveler.PassportNumber,
                traveler.PassportImagePath,
                traveler.FullName,
                traveler.Nationality,
                traveler.Gender,
                traveler.DateOfBirth,
                traveler.Email,
                traveler.ResidenceNumber,
                traveler.PassportExpiryDate,
                traveler.PhoneNumber,
                traveler.UmrahCount,
                traveler.IsBlocked,
                traveler.BlockReason,
                traveler.BlockedAt,
                traveler.Notes,
                traveler.IsDeleted,
                traveler.DeletedAt,
                traveler.DeletedBy,
                traveler.CreatedAt,
                traveler.Trips
                    .OrderByDescending(trip => trip.TripDate)
                    .Select(trip => new TravelerTripDto(trip.Id, trip.TripType, trip.TripDate, trip.Notes))
                    .ToList(),
                traveler.Documents
                    .OrderByDescending(document => document.UploadedAt)
                    .Select(document => new TravelerDocumentDto(document.Id, document.DocumentType, document.FileName, document.FilePath, document.Notes, document.UploadedAt))
                    .ToList()
            );
        }

        private static PassportOcrResponseDto MapPassportOcrResponse(PassportOcrResult result, string mode, string message)
        {
            return new PassportOcrResponseDto(
                result.PassportNumber ?? string.Empty,
                result.FullName ?? string.Empty,
                result.Nationality ?? string.Empty,
                result.Gender ?? string.Empty,
                result.DateOfBirth,
                result.PassportExpiryDate,
                mode,
                message);
        }

        private static PassportOcrResponseDto CreateDemoPassportResponse(string mode, string message)
        {
            return new PassportOcrResponseDto(
                "A12345678",
                "محمد أحمد عبدالله",
                "كويتي",
                "ذكر",
                new DateTime(1990, 5, 12),
                new DateTime(2030, 5, 12),
                mode,
                message);
        }
    }
}
