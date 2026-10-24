using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RowadUmrahSystem.Web.Data;
using RowadUmrahSystem.Web.Models;
using RowadUmrahSystem.Web.Services.BookingFlow;

namespace RowadUmrahSystem.Web.Controllers.Api
{
    /// <summary>
    /// Booking flow of the app (spec "Rowad — Spécification fonctionnelle de la réservation"):
    /// options (departures with seats, nationalities, hotels and rooms), server-side quote,
    /// checkout (pending booking + seat and bed hold) and MyFatoorah payment. The total is
    /// always computed here; the MyFatoorah key never leaves the server.
    /// </summary>
    [ApiController]
    [Route("api/booking-flow")]
    public class BookingFlowApiController : ControllerBase
    {
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly BookingConfigStore _configStore;
        private readonly BookingFlowService _flow;
        private readonly IPaymentGateway _gateway;
        private readonly MyFatoorahOptions _paymentOptions;
        private readonly ILogger<BookingFlowApiController> _logger;
        private readonly string _packagesPath;

        public BookingFlowApiController(
            ApplicationDbContext context,
            UserManager<ApplicationUser> userManager,
            BookingConfigStore configStore,
            BookingFlowService flow,
            IPaymentGateway gateway,
            IOptions<MyFatoorahOptions> paymentOptions,
            ILogger<BookingFlowApiController> logger,
            IWebHostEnvironment environment)
        {
            _context = context;
            _userManager = userManager;
            _configStore = configStore;
            _flow = flow;
            _gateway = gateway;
            _paymentOptions = paymentOptions.Value;
            _logger = logger;
            _packagesPath = Path.Combine(environment.ContentRootPath, "App_Data", "travel-packages.json");
        }

        /// <summary>Everything the six steps need; room availability when a departure is given.</summary>
        [AllowAnonymous]
        [HttpGet("options")]
        public async Task<ActionResult<BookingOptionsDto>> GetOptions([FromQuery, Required] string packageId, [FromQuery] string? departureDate)
        {
            var config = await _configStore.ReadAsync();
            var package = await FindPackageAsync(packageId);
            var pack = package == null ? null : config.Pack(package.Id);
            if (package == null || pack == null) return NotFound(Problem("هذه الباقة غير متاحة للحجز عبر التطبيق حالياً."));

            await _flow.ReleaseExpiredHoldsAsync();
            var today = PackageDepartures.TodayInKuwait();
            var dates = PackageDepartures.Upcoming(package, today);
            var taken = await _flow.SeatsTakenAsync(package.Id, dates);
            var departures = dates.Select(date =>
            {
                var day = DateOnly.ParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture);
                var left = Math.Max(0, pack.SeatsPerDeparture - taken.GetValueOrDefault(date));
                var status = left == 0 ? "full" : left <= config.FewSeatsThreshold ? "few" : "available";
                return new DepartureDto(date, day.AddDays(package.DurationDays).ToString("yyyy-MM-dd"), (int)day.DayOfWeek, left, status);
            }).ToList();

            var roomsTaken = string.IsNullOrEmpty(departureDate) ? null : await _flow.RoomsTakenAsync(package.Id, departureDate);
            var hotels = pack.Cities().ToDictionary(city => city, city =>
                (pack.Hotels.TryGetValue(city, out var ids) ? ids : Array.Empty<string>())
                    .Select(config.Hotel).Where(hotel => hotel != null && hotel.City == city)
                    .Select(hotel => MapHotel(hotel!, pack.NightsIn(city), roomsTaken))
                    .ToList());

            return Ok(new BookingOptionsDto(
                new PackSummaryDto(package.Id, package.Name, package.DurationDays, pack.Nights.Makkah, pack.Nights.Madinah, package.DepartureWeekdays ?? Array.Empty<int>(), config.Currency, package.ImageUrl),
                departures,
                config.Nationalities.Select(item => new NationalityDto(item.Name, item.VisaType)).ToList(),
                config.FrequentNationalities,
                hotels,
                new BookingRulesDto(config.FewSeatsThreshold, config.MinPassportValidityMonths, config.ExtractionConfidenceThreshold,
                    config.PaymentHoldMinutes, config.DraftExpiryDays, config.Documents.CivilIdRequired, config.Documents.CivilIdMinAge, config.Documents.PersonalPhotoRequired),
                _gateway.IsConfigured));
        }

