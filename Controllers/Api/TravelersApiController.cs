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
        private readonly PermissionService _permissionService;

        public TravelersApiController(
            ApplicationDbContext context,
            IWebHostEnvironment environment,
            PassportOcrService passportOcrService,
            PermissionService permissionService)
        {
            _context = context;
            _environment = environment;
            _passportOcrService = passportOcrService;
            _permissionService = permissionService;
        }

        public sealed record PassportOcrResponseDto(
            string PassportNumber,
            string FullName,
            string FirstNameArabic,
            string FatherNameArabic,
            string GrandFatherNameArabic,
            string FamilyNameArabic,
            string FirstNameEnglish,
            string FatherNameEnglish,
            string GrandFatherNameEnglish,
            string FamilyNameEnglish,
            string Nationality,
            string Gender,
            string Profession,
            string BirthCountry,
            string BirthCity,
            string MaritalStatus,
            DateTime? DateOfBirth,
            string ResidenceNumber,
            DateTime? ResidenceExpiryDate,
            DateTime? PassportExpiryDate,
            string Mode,
            string Message);

        public sealed record CivilIdOcrResponseDto(
            string CivilId,
            string PassportNumber,
            string FullName,
            string FirstNameArabic,
            string FatherNameArabic,
            string GrandFatherNameArabic,
            string FamilyNameArabic,
            string FirstNameEnglish,
            string FatherNameEnglish,
            string GrandFatherNameEnglish,
            string FamilyNameEnglish,
            string Nationality,
            string Gender,
            string Profession,
            string BirthCountry,
            string BirthCity,
            string MaritalStatus,
            DateTime? DateOfBirth,
            string ResidenceNumber,
            DateTime? ResidenceExpiryDate,
            DateTime? PassportExpiryDate,
            string Mode,
            string Message);

        [HttpGet]
        public async Task<ActionResult<IReadOnlyList<TravelerListItemDto>>> GetAll(
            [FromQuery] string? search = null,
            [FromQuery] bool includeDeleted = false,
            [FromQuery] bool onlyActive = false,
            [FromQuery] bool documentsReviewedOnly = false)
        {
            if (documentsReviewedOnly)
            {
                if (!await CanViewDocuments())
                    return Forbid();
            }
            else if (!await CanViewTravelers())
            {
                return Forbid();
            }

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

            if (documentsReviewedOnly)
            {
                query = query.Where(x => x.DocumentsReviewed);
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
                    x.DocumentsReviewed,
                    x.DocumentsReviewedAt,
                    x.Trips.Count,
                    x.DeletedAt,
                    x.DeletedBy))
                .ToListAsync();

            return Ok(travelers);
        }

        [HttpGet("{id:int}")]
        public async Task<ActionResult<TravelerDetailDto>> GetById(int id)
        {
            if (!await CanViewTravelers() && !await CanViewDocuments())
                return Forbid();

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

        [AllowAnonymous]
        [HttpPost]
        [Consumes("multipart/form-data")]
        public async Task<ActionResult<TravelerDetailDto>> Create(
            [FromForm] TravelerUpsertRequestDto request,
            [FromForm] IFormFile? passportImage,
            [FromForm] IFormFile? civilIdImage,
            [FromForm] IFormFile? visaImage)
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

            if (request.HasVisa && visaImage == null)
            {
                return BadRequest("الرجاء رفع صورة التأشيرة.");
            }

            var existingTraveler = await _context.Travelers
                .FirstOrDefaultAsync(t => t.PassportNumber == request.PassportNumber && !t.IsDeleted);

            if (existingTraveler != null)
            {
                if (existingTraveler.IsBlocked)
                {
                    return Conflict($"هذا المسافر محظور. سبب الحظر: {existingTraveler.BlockReason}");
                }

                if (HasReservationPackageData(request))
                {
                    await AddReservationDocumentsAsync(existingTraveler.Id, civilIdImage, visaImage);
                    await AddReservationTripAsync(existingTraveler, request);
                    return Ok(await MapDetailAsync(existingTraveler.Id));
                }

                return Conflict("هذا المسافر مسجل مسبقاً بنفس رقم الجواز.");
            }

            var traveler = new Traveler
            {
                PassportNumber = request.PassportNumber.Trim(),
                PassportImagePath = await SavePassportImageAsync(passportImage, request.PassportImagePath),
                FullName = request.FullName.Trim(),
                FirstNameArabic = CleanOptional(request.FirstNameArabic),
                FatherNameArabic = CleanOptional(request.FatherNameArabic),
                GrandFatherNameArabic = CleanOptional(request.GrandFatherNameArabic),
                FamilyNameArabic = CleanOptional(request.FamilyNameArabic),
                FirstNameEnglish = CleanOptional(request.FirstNameEnglish),
                FatherNameEnglish = CleanOptional(request.FatherNameEnglish),
                GrandFatherNameEnglish = CleanOptional(request.GrandFatherNameEnglish),
                FamilyNameEnglish = CleanOptional(request.FamilyNameEnglish),
                Nationality = request.Nationality.Trim(),
                Gender = request.Gender.Trim(),
                Profession = CleanOptional(request.Profession),
                BirthCountry = CleanOptional(request.BirthCountry),
                BirthCity = CleanOptional(request.BirthCity),
                MaritalStatus = CleanOptional(request.MaritalStatus),
                DateOfBirth = request.DateOfBirth,
                Email = string.IsNullOrWhiteSpace(request.Email) ? null : request.Email.Trim(),
                ResidenceNumber = string.IsNullOrWhiteSpace(request.ResidenceNumber) ? null : request.ResidenceNumber.Trim(),
                ResidenceExpiryDate = request.ResidenceExpiryDate,
                PassportExpiryDate = request.PassportExpiryDate,
                PhoneNumber = request.PhoneNumber.Trim(),
                Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim(),
                UmrahCount = 0,
                IsBlocked = request.IsBlocked,
                BlockReason = string.IsNullOrWhiteSpace(request.BlockReason) ? null : request.BlockReason.Trim(),
                BlockedAt = request.IsBlocked ? DateTime.UtcNow : null,
                CreatedAt = DateTime.UtcNow
            };

            _context.Travelers.Add(traveler);
            await _context.SaveChangesAsync();

            await AddReservationDocumentsAsync(traveler.Id, civilIdImage, visaImage);

            if (HasReservationPackageData(request))
            {
                await AddReservationTripAsync(traveler, request);
            }

            return CreatedAtAction(nameof(GetById), new { id = traveler.Id }, await MapDetailAsync(traveler.Id));
        }

        [AllowAnonymous]
        [HttpPost("read-passport")]
        [Consumes("multipart/form-data")]
        public async Task<ActionResult<PassportOcrResponseDto>> ReadPassport([FromForm] IFormFile? passportImage)
        {
            if (passportImage == null)
            {
                return BadRequest("الرجاء تحميل صورة الجواز.");
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
                    return StatusCode(StatusCodes.Status500InternalServerError, "فشل حفظ صورة الجواز.");
                }

                var result = await _passportOcrService.ReadPassportAsync(savedPath);
                return Ok(MapPassportOcrResponse(result, "ready", "تم استخراج بيانات الجواز بنجاح."));
            }
            catch (InvalidOperationException ex)
            {
                return Ok(CreateManualPassportResponse($"تعذرت قراءة الجواز محليا. {ex.Message}"));
            }
            catch
            {
                return Ok(CreateManualPassportResponse("تعذرت قراءة الجواز حاليا. تم حفظ صورة الجواز، الرجاء إدخال البيانات يدويا."));
            }
        }

        [AllowAnonymous]
        [HttpPost("read-civil-id")]
        [Consumes("multipart/form-data")]
        public async Task<ActionResult<CivilIdOcrResponseDto>> ReadCivilId([FromForm] IFormFile? civilIdImage)
        {
            if (civilIdImage == null)
            {
                return BadRequest("الرجاء تحميل صورة البطاقة المدنية.");
            }

            var validation = _passportOcrService.ValidateImage(civilIdImage);
            if (!validation.IsValid)
            {
                return BadRequest(validation.Message);
            }

            try
            {
                var savedPath = await SavePassportImageAsync(civilIdImage, null);
                if (string.IsNullOrWhiteSpace(savedPath))
                {
                    return StatusCode(StatusCodes.Status500InternalServerError, "فشل حفظ صورة البطاقة المدنية.");
                }

                var result = await _passportOcrService.ReadCivilIdAsync(savedPath);
                return Ok(MapCivilIdOcrResponse(result, "ready", "تم استخراج بيانات البطاقة المدنية بنجاح."));
            }
            catch (InvalidOperationException ex)
            {
                return Ok(CreateManualCivilIdResponse($"تعذرت قراءة البطاقة المدنية محليا. {ex.Message}"));
            }
            catch
            {
                return Ok(CreateManualCivilIdResponse("تعذرت قراءة البطاقة المدنية حاليا. الرجاء إدخال البيانات يدويا."));
            }
        }

        [HttpPut("{id:int}")]
        [Consumes("multipart/form-data")]
        public async Task<ActionResult<TravelerDetailDto>> Update(
            int id,
            [FromForm] TravelerUpsertRequestDto request,
            [FromForm] IFormFile? passportImage)
        {
            if (!await CanEditTravelers())
                return Forbid();

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
            traveler.FirstNameArabic = CleanOptional(request.FirstNameArabic);
            traveler.FatherNameArabic = CleanOptional(request.FatherNameArabic);
            traveler.GrandFatherNameArabic = CleanOptional(request.GrandFatherNameArabic);
            traveler.FamilyNameArabic = CleanOptional(request.FamilyNameArabic);
            traveler.FirstNameEnglish = CleanOptional(request.FirstNameEnglish);
            traveler.FatherNameEnglish = CleanOptional(request.FatherNameEnglish);
            traveler.GrandFatherNameEnglish = CleanOptional(request.GrandFatherNameEnglish);
            traveler.FamilyNameEnglish = CleanOptional(request.FamilyNameEnglish);
            traveler.Nationality = request.Nationality.Trim();
            traveler.Gender = request.Gender.Trim();
            traveler.Profession = CleanOptional(request.Profession);
            traveler.BirthCountry = CleanOptional(request.BirthCountry);
            traveler.BirthCity = CleanOptional(request.BirthCity);
            traveler.MaritalStatus = CleanOptional(request.MaritalStatus);
            traveler.DateOfBirth = request.DateOfBirth;
            traveler.Email = string.IsNullOrWhiteSpace(request.Email) ? null : request.Email.Trim();
            traveler.ResidenceNumber = string.IsNullOrWhiteSpace(request.ResidenceNumber) ? null : request.ResidenceNumber.Trim();
            traveler.ResidenceExpiryDate = request.ResidenceExpiryDate;
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
            if (!await CanBlockTravelers())
                return Forbid();

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
            if (!await CanUnblockTravelers())
                return Forbid();

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

        [HttpPost("{id:int}/documents-reviewed")]
        public async Task<ActionResult<TravelerDetailDto>> MarkDocumentsReviewed(int id)
        {
            if (!await CanEditTravelers())
                return Forbid();

            var traveler = await _context.Travelers
                .FirstOrDefaultAsync(t => t.Id == id && !t.IsDeleted);

            if (traveler == null)
            {
                return NotFound();
            }

            traveler.DocumentsReviewed = true;
            traveler.DocumentsReviewedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            return Ok(await MapDetailAsync(traveler.Id));
        }

        [HttpPost("{id:int}/delete")]
        public async Task<IActionResult> Delete(int id)
        {
            if (!await CanArchiveTravelers())
                return Forbid();

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
            if (!await CanRestoreTravelers())
                return Forbid();

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

        private async Task<bool> CanViewTravelers()
        {
            return await _permissionService.HasPermissionAsync(User, "Travelers.View");
        }

        private async Task<bool> CanViewDocuments()
        {
            return await _permissionService.HasPermissionAsync(User, "Documents.View");
        }

        private async Task<bool> CanEditTravelers()
        {
            return await _permissionService.HasPermissionAsync(User, "Travelers.Edit");
        }

        private async Task<bool> CanArchiveTravelers()
        {
            return await _permissionService.HasPermissionAsync(User, "Travelers.Archive");
        }

        private async Task<bool> CanRestoreTravelers()
        {
            return await _permissionService.HasPermissionAsync(User, "Travelers.Restore");
        }

        private async Task<bool> CanBlockTravelers()
        {
            return await _permissionService.HasPermissionAsync(User, "Blocks.Block");
        }

        private async Task<bool> CanUnblockTravelers()
        {
            return await _permissionService.HasPermissionAsync(User, "Blocks.Unblock");
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

        private async Task<string> SaveTravelerDocumentAsync(int travelerId, IFormFile document)
        {
            var uploadsFolder = Path.Combine(_environment.WebRootPath, "uploads", "documents", travelerId.ToString());
            Directory.CreateDirectory(uploadsFolder);

            var extension = Path.GetExtension(document.FileName);
            var fileName = $"{Guid.NewGuid()}{extension}";
            var filePath = Path.Combine(uploadsFolder, fileName);

            await using var stream = new FileStream(filePath, FileMode.Create);
            await document.CopyToAsync(stream);

            return $"/uploads/documents/{travelerId}/{fileName}";
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
                traveler.FirstNameArabic,
                traveler.FatherNameArabic,
                traveler.GrandFatherNameArabic,
                traveler.FamilyNameArabic,
                traveler.FirstNameEnglish,
                traveler.FatherNameEnglish,
                traveler.GrandFatherNameEnglish,
                traveler.FamilyNameEnglish,
                traveler.Nationality,
                traveler.Gender,
                traveler.Profession,
                traveler.BirthCountry,
                traveler.BirthCity,
                traveler.MaritalStatus,
                traveler.DateOfBirth,
                traveler.Email,
                traveler.ResidenceNumber,
                traveler.ResidenceExpiryDate,
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
                traveler.DocumentsReviewed,
                traveler.DocumentsReviewedAt,
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

        private static string? CleanOptional(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        private static bool HasReservationPackageData(TravelerUpsertRequestDto request)
        {
            return !string.IsNullOrWhiteSpace(request.PackageId) ||
                !string.IsNullOrWhiteSpace(request.PackageName) ||
                request.BookingDate.HasValue ||
                !string.IsNullOrWhiteSpace(request.RoomType) ||
                !string.IsNullOrWhiteSpace(request.TransportType) ||
                request.HasVisa ||
                request.ReservationTotal.HasValue;
        }

        private static string BuildReservationTripNotes(TravelerUpsertRequestDto request)
        {
            var parts = new List<string>
            {
                "طلب حجز من الموقع العام",
                string.IsNullOrWhiteSpace(request.PackageId) ? "" : $"PackageId: {request.PackageId.Trim()}",
                string.IsNullOrWhiteSpace(request.PackageName) ? "" : $"الباقة: {request.PackageName.Trim()}",
                request.BookingDate.HasValue ? $"تاريخ الحجز: {request.BookingDate.Value:yyyy-MM-dd}" : "",
                string.IsNullOrWhiteSpace(request.RoomType) ? "" : $"نوع الغرفة: {request.RoomType.Trim()}",
                string.IsNullOrWhiteSpace(request.TransportType) ? "" : $"وسيلة النقل: {request.TransportType.Trim()}",
                $"التأشيرة: {(request.HasVisa ? "لديه تأشيرة" : "بدون تأشيرة")}",
                request.ReservationTotal.HasValue ? $"المبلغ المحسوب: {request.ReservationTotal.Value:0.##} د.ك" : ""
            };

            return string.Join(" | ", parts.Where(part => !string.IsNullOrWhiteSpace(part)));
        }

        private async Task AddReservationDocumentsAsync(int travelerId, IFormFile? civilIdImage, IFormFile? visaImage)
        {
            var documents = new List<TravelerDocument>();

            if (civilIdImage is { Length: > 0 })
            {
                var documentPath = await SaveTravelerDocumentAsync(travelerId, civilIdImage);
                documents.Add(new TravelerDocument
                {
                    TravelerId = travelerId,
                    DocumentType = "CivilId",
                    FileName = Path.GetFileName(civilIdImage.FileName),
                    FilePath = documentPath,
                    Notes = "بطاقة الهوية من طلب الحجز العام",
                    UploadedAt = DateTime.UtcNow,
                    IsDeleted = false
                });
            }

            if (visaImage is { Length: > 0 })
            {
                var documentPath = await SaveTravelerDocumentAsync(travelerId, visaImage);
                documents.Add(new TravelerDocument
                {
                    TravelerId = travelerId,
                    DocumentType = "Visa",
                    FileName = Path.GetFileName(visaImage.FileName),
                    FilePath = documentPath,
                    Notes = "تأشيرة من طلب الحجز العام",
                    UploadedAt = DateTime.UtcNow,
                    IsDeleted = false
                });
            }

            if (documents.Count == 0)
            {
                return;
            }

            _context.TravelerDocuments.AddRange(documents);
            await _context.SaveChangesAsync();
        }

        private async Task AddReservationTripAsync(Traveler traveler, TravelerUpsertRequestDto request)
        {
            var tripDate = request.BookingDate?.Date ?? DateTime.UtcNow.Date;
            var duplicateTrip = await _context.Trips.AnyAsync(x =>
                !x.IsDeleted &&
                x.TravelerId == traveler.Id &&
                x.TripType == "Umrah" &&
                x.TripDate.Date == tripDate);

            if (!duplicateTrip)
            {
                _context.Trips.Add(new Trip
                {
                    TravelerId = traveler.Id,
                    TripType = "Umrah",
                    TripDate = tripDate,
                    Notes = BuildReservationTripNotes(request),
                    CreatedAt = DateTime.UtcNow,
                    IsDeleted = false
                });

                await _context.SaveChangesAsync();
            }

            traveler.UmrahCount = await _context.Trips
                .CountAsync(x => x.TravelerId == traveler.Id && !x.IsDeleted);

            await _context.SaveChangesAsync();
        }

        private static PassportOcrResponseDto MapPassportOcrResponse(PassportOcrResult result, string mode, string message)
        {
            return new PassportOcrResponseDto(
                result.PassportNumber ?? string.Empty,
                result.FullName ?? string.Empty,
                result.FirstNameArabic ?? string.Empty,
                result.FatherNameArabic ?? string.Empty,
                result.GrandFatherNameArabic ?? string.Empty,
                result.FamilyNameArabic ?? string.Empty,
                result.FirstNameEnglish ?? string.Empty,
                result.FatherNameEnglish ?? string.Empty,
                result.GrandFatherNameEnglish ?? string.Empty,
                result.FamilyNameEnglish ?? string.Empty,
                result.Nationality ?? string.Empty,
                result.Gender ?? string.Empty,
                result.Profession ?? string.Empty,
                result.BirthCountry ?? string.Empty,
                result.BirthCity ?? string.Empty,
                result.MaritalStatus ?? string.Empty,
                result.DateOfBirth,
                result.ResidenceNumber ?? string.Empty,
                result.ResidenceExpiryDate,
                result.PassportExpiryDate,
                mode,
                message);
        }

        private static PassportOcrResponseDto CreateManualPassportResponse(string message)
        {
            return new PassportOcrResponseDto(
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                null,
                string.Empty,
                null,
                null,
                "demo",
                message);
        }

        private static CivilIdOcrResponseDto MapCivilIdOcrResponse(CivilIdOcrResult result, string mode, string message)
        {
            return new CivilIdOcrResponseDto(
                result.CivilId ?? string.Empty,
                result.PassportNumber ?? string.Empty,
                result.FullName ?? string.Empty,
                result.FirstNameArabic ?? string.Empty,
                result.FatherNameArabic ?? string.Empty,
                result.GrandFatherNameArabic ?? string.Empty,
                result.FamilyNameArabic ?? string.Empty,
                result.FirstNameEnglish ?? string.Empty,
                result.FatherNameEnglish ?? string.Empty,
                result.GrandFatherNameEnglish ?? string.Empty,
                result.FamilyNameEnglish ?? string.Empty,
                result.Nationality ?? string.Empty,
                result.Gender ?? string.Empty,
                result.Profession ?? string.Empty,
                result.BirthCountry ?? string.Empty,
                result.BirthCity ?? string.Empty,
                result.MaritalStatus ?? string.Empty,
                result.DateOfBirth,
                result.ResidenceNumber ?? string.Empty,
                result.ResidenceExpiryDate,
                result.PassportExpiryDate,
                mode,
                message);
        }

        private static CivilIdOcrResponseDto CreateManualCivilIdResponse(string message)
        {
            return new CivilIdOcrResponseDto(
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                null,
                string.Empty,
                null,
                null,
                "demo",
                message);
        }
    }
}
