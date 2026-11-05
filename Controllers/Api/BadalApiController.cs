using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RowadUmrahSystem.Web.Data;
using RowadUmrahSystem.Web.Models;
using RowadUmrahSystem.Web.Services.Badal;
using RowadUmrahSystem.Web.Services.BookingFlow;

namespace RowadUmrahSystem.Web.Controllers.Api
{
    /// <summary>
    /// «عمرة البدل» for the app: an omra ordered for a deceased or lastingly unable person and
    /// performed by a verified pilgrim.
    ///
    /// Two promises are kept here and not on the phone:
    ///  - the ELIGIBILITY rule is checked again (422 for a living, able person), so a modified or
    ///    outdated client can never place an order the app would have refused;
    ///  - the PRICE is read from the back-office configuration; the request body carries none,
    ///    and the MyFatoorah key never leaves this server.
    /// </summary>
    [ApiController]
    [Route("api/badal")]
    public class BadalApiController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly BadalConfigStore _configStore;
        private readonly IPaymentGateway _gateway;
        private readonly MyFatoorahOptions _paymentOptions;
        private readonly ILogger<BadalApiController> _logger;

        public BadalApiController(
            ApplicationDbContext context,
            UserManager<ApplicationUser> userManager,
            BadalConfigStore configStore,
            IPaymentGateway gateway,
            IOptions<MyFatoorahOptions> paymentOptions,
            ILogger<BadalApiController> logger)
        {
            _context = context;
            _userManager = userManager;
            _configStore = configStore;
            _gateway = gateway;
            _paymentOptions = paymentOptions.Value;
            _logger = logger;
        }

        // --- DTOs -------------------------------------------------------------------------------

        public sealed record BadalConfigDto(bool Enabled, decimal? Price, string CurrencyIso, string DelayLabel, int SlaDays);
        public sealed record BadalIssueDto(string Field, string Code);
        public sealed record BadalPerformerDto(string FirstName, string City, string[] Languages, double Rating, int BadalCount);
        public sealed record BadalOrderDto(
            string Reference, string Status, string PaymentStatus,
            string BeneficiaryFullName, string BeneficiaryGender, string BeneficiaryStatus, string? BeneficiaryRelation,
            string? Honorific, string NiyyaText, string? PersonalDua,
            string TimingType, string? TimingDate,
            decimal Price, string CurrencyIso,
            DateTime? PaidAt, DateTime? AssignedAt, DateTime? ScheduledAt, DateTime? CompletedAt,
            string? SlaDeadline, string? CertificateNumber, BadalPerformerDto? Performer);
        public sealed record PayRequestDto(int PaymentMethodId, string? PaymentMethodName, string? ReturnUrl);
        public sealed record PayResponseDto(string? PaymentUrl, BadalOrderDto Order);
        public sealed record CreateResponseDto(BadalOrderDto Order, IReadOnlyList<GatewayPaymentMethod> PaymentMethods);

        // --- Settings ---------------------------------------------------------------------------

        /// <summary>
        /// What the presentation screen needs. While the service is off, or no price is set, the
        /// app shows no price at all rather than a placeholder.
        /// </summary>
        [HttpGet("config")]
        public async Task<ActionResult<BadalConfigDto>> Config()
        {
            var config = await _configStore.ReadAsync();
            var available = config.Enabled && config.Price > 0;
            return Ok(new BadalConfigDto(
                available,
                available ? config.Price : null,
                config.CurrencyIso,
                config.DelayLabel,
                config.SlaDays));
        }

        // --- Orders -----------------------------------------------------------------------------