        /// <summary>
        /// The account holder's passport from an earlier booking (traveler with the account email
        /// and a stored passport image), so step 2 starts with «المسافر 1 · صاحب الحساب». No image
        /// is sent back; the stored one is reused when the booking is sent.
        /// </summary>
        [Authorize]
        [HttpGet("my-passport")]
        public async Task<ActionResult<SavedPassportDto>> MyPassport()
        {
            var user = await _userManager.GetUserAsync(User);
            if (string.IsNullOrWhiteSpace(user?.Email)) return NoContent();
            var email = user.Email.Trim().ToLower();
            var traveler = await _context.Travelers.AsNoTracking()
                .Where(item => !item.IsDeleted && !item.IsBlocked && item.Email != null && item.Email.ToLower() == email && item.PassportImagePath != null)
                .OrderByDescending(item => item.CreatedAt)
                .FirstOrDefaultAsync();
            if (traveler == null) return NoContent();
            return Ok(new SavedPassportDto(
                traveler.FullName,
                traveler.PassportNumber,
                traveler.PassportExpiryDate?.ToString("yyyy-MM-dd"),
                traveler.DateOfBirth == default ? null : traveler.DateOfBirth.ToString("yyyy-MM-dd"),
                traveler.Nationality,
                traveler.Gender is "أنثى" or "female" ? "female" : traveler.Gender is "ذكر" or "male" ? "male" : null,
                traveler.ResidenceNumber));
        }

        /// <summary>
        /// Price lines and total for a draft, with blocking errors and notices (seats included).
        /// With paymentMethods=true (signed in), the methods MyFatoorah offers for that total.
        /// </summary>
        [AllowAnonymous]
        [HttpPost("quote")]
        public async Task<ActionResult<QuoteResponseDto>> Quote([FromBody] BookingDraftRequest draft, [FromQuery] bool paymentMethods = false)
        {
            var config = await _configStore.ReadAsync();
            var package = await FindPackageAsync(draft.PackageId);
            if (package == null) return BadRequest(Problem("الباقة غير متاحة للحجز حالياً."));

            await _flow.ReleaseExpiredHoldsAsync();
            var quote = BookingQuote.Calculate(config, package, draft, PackageDepartures.TodayInKuwait(), requireStays: false);
            quote = await AddSeatIssuesAsync(quote, config, package, draft, null);

            IReadOnlyList<PaymentMethodDto> methods = Array.Empty<PaymentMethodDto>();
            if (paymentMethods && User.Identity?.IsAuthenticated == true && quote.Valid && quote.Total > 0)
                methods = await PaymentMethodsAsync(quote.Total, config.CurrencyIso);
            return Ok(new QuoteResponseDto(quote, methods));
        }

        /// <summary>
        /// Creates (or updates, same clientRequestId) the booking as "pending payment" and holds
        /// its seats and beds for PaymentHoldMinutes. When expectedTotal differs from the server
        /// total, nothing is held and 409 returns the new quote to show before paying. While the
        /// MyFatoorah key is not set, the booking is confirmed at once and stays unpaid: Rowad
        /// collects the payment (back-office shows it as confirmed, unpaid).
        /// </summary>
        [Authorize]
        [HttpPost("checkout")]
        public async Task<ActionResult<CheckoutResponseDto>> Checkout([FromBody] CheckoutRequest request)
        {
            var userId = _userManager.GetUserId(User);
            if (userId == null) return Unauthorized();

            var config = await _configStore.ReadAsync();
            var package = await FindPackageAsync(request.PackageId);
            if (package == null) return BadRequest(Problem("الباقة غير متاحة للحجز حالياً."));

            var booking = await _context.Bookings.Include(item => item.Travelers)
                .FirstOrDefaultAsync(item => item.UserId == userId && item.ClientRequestId == request.ClientRequestId);
            if (booking?.PaymentStatus == BookingPaymentStatuses.Paid || (booking?.Status == BookingStatuses.Confirmed && !_gateway.IsConfigured))
                return Ok(new CheckoutResponseDto(BookingsApiController.Map(await LoadBookingAsync(booking.Id)), null));

            // The server's copy of each traveler (documents already uploaded) wins over the draft.
            var ids = request.Travelers.Select(item => item.TravelerId ?? 0).ToList();
            if (ids.Any(id => id <= 0) || ids.Distinct().Count() != ids.Count)
                return BadRequest(Problem("بيانات المسافرين غير مكتملة. أعد رفع المستندات."));
            var stored = await _context.Travelers.AsNoTracking().Where(item => ids.Contains(item.Id) && !item.IsDeleted).ToDictionaryAsync(item => item.Id);
            if (stored.Count != ids.Count) return BadRequest(Problem("بعض المسافرين غير مسجلين. أعد إرسال بياناتهم."));
            var blocked = stored.Values.FirstOrDefault(item => item.IsBlocked);
            if (blocked != null) return Conflict(Problem($"لا يمكن إتمام الحجز للمسافر {blocked.FullName}. تواصل مع روّاد."));
            foreach (var traveler in request.Travelers) Merge(traveler, stored[traveler.TravelerId!.Value], config);

            await _flow.ReleaseExpiredHoldsAsync();
            var quote = BookingQuote.Calculate(config, package, request, PackageDepartures.TodayInKuwait(), requireStays: true);
            quote = await AddSeatIssuesAsync(quote, config, package, request, booking?.Id);
            var duplicate = await DuplicatePassportAsync(package.Id, request.DepartureDate, ids, booking?.Id);
            if (duplicate != null) quote = quote with { Valid = false, Issues = quote.Issues.Append(new QuoteIssue("duplicate-booking", "error", duplicate)).ToList() };
            if (!quote.Valid) return BadRequest(new CheckoutResponseDto(null, quote));
            if (request.ExpectedTotal.HasValue && request.ExpectedTotal.Value != quote.Total)
                return Conflict(new CheckoutResponseDto(null, quote with
                {
                    Issues = quote.Issues.Append(new QuoteIssue("price-changed", "notice", $"تغيّر الإجمالي إلى {BookingQuote.Amount(quote.Total)} {config.Currency}. راجعه قبل الدفع.")).ToList()
                }));

            var now = DateTime.UtcNow;
            if (booking == null)
            {
                booking = new Booking { Reference = await NewReferenceAsync(now), UserId = userId, ClientRequestId = request.ClientRequestId, CreatedAt = now };
                _context.Bookings.Add(booking);
            }
            else
            {
                _context.BookingTravelers.RemoveRange(booking.Travelers);
                booking.Travelers.Clear();
            }

            var stays = request.Stays.Select(stay => new DraftStay
            {
                City = stay.City,
                HotelId = stay.HotelId,
                Rooms = request.FamilyMode ? stay.Rooms.Where(room => room.Quantity > 0).ToList() : new List<DraftRoomQuantity>(),
                SharedBeds = request.FamilyMode ? null : stay.SharedBeds
            }).ToList();
            var makkah = stays.FirstOrDefault(stay => stay.City == BookingCities.Makkah);

            booking.PackageId = package.Id;
            booking.PackageName = package.Name;
            booking.DurationDays = package.DurationDays;
            booking.ImageUrl = package.ImageUrl;
            booking.DepartureDate = quote.DepartureDate;
            // The group's main nationality: the one most travelers' passports show.
            booking.Nationality = string.IsNullOrWhiteSpace(request.Nationality)
                ? quote.Travelers.GroupBy(item => item.Nationality).OrderByDescending(group => group.Count()).Select(group => group.Key).FirstOrDefault() ?? ""
                : request.Nationality.Trim();
            booking.Hotel = string.Join(" · ", stays.Select(stay => config.Hotel(stay.HotelId)?.Name).Where(name => name != null));
            booking.RoomType = makkah == null ? null : RoomSummary(makkah, request.FamilyMode);
            booking.Transport = "باص";
            booking.HasExistingVisa = request.Travelers.All(item => item.VisaOption == BookingQuote.ExistingVisa);
            booking.VisaIssuer = config.VisaType(booking.Nationality);
            booking.Men = quote.Men;
            booking.Women = quote.Women;
            booking.Adults = quote.Men + quote.Women;
            booking.Children = quote.Children;
            booking.Infants = quote.Infants;
            booking.FamilyMode = request.FamilyMode;
            booking.StaysJson = JsonSerializer.Serialize(stays, JsonOptions);
            booking.PriceBreakdownJson = JsonSerializer.Serialize(quote.Lines, JsonOptions);
            booking.TotalAmount = quote.Total;
            booking.Currency = config.Currency;
            booking.Status = BookingStatuses.PendingPayment;
            booking.PaymentStatus = BookingPaymentStatuses.Unpaid;
            booking.HoldExpiresAt = now.AddMinutes(config.PaymentHoldMinutes);
            booking.LastPaymentError = null;
            booking.UpdatedAt = now;
            foreach (var quoted in quote.Travelers)
            {
                var traveler = request.Travelers[quoted.Index];
                booking.Travelers.Add(new BookingTraveler
                {
                    TravelerId = traveler.TravelerId!.Value,
                    Kind = BookingQuote.IsAdult(quoted.Kind) ? "adult" : quoted.Kind,
                    VisaOption = quoted.VisaOption,
                    Gender = quoted.Gender,
                    Nationality = quoted.Nationality
                });
            }
            await _context.SaveChangesAsync();

            var sleepers = quote.Travelers.Select(item => new AllocationTraveler(request.Travelers[item.Index].TravelerId!.Value, item.Kind, item.SleepGender ?? item.Gender ?? "male", item.Nationality)).ToList();
            var problem = await _flow.AllocateAsync(booking, sleepers, stays, config);
            if (problem != null)
            {
                booking.HoldExpiresAt = null;
                booking.Status = BookingStatuses.Expired;
                await _context.SaveChangesAsync();
                return Conflict(new CheckoutResponseDto(null, quote with
                {
                    Valid = false,
                    Issues = quote.Issues.Append(new QuoteIssue("room-full", "error", problem)).ToList()
                }));
            }

            if (!_gateway.IsConfigured)
            {
                // TODO: remove when the MyFatoorah key is set. The payment gateway is not configured
                // yet, so the booking is confirmed and marked paid in a clearly labelled TEST mode
                // (no money is collected); the back-office sees "دفع تجريبي". The real flow (pending
                // → MyFatoorah → paid, verified on the server) comes back on its own once the key is set.
                booking.Status = BookingStatuses.Confirmed;
                booking.PaymentStatus = BookingPaymentStatuses.Paid;
                booking.PaymentMethod = "دفع تجريبي (بانتظار تفعيل بوابة الدفع)";
                booking.PaidAt = now;
                booking.HoldExpiresAt = null;
                await _context.SaveChangesAsync();
            }

            return Ok(new CheckoutResponseDto(BookingsApiController.Map(await LoadBookingAsync(booking.Id)), quote));
        }