        [Authorize]
        [HttpPost("orders")]
        public async Task<ActionResult<CreateResponseDto>> Create([FromBody] BadalOrderRequest request)
        {
            var userId = _userManager.GetUserId(User);
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            var config = await _configStore.ReadAsync();
            if (!config.Enabled || config.Price <= 0)
                return StatusCode(503, Fail("خدمة عمرة البدل غير متاحة حالياً.", "service-off"));

            // The rule, checked again on the server. A living, able person is refused here too.
            var issues = BadalRules.Validate(request);
            if (issues.Count > 0)
            {
                return UnprocessableEntity(new
                {
                    message = issues.Any(issue => issue.Code == "capable_not_allowed")
                        ? "لا تصحّ عمرة البدل عن حيٍّ قادر على أدائها بنفسه."
                        : "تحقّق من بيانات الطلب.",
                    issues = issues.Select(issue => new BadalIssueDto(issue.Field, issue.Code)).ToList()
                });
            }

            // A retried request never creates a second order.
            if (!string.IsNullOrWhiteSpace(request.ClientRequestId))
            {
                var existing = await Query().FirstOrDefaultAsync(order =>
                    order.UserId == userId && order.ClientRequestId == request.ClientRequestId);
                if (existing != null) return Ok(new CreateResponseDto(Map(existing, config), await MethodsAsync(config)));
            }

            var beneficiary = request.Beneficiary!;
            var dua = string.IsNullOrWhiteSpace(request.PersonalDua) ? null : request.PersonalDua!.Trim();
            var order = new BadalOrder
            {
                Reference = "RW-B-TMP",
                UserId = userId,
                ClientRequestId = request.ClientRequestId,
                BeneficiaryFullName = beneficiary.FullName!.Trim(),
                BeneficiaryGender = beneficiary.Gender!,
                BeneficiaryStatus = beneficiary.Status!,
                BeneficiaryRelation = string.IsNullOrEmpty(beneficiary.Relation) ? null : beneficiary.Relation,
                PersonalDua = dua?.Length > BadalRules.DuaMax ? dua[..BadalRules.DuaMax] : dua,
                // A dua is never passed to the pilgrim before the back-office has read it.
                PersonalDuaApproved = false,
                TimingType = request.Timing!.Type!,
                TimingDate = request.Timing.Type == "date"
                    ? DateTime.SpecifyKind(DateTime.Parse(request.Timing.Date!), DateTimeKind.Utc)
                    : null,
                DeclarationAcceptedAt = DateTime.UtcNow,
                Price = config.Price,
                CurrencyIso = config.CurrencyIso,
                Status = BadalStatuses.PendingPayment
            };

            _context.BadalOrders.Add(order);
            await _context.SaveChangesAsync();
            order.Reference = $"RW-B-{1000 + order.Id}";
            await _context.SaveChangesAsync();

            _logger.LogInformation("Badal order {Reference} created for user {UserId}", order.Reference, userId);
            return Ok(new CreateResponseDto(Map(order, config), await MethodsAsync(config)));
        }

        [Authorize]
        [HttpGet("orders/{reference}")]
        public async Task<ActionResult<BadalOrderDto>> Get(string reference)
        {
            var userId = _userManager.GetUserId(User);
            var order = await Query().FirstOrDefaultAsync(item => item.Reference == reference && item.UserId == userId);
            if (order == null) return NotFound();
            return Ok(Map(order, await _configStore.ReadAsync()));
        }

        [Authorize]
        [HttpGet("orders")]
        public async Task<ActionResult<IReadOnlyList<BadalOrderDto>>> Mine()
        {
            var userId = _userManager.GetUserId(User);
            var config = await _configStore.ReadAsync();
            var orders = await Query().Where(item => item.UserId == userId)
                .OrderByDescending(item => item.CreatedAt).Take(50).ToListAsync();
            return Ok(orders.Select(order => Map(order, config)).ToList());
        }

        // --- Payment ----------------------------------------------------------------------------

        [Authorize]
        [HttpPost("orders/{reference}/pay")]
        public async Task<ActionResult<PayResponseDto>> Pay(string reference, [FromBody] PayRequestDto request)
        {
            var userId = _userManager.GetUserId(User);
            var order = await Query().FirstOrDefaultAsync(item => item.Reference == reference && item.UserId == userId);
            if (order == null) return NotFound();

            var config = await _configStore.ReadAsync();
            if (!_gateway.IsConfigured)
                return StatusCode(503, Fail("الدفع الإلكتروني غير مفعّل بعد. تواصل مع روّاد لإتمام الطلب.", "gateway-off"));
            if (order.PaymentStatus == "paid") return Ok(new PayResponseDto(null, Map(order, config)));
            if (order.Status != BadalStatuses.PendingPayment)
                return Conflict(Fail("هذا الطلب لم يعد بانتظار الدفع.", "not-pending"));

            var user = await _userManager.GetUserAsync(User);
            order.PaymentReturnUrl = SafeReturnUrl(request.ReturnUrl);
            var callback = $"{PublicBaseUrl()}/api/badal/payment-return?reference={Uri.EscapeDataString(order.Reference)}";
            try
            {
                var result = await _gateway.ExecutePaymentAsync(new GatewayExecuteRequest(
                    request.PaymentMethodId,
                    order.Price,
                    order.CurrencyIso,
                    order.Reference,
                    string.IsNullOrWhiteSpace(user?.FullName) ? "Rowad" : user!.FullName,
                    user?.Email,
                    user?.PhoneNumber,
                    callback + "&outcome=success",
                    callback + "&outcome=error"));
                order.PaymentInvoiceId = result.InvoiceId;
                order.PaymentMethod = request.PaymentMethodName;
                order.LastPaymentError = null;
                order.UpdatedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();
                return Ok(new PayResponseDto(result.PaymentUrl, Map(order, config)));
            }
            catch (PaymentGatewayException exception)
            {
                order.LastPaymentError = exception.Message;
                await _context.SaveChangesAsync();
                return StatusCode(502, Fail(exception.Message, "gateway-error"));
            }
        }