        /// <summary>
        /// Starts a MyFatoorah payment for a pending booking (amount from the server). A new call
        /// (other method, retry) first checks the previous invoice so a paid booking is never paid twice.
        /// </summary>
        [Authorize]
        [HttpPost("bookings/{reference}/pay")]
        public async Task<ActionResult<PayResponseDto>> Pay(string reference, [FromBody] PayRequest request)
        {
            var userId = _userManager.GetUserId(User);
            var booking = await _context.Bookings.FirstOrDefaultAsync(item => item.Reference == reference && item.UserId == userId);
            if (booking == null) return NotFound();
            if (!_gateway.IsConfigured) return StatusCode(503, Problem("الدفع الإلكتروني غير مفعّل بعد. تواصل مع روّاد لإتمام الحجز."));

            if (booking.PaymentStatus != BookingPaymentStatuses.Paid && !string.IsNullOrEmpty(booking.PaymentInvoiceId))
                await RefreshFromGatewayAsync(booking);
            if (booking.PaymentStatus == BookingPaymentStatuses.Paid) return Ok(new PayResponseDto(null, BookingsApiController.Map(await LoadBookingAsync(booking.Id))));
            if (booking.Status != BookingStatuses.PendingPayment || booking.HoldExpiresAt == null || booking.HoldExpiresAt <= DateTime.UtcNow)
                return Conflict(Problem("انتهت مهلة حجز المقاعد. أكّد الحجز من جديد.", "hold-expired"));

            var config = await _configStore.ReadAsync();
            var user = await _userManager.GetUserAsync(User);
            booking.PaymentReturnUrl = SafeReturnUrl(request.ReturnUrl);
            booking.HoldExpiresAt = DateTime.UtcNow.AddMinutes(config.PaymentHoldMinutes);
            var callback = $"{PublicBaseUrl()}/api/booking-flow/payment-return?reference={Uri.EscapeDataString(booking.Reference)}";
            try
            {
                var result = await _gateway.ExecutePaymentAsync(new GatewayExecuteRequest(
                    request.PaymentMethodId,
                    booking.TotalAmount,
                    config.CurrencyIso,
                    booking.Reference,
                    string.IsNullOrWhiteSpace(user?.FullName) ? "Rowad" : user!.FullName,
                    user?.Email,
                    user?.PhoneNumber,
                    callback + "&outcome=success",
                    callback + "&outcome=error"));
                booking.PaymentInvoiceId = result.InvoiceId;
                booking.PaymentMethod = request.PaymentMethodName;
                booking.LastPaymentError = null;
                booking.UpdatedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();
                return Ok(new PayResponseDto(result.PaymentUrl, BookingsApiController.Map(await LoadBookingAsync(booking.Id))));
            }
            catch (PaymentGatewayException exception)
            {
                await _context.SaveChangesAsync();
                return StatusCode(502, Problem(exception.Message));
            }
        }

        /// <summary>
        /// TEST payment (no money) for an existing unpaid booking, from the trip page, so the
        /// "paid" display can be checked while MyFatoorah is not configured. Works only when the
        /// gateway is off; the back-office sees "دفع تجريبي".
        /// TODO: remove once the MyFatoorah key is set (the real pay flow takes over).
        /// </summary>
        [Authorize]
        [HttpPost("bookings/{reference}/test-pay")]
        public async Task<ActionResult<PaymentStatusDto>> TestPay(string reference)
        {
            if (_gateway.IsConfigured) return BadRequest(Problem("الدفع الإلكتروني مفعّل؛ استخدم الدفع الحقيقي."));
            var userId = _userManager.GetUserId(User);
            var booking = await _context.Bookings.FirstOrDefaultAsync(item => item.Reference == reference && item.UserId == userId);
            if (booking == null) return NotFound();
            if (booking.Status == BookingStatuses.Cancelled) return Conflict(Problem("هذا الحجز ملغى."));
            booking.Status = BookingStatuses.Confirmed;
            booking.PaymentStatus = BookingPaymentStatuses.Paid;
            booking.PaymentMethod = "دفع تجريبي (بانتظار تفعيل بوابة الدفع)";
            booking.PaidAt = DateTime.UtcNow;
            booking.HoldExpiresAt = null;
            booking.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
            return Ok(new PaymentStatusDto("paid", null, BookingsApiController.Map(await LoadBookingAsync(booking.Id))));
        }

        /// <summary>Status after the payment page: verified with MyFatoorah when still unpaid.</summary>
        [Authorize]
        [HttpGet("bookings/{reference}/payment-status")]
        public async Task<ActionResult<PaymentStatusDto>> PaymentStatus(string reference)
        {
            var userId = _userManager.GetUserId(User);
            var booking = await _context.Bookings.FirstOrDefaultAsync(item => item.Reference == reference && item.UserId == userId);
            if (booking == null) return NotFound();
            if (booking.PaymentStatus != BookingPaymentStatuses.Paid && !string.IsNullOrEmpty(booking.PaymentInvoiceId))
                await RefreshFromGatewayAsync(booking);
            var dto = BookingsApiController.Map(await LoadBookingAsync(booking.Id));
            return Ok(new PaymentStatusDto(booking.PaymentStatus == BookingPaymentStatuses.Paid ? "paid" : booking.LastPaymentError != null ? "failed" : "pending", booking.LastPaymentError, dto));
        }

        /// <summary>
        /// Where the traveler sleeps: hotel and room type, the room number once the rooming list
        /// freezes, and for a shared room «سريرك في غرفة رجال · ٢ من نفس جنسيتك». The other
        /// occupants are counted, never named.
        /// </summary>
        [Authorize]
        [HttpGet("bookings/{reference}/rooms")]
        public async Task<ActionResult<IReadOnlyList<StayRoomsDto>>> Rooms(string reference)
        {
            var userId = _userManager.GetUserId(User);
            var booking = await _context.Bookings.AsNoTracking().FirstOrDefaultAsync(item => item.Reference == reference && item.UserId == userId);
            if (booking == null) return NotFound();
            var config = await _configStore.ReadAsync();
            var frozen = booking.DepartureDate != null && BookingFlowService.IsFrozen(booking.DepartureDate, config);
            var mine = await _context.RoomAssignments.AsNoTracking().Where(item => item.BookingId == booking.Id).ToListAsync();
            var names = await _context.BookingTravelers.AsNoTracking().Where(link => link.BookingId == booking.Id)
                .Select(link => new { link.TravelerId, link.Traveler.FullName }).ToDictionaryAsync(link => link.TravelerId, link => link.FullName);
            // Others are counted, never named.

            var result = new List<StayRoomsDto>();
            foreach (var group in mine.GroupBy(item => new { item.City, item.HotelId }))
            {
                var hotel = config.Hotel(group.Key.HotelId);
                var rooms = new List<RoomInfoDto>();
                foreach (var room in group.GroupBy(item => new { item.RoomType, item.RoomNumber, item.IsFamily }))
                {
                    string? summary = null;
                    var sameNationality = 0;
                    if (!room.Key.IsFamily)
                    {
                        var others = await _context.RoomAssignments.AsNoTracking()
                            .Where(item => item.PackageId == booking.PackageId && item.DepartureDate == booking.DepartureDate && item.City == group.Key.City &&
                                item.HotelId == group.Key.HotelId && item.RoomType == room.Key.RoomType && item.RoomNumber == room.Key.RoomNumber && item.BookingId != booking.Id)
                            .Select(item => item.Nationality)
                            .ToListAsync();
                        var mineNationalities = room.Select(item => item.Nationality).ToHashSet();
                        sameNationality = others.Count(mineNationalities.Contains);
                        var side = room.First().Gender == "female" ? "نساء" : "رجال";
                        summary = $"سريرك في غرفة {side}" + (sameNationality > 0 ? $" · {RoomLabels.ArabicNumber(sameNationality)} من نفس جنسيتك" : "");
                    }
                    rooms.Add(new RoomInfoDto(
                        room.Key.IsFamily ? RoomLabels.Of(room.Key.RoomType) : RoomLabels.SharedBed(room.Key.RoomType),
                        frozen ? room.Key.RoomNumber : null,
                        room.Select(item => names.GetValueOrDefault(item.TravelerId) ?? "").ToList(),
                        summary,
                        sameNationality));
                }
                result.Add(new StayRoomsDto(group.Key.City, BookingCities.Label(group.Key.City), hotel?.Name ?? group.Key.HotelId, frozen, rooms));
            }
            return Ok(result);
        }

        /// <summary>
        /// CallBackUrl / ErrorUrl of MyFatoorah. The paymentId is checked with GetPaymentStatus
        /// (never trusted as such), then the browser goes back to the app.
        /// </summary>
        [AllowAnonymous]
        [HttpGet("payment-return")]
        public async Task<IActionResult> PaymentReturn([FromQuery] string? reference, [FromQuery] string? paymentId, [FromQuery] string? outcome)
        {
            Booking? booking = null;
            var status = "failed";
            try
            {
                if (!string.IsNullOrEmpty(paymentId))
                {
                    var verified = await _gateway.GetPaymentStatusAsync(paymentId, "PaymentId");
                    booking = await _context.Bookings.FirstOrDefaultAsync(item => item.Reference == (verified.CustomerReference ?? reference) && item.PaymentInvoiceId == verified.InvoiceId);
                    if (booking != null)
                    {
                        await _flow.ApplyPaymentStatusAsync(booking, verified);
                        status = booking.PaymentStatus == BookingPaymentStatuses.Paid ? "paid" : verified.Status == "Canceled" ? "cancelled" : "failed";
                    }
                }
                booking ??= await _context.Bookings.AsNoTracking().FirstOrDefaultAsync(item => item.Reference == reference);
            }
            catch (PaymentGatewayException exception)
            {
                _logger.LogWarning("Payment return for {Reference}: {Message}", reference, exception.Message);
            }

            var target = booking?.PaymentReturnUrl;
            if (!string.IsNullOrEmpty(target))
            {
                var separator = target.Contains('?') ? '&' : '?';
                return Redirect($"{target}{separator}reference={Uri.EscapeDataString(booking!.Reference)}&status={status}");
            }
            var message = status == "paid" ? "تمّ الدفع بنجاح. عُد إلى تطبيق روّاد." : "لم يكتمل الدفع. عُد إلى تطبيق روّاد لإعادة المحاولة.";
            return Content($"<html lang=\"ar\" dir=\"rtl\"><meta name=\"viewport\" content=\"width=device-width\"><body style=\"font-family:Arial;text-align:center;padding:48px\"><h2>{WebUtility.HtmlEncode(message)}</h2></body></html>", "text/html; charset=utf-8");
        }