        /// <summary>MyFatoorah sends the payer back here; the state is then read from the gateway.</summary>
        [HttpGet("payment-return")]
        public async Task<IActionResult> PaymentReturn(string reference, string? paymentId, string? outcome)
        {
            var order = await _context.BadalOrders.FirstOrDefaultAsync(item => item.Reference == reference);
            if (order != null && !string.IsNullOrEmpty(paymentId))
            {
                await ApplyStatusAsync(order, await _gateway.GetPaymentStatusAsync(paymentId, "PaymentId"));
            }
            var target = order?.PaymentReturnUrl;
            if (string.IsNullOrEmpty(target)) return Ok(new { reference, outcome, status = order?.PaymentStatus });
            var separator = target.Contains('?') ? "&" : "?";
            return Redirect($"{target}{separator}reference={Uri.EscapeDataString(reference)}&outcome={Uri.EscapeDataString(outcome ?? "")}");
        }

        [HttpPost("myfatoorah/webhook")]
        public async Task<IActionResult> Webhook([FromQuery] string? invoiceId, [FromQuery] string? reference)
        {
            if (string.IsNullOrEmpty(invoiceId) && string.IsNullOrEmpty(reference)) return BadRequest();
            var order = await _context.BadalOrders.FirstOrDefaultAsync(item =>
                (reference != null && item.Reference == reference) ||
                (invoiceId != null && item.PaymentInvoiceId == invoiceId));
            if (order == null) return NotFound();
            await ApplyStatusAsync(order, await _gateway.GetPaymentStatusAsync(order.PaymentInvoiceId ?? invoiceId!, "InvoiceId"));
            return Ok();
        }

        // --- Helpers ----------------------------------------------------------------------------

        private IQueryable<BadalOrder> Query() => _context.BadalOrders.Include(order => order.Performer);

        private async Task<IReadOnlyList<GatewayPaymentMethod>> MethodsAsync(BadalConfig config)
        {
            if (!_gateway.IsConfigured) return Array.Empty<GatewayPaymentMethod>();
            try
            {
                return await _gateway.InitiatePaymentAsync(config.Price, config.CurrencyIso);
            }
            catch (PaymentGatewayException exception)
            {
                _logger.LogWarning(exception, "Badal: payment methods unavailable");
                return Array.Empty<GatewayPaymentMethod>();
            }
        }

        /// <summary>Applies what the gateway says; paid moves the order into the assignment queue.</summary>
        private async Task ApplyStatusAsync(BadalOrder order, GatewayPaymentStatus status)
        {
            if (status.Status == "Paid")
            {
                order.PaymentStatus = "paid";
                order.PaymentId = status.PaymentId;
                order.PaymentMethod = status.PaymentMethod ?? order.PaymentMethod;
                order.PaidAt ??= DateTime.UtcNow;
                if (order.Status == BadalStatuses.PendingPayment) order.Status = BadalStatuses.Paid;
                order.LastPaymentError = null;
            }
            else if (status.Status is "Failed" or "Canceled")
            {
                order.PaymentStatus = "unpaid";
                order.LastPaymentError = status.Error;
            }
            order.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
        }

        private BadalOrderDto Map(BadalOrder order, BadalConfig config) => new(
            order.Reference,
            order.Status,
            order.PaymentStatus,
            order.BeneficiaryFullName,
            order.BeneficiaryGender,
            order.BeneficiaryStatus,
            order.BeneficiaryRelation,
            BadalRules.Honorific(order.BeneficiaryStatus, order.BeneficiaryGender),
            BadalRules.NiyyaText(order.BeneficiaryFullName),
            // The dua only travels once the back-office has read it.
            order.PersonalDuaApproved ? order.PersonalDua : null,
            order.TimingType,
            order.TimingDate?.ToString("yyyy-MM-dd"),
            order.Price,
            order.CurrencyIso,
            order.PaidAt,
            order.AssignedAt,
            order.ScheduledAt,
            order.CompletedAt,
            BadalRules.SlaDeadline(order.AssignedAt, config.SlaDays)?.ToString("o"),
            order.CertificateNumber,
            // Never the pilgrim's full name: first name and city only.
            order.Performer == null ? null : new BadalPerformerDto(
                order.Performer.FirstName,
                order.Performer.City,
                order.Performer.Languages.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                order.Performer.Rating,
                order.Performer.BadalCount));

        private object Fail(string message, string? code = null) => new { message, code };

        private string PublicBaseUrl()
        {
            var configured = _paymentOptions.PublicBaseUrl;
            if (!string.IsNullOrWhiteSpace(configured)) return configured.TrimEnd('/');
            return $"{Request.Scheme}://{Request.Host}";
        }

        /// <summary>Only our own app scheme comes back, never an arbitrary redirect.</summary>
        private static string? SafeReturnUrl(string? url)
        {
            if (string.IsNullOrWhiteSpace(url)) return null;
            return url.StartsWith("rowadumrah://", StringComparison.OrdinalIgnoreCase)
                || url.StartsWith("ruwad://", StringComparison.OrdinalIgnoreCase)
                || url.StartsWith("rowad://", StringComparison.OrdinalIgnoreCase)
                ? url
                : null;
        }
    }
}