        /// <summary>
        /// MyFatoorah webhook (TransactionsStatusChanged). The payload only names the invoice: its
        /// status is read back from MyFatoorah, so a forged call cannot confirm a booking.
        /// </summary>
        [AllowAnonymous]
        [HttpPost("myfatoorah/webhook")]
        public async Task<IActionResult> Webhook([FromBody] JsonElement payload)
        {
            var data = payload.TryGetProperty("Data", out var value) ? value : payload;
            var invoiceId = data.TryGetProperty("InvoiceId", out var invoice) ? invoice.ToString() : null;
            if (string.IsNullOrEmpty(invoiceId)) return Ok();

            var booking = await _context.Bookings.FirstOrDefaultAsync(item => item.PaymentInvoiceId == invoiceId);
            if (booking == null || booking.PaymentStatus == BookingPaymentStatuses.Paid) return Ok();
            try
            {
                await _flow.ApplyPaymentStatusAsync(booking, await _gateway.GetPaymentStatusAsync(invoiceId, "InvoiceId"));
            }
            catch (PaymentGatewayException exception)
            {
                _logger.LogWarning("Webhook for invoice {Invoice}: {Message}", invoiceId, exception.Message);
                return StatusCode(502);
            }
            return Ok();
        }

        /// <summary>Development only: the simulated payment page (success, failure, cancel).</summary>
        [AllowAnonymous]
        [HttpGet("simulated-gateway")]
        public IActionResult SimulatedGateway([FromQuery] string invoiceId)
        {
            var invoice = _gateway is SimulatedPaymentGateway ? SimulatedPaymentGateway.Find(invoiceId) : null;
            if (invoice == null) return NotFound();
            string Button(string outcome, string label, string color) =>
                $"<form method=\"post\" action=\"/api/booking-flow/simulated-gateway/{invoice.InvoiceId}/{outcome}\"><button style=\"width:100%;padding:16px;margin:8px 0;border:0;border-radius:14px;background:{color};color:#fff;font-size:17px\">{label}</button></form>";
            var html = $"<html lang=\"ar\" dir=\"rtl\"><meta name=\"viewport\" content=\"width=device-width\"><body style=\"font-family:Arial;padding:24px;background:#F7F3EC\">" +
                $"<p style=\"color:#8A6526\">بوابة دفع تجريبية (وضع التطوير)</p><h2>{WebUtility.HtmlEncode(invoice.Method)} · {BookingQuote.Amount(invoice.Amount)} د.ك</h2><p>الحجز {WebUtility.HtmlEncode(invoice.Reference)}</p>" +
                Button("success", "دفع ناجح", "#2F6B4F") + Button("failure", "رفض البطاقة", "#B5432A") + Button("cancel", "إلغاء", "#6B655B") + "</body></html>";
            return Content(html, "text/html; charset=utf-8");
        }

        [AllowAnonymous]
        [HttpPost("simulated-gateway/{invoiceId}/{outcome}")]
        public IActionResult SimulatedGatewayResult(string invoiceId, string outcome)
        {
            var invoice = _gateway is SimulatedPaymentGateway ? SimulatedPaymentGateway.Find(invoiceId) : null;
            if (invoice == null) return NotFound();
            invoice.PaymentId = $"{invoiceId}{RandomNumberGenerator.GetInt32(100, 1000)}";
            invoice.Status = outcome switch { "success" => "Paid", "cancel" => "Canceled", _ => "Failed" };
            invoice.Error = outcome == "failure" ? "رُفضت البطاقة." : null;
            var url = outcome == "success" ? invoice.CallBackUrl : invoice.ErrorUrl;
            return Redirect($"{url}&paymentId={invoice.PaymentId}");
        }

        private async Task RefreshFromGatewayAsync(Booking booking)
        {
            try
            {
                await _flow.ApplyPaymentStatusAsync(booking, await _gateway.GetPaymentStatusAsync(booking.PaymentInvoiceId!, "InvoiceId"));
            }
            catch (PaymentGatewayException exception)
            {
                _logger.LogWarning("Payment status of {Reference}: {Message}", booking.Reference, exception.Message);
            }
        }

        private async Task<IReadOnlyList<PaymentMethodDto>> PaymentMethodsAsync(decimal total, string currencyIso)
        {
            if (!_gateway.IsConfigured) return Array.Empty<PaymentMethodDto>();
            try
            {
                var methods = await _gateway.InitiatePaymentAsync(total, currencyIso);
                return methods.Where(method => !method.Direct)
                    .Select(method => new PaymentMethodDto(method.Id, method.Code, method.NameAr, method.NameEn, method.ImageUrl, method.TotalAmount))
                    .ToList();
            }
            catch (PaymentGatewayException exception)
            {
                _logger.LogWarning("InitiatePayment: {Message}", exception.Message);
                return Array.Empty<PaymentMethodDto>();
            }
        }

        private async Task<BookingQuoteResult> AddSeatIssuesAsync(BookingQuoteResult quote, BookingConfig config, TravelPackageDto package, BookingDraftRequest draft, int? exceptBookingId)
        {
            var pack = config.Pack(package.Id);
            if (pack == null || string.IsNullOrEmpty(quote.DepartureDate)) return quote;
            var taken = (await _flow.SeatsTakenAsync(package.Id, new[] { quote.DepartureDate }, exceptBookingId)).GetValueOrDefault(quote.DepartureDate);
            var left = Math.Max(0, pack.SeatsPerDeparture - taken);
            var seats = quote.Men + quote.Women + quote.Children;
            if (seats <= left) return quote;
            var message = left == 0 ? "اكتمل هذا الموعد. اختر موعداً آخر." : $"بقي {left} من المقاعد فقط في هذا الموعد.";
            return quote with { Valid = false, Issues = quote.Issues.Append(new QuoteIssue("seats", "error", message)).ToList() };
        }

        /// <summary>Same passport already on another active booking of this departure.</summary>
        private async Task<string?> DuplicatePassportAsync(string packageId, string? departure, IReadOnlyList<int> travelerIds, int? exceptBookingId)
        {
            var now = DateTime.UtcNow;
            var holding = BookingFlowService.Holding(_context.Bookings, now)
                .Where(booking => booking.PackageId == packageId && booking.DepartureDate == departure && booking.Id != (exceptBookingId ?? 0))
                .Select(booking => booking.Id);
            var name = await _context.BookingTravelers.AsNoTracking()
                .Where(link => travelerIds.Contains(link.TravelerId) && holding.Contains(link.BookingId))
                .Select(link => link.Traveler.FullName)
                .FirstOrDefaultAsync();
            return name == null ? null : $"المسافر {name} محجوز مسبقاً على هذا الموعد.";
        }

        /// <summary>Passport data from the server's traveler record replaces the draft's copy.</summary>
        private static void Merge(DraftTraveler traveler, Traveler stored, BookingConfig config)
        {
            if (stored.DateOfBirth > new DateTime(1900, 1, 1)) traveler.BirthDate = stored.DateOfBirth.ToString("yyyy-MM-dd");
            if (stored.PassportExpiryDate.HasValue) traveler.PassportExpiry = stored.PassportExpiryDate.Value.ToString("yyyy-MM-dd");
            traveler.PassportNumber = stored.PassportNumber;
            var gender = NormalizeGender(stored.Gender);
            if (gender != null) traveler.Gender = gender;
            if (config.VisaType(stored.Nationality) != null) traveler.Nationality = stored.Nationality.Trim();
        }

        public static string? NormalizeGender(string? value)
        {
            var text = (value ?? "").Trim().ToLowerInvariant();
            if (text is "m" or "male" or "ذكر" or "رجل") return "male";
            if (text is "f" or "female" or "أنثى" or "انثى" or "امرأة") return "female";
            return null;
        }

        private static string RoomSummary(DraftStay stay, bool family) => family
            ? string.Join(" + ", stay.Rooms.Where(room => room.Quantity > 0).Select(room => $"{RoomLabels.Of(room.Type)} × {room.Quantity}"))
            : string.Join(" + ", new[] { stay.SharedBeds?.Men, stay.SharedBeds?.Women }.Where(type => type != null).Distinct().Select(type => RoomLabels.SharedBed(type!)));

        private static HotelDto MapHotel(HotelConfig hotel, int nights, Dictionary<(string HotelId, string RoomType), int>? roomsTaken) => new(
            hotel.Id,
            hotel.Name,
            hotel.City,
            nights,
            hotel.Stars,
            hotel.District,
            hotel.WalkMinutes,
            hotel.Description,
            hotel.Amenities,
            hotel.Photos,
            hotel.Latitude,
            hotel.Longitude,
            hotel.Placeholder,
            hotel.SupplementPerPersonPerNight * nights,
            hotel.Rooms.Select(room => new RoomTypeDto(
                room.Type,
                RoomLabels.Of(room.Type),
                room.Beds,
                room.FamilySupplementPerRoomPerNight * nights,
                room.SharedSupplementPerBedPerNight * nights,
                roomsTaken == null ? null : Math.Max(0, room.RoomsPerDeparture - roomsTaken.GetValueOrDefault((hotel.Id, room.Type))))).ToList());

        private string PublicBaseUrl() => string.IsNullOrWhiteSpace(_paymentOptions.PublicBaseUrl)
            ? $"{Request.Scheme}://{Request.Host}"
            : _paymentOptions.PublicBaseUrl.TrimEnd('/');

        /// <summary>Only the app's own links (installed app or Expo Go) may receive the result.</summary>
        private static string? SafeReturnUrl(string? url)
        {
            if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri)) return null;
            return uri.Scheme is "rowadumrah" or "exp" or "exps" ? url : null;
        }

        private async Task<Booking> LoadBookingAsync(int id) => await _context.Bookings.AsNoTracking()
            .Include(booking => booking.Travelers).ThenInclude(link => link.Traveler)
            .FirstAsync(booking => booking.Id == id);

        private async Task<string> NewReferenceAsync(DateTime now)
        {
            while (true)
            {
                var reference = $"RW-{now:yyMMdd}-{RandomNumberGenerator.GetInt32(1000, 10000)}";
                if (!await _context.Bookings.AnyAsync(booking => booking.Reference == reference)) return reference;
            }
        }

        private async Task<TravelPackageDto?> FindPackageAsync(string packageId)
        {
            if (!System.IO.File.Exists(_packagesPath)) return null;
            await using var stream = System.IO.File.OpenRead(_packagesPath);
            var packages = await JsonSerializer.DeserializeAsync<List<TravelPackageDto>>(stream, JsonOptions);
            return packages?.FirstOrDefault(package => package.Id == packageId && package.Status == "published");
        }

        private static ProblemDetails Problem(string message, string? code = null) => new() { Title = message, Detail = message, Type = code };
    }

    public sealed class CheckoutRequest : BookingDraftRequest
    {
        [Required, StringLength(64)] public string ClientRequestId { get; set; } = "";
        public decimal? ExpectedTotal { get; set; }
    }

    public sealed class PayRequest
    {
        [Range(1, int.MaxValue)] public int PaymentMethodId { get; set; }
        [StringLength(60)] public string? PaymentMethodName { get; set; }
        [StringLength(300)] public string? ReturnUrl { get; set; }
    }

    public sealed record PackSummaryDto(string Id, string Name, int DurationDays, int MakkahNights, int MadinahNights, IReadOnlyList<int> DepartureWeekdays, string Currency, string? ImageUrl);
    public sealed record DepartureDto(string Date, string ReturnDate, int Weekday, int SeatsLeft, string Status);
    public sealed record NationalityDto(string Name, string VisaType);
    public sealed record RoomTypeDto(string Type, string Label, int Beds, decimal FamilySupplementPerRoom, decimal SharedSupplementPerBed, int? RoomsLeft);
    public sealed record HotelDto(string Id, string Name, string City, int Nights, int? Stars, string? District, int? WalkMinutes, string? Description,
        IReadOnlyList<string> Amenities, IReadOnlyList<string> Photos, double? Latitude, double? Longitude, bool Placeholder, decimal SupplementPerPerson, IReadOnlyList<RoomTypeDto> Rooms);
    public sealed record BookingRulesDto(int FewSeatsThreshold, int MinPassportValidityMonths, double ExtractionConfidenceThreshold, int PaymentHoldMinutes, int DraftExpiryDays,
        bool CivilIdRequired, int CivilIdMinAge, bool PersonalPhotoRequired);
    public sealed record BookingOptionsDto(PackSummaryDto Pack, IReadOnlyList<DepartureDto> Departures, IReadOnlyList<NationalityDto> Nationalities,
        IReadOnlyList<string> FrequentNationalities, IReadOnlyDictionary<string, List<HotelDto>> Hotels, BookingRulesDto Rules, bool PaymentAvailable);
    public sealed record PaymentMethodDto(int Id, string Code, string NameAr, string NameEn, string? ImageUrl, decimal TotalAmount);
    public sealed record QuoteResponseDto(BookingQuoteResult Quote, IReadOnlyList<PaymentMethodDto> PaymentMethods);
    public sealed record CheckoutResponseDto(BookingDto? Booking, BookingQuoteResult? Quote);
    public sealed record PayResponseDto(string? PaymentUrl, BookingDto Booking);
    public sealed record RoomInfoDto(string Label, int? RoomNumber, IReadOnlyList<string> Travelers, string? Summary, int SameNationality);
    public sealed record SavedPassportDto(string FullName, string PassportNumber, string? PassportExpiry, string? BirthDate, string Nationality, string? Gender, string? CivilId);
    public sealed record StayRoomsDto(string City, string CityLabel, string Hotel, bool Frozen, IReadOnlyList<RoomInfoDto> Rooms);
    public sealed record PaymentStatusDto(string Status, string? Error, BookingDto Booking);
}
