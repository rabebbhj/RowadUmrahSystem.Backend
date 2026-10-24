using System.Collections.Concurrent;
using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RowadUmrahSystem.Web.Services.BookingFlow;

/// <summary>
/// MyFatoorah settings (section "MyFatoorah" of appsettings or environment variables, e.g.
/// MyFatoorah__ApiKey). The API key lives only on the server; the app never sees it.
/// BaseUrl: https://apitest.myfatoorah.com (test) or https://api.myfatoorah.com (Kuwait, live).
/// Simulate: development only, when no key is set — a local page stands in for the gateway.
/// </summary>
public sealed class MyFatoorahOptions
{
    public string BaseUrl { get; set; } = "https://apitest.myfatoorah.com";
    public string ApiKey { get; set; } = "";
    /// <summary>Public address of this server for the CallBackUrl / ErrorUrl (e.g. https://ruwadomra.com).</summary>
    public string PublicBaseUrl { get; set; } = "";
    public bool Simulate { get; set; }
}

public sealed record GatewayPaymentMethod(int Id, string Code, string NameAr, string NameEn, string? ImageUrl, decimal TotalAmount, bool Direct);

public sealed record GatewayExecuteRequest(
    int PaymentMethodId,
    decimal Amount,
    string CurrencyIso,
    string Reference,
    string CustomerName,
    string? CustomerEmail,
    string? CustomerMobile,
    string CallBackUrl,
    string ErrorUrl);

public sealed record GatewayExecuteResult(string InvoiceId, string PaymentUrl);

/// <summary>Invoice state as the gateway reports it. Status: Paid, Pending, Failed, Canceled.</summary>
public sealed record GatewayPaymentStatus(string InvoiceId, string Status, decimal InvoiceValue, string? CustomerReference, string? PaymentId, string? PaymentMethod, string? Error);

public interface IPaymentGateway
{
    bool IsConfigured { get; }
    Task<IReadOnlyList<GatewayPaymentMethod>> InitiatePaymentAsync(decimal amount, string currencyIso, CancellationToken cancellationToken = default);
    Task<GatewayExecuteResult> ExecutePaymentAsync(GatewayExecuteRequest request, CancellationToken cancellationToken = default);
    /// <summary>keyType: PaymentId or InvoiceId.</summary>
    Task<GatewayPaymentStatus> GetPaymentStatusAsync(string key, string keyType, CancellationToken cancellationToken = default);
}

public sealed class PaymentGatewayException(string message) : Exception(message);

/// <summary>MyFatoorah API v2: InitiatePayment, ExecutePayment, GetPaymentStatus.</summary>
public sealed class MyFatoorahGateway : IPaymentGateway
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = null, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
    private readonly HttpClient _http;
    private readonly MyFatoorahOptions _options;
    private readonly ILogger<MyFatoorahGateway> _logger;

    public MyFatoorahGateway(HttpClient http, Microsoft.Extensions.Options.IOptions<MyFatoorahOptions> options, ILogger<MyFatoorahGateway> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;
        _http.BaseAddress = new Uri(_options.BaseUrl.TrimEnd('/') + "/");
        _http.Timeout = TimeSpan.FromSeconds(30);
        if (!string.IsNullOrWhiteSpace(_options.ApiKey))
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey.Trim());
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_options.ApiKey);

    public async Task<IReadOnlyList<GatewayPaymentMethod>> InitiatePaymentAsync(decimal amount, string currencyIso, CancellationToken cancellationToken = default)
    {
        var data = await PostAsync("v2/InitiatePayment", new { InvoiceAmount = amount, CurrencyIso = currencyIso }, cancellationToken);
        var methods = new List<GatewayPaymentMethod>();
        if (data.TryGetProperty("PaymentMethods", out var list) && list.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in list.EnumerateArray())
            {
                methods.Add(new GatewayPaymentMethod(
                    item.GetProperty("PaymentMethodId").GetInt32(),
                    Text(item, "PaymentMethodCode") ?? "",
                    Text(item, "PaymentMethodAr") ?? Text(item, "PaymentMethodEn") ?? "",
                    Text(item, "PaymentMethodEn") ?? "",
                    Text(item, "ImageUrl"),
                    Number(item, "TotalAmount") ?? amount,
                    item.TryGetProperty("IsDirectPayment", out var direct) && direct.ValueKind == JsonValueKind.True));
            }
        }
        return methods;
    }

    public async Task<GatewayExecuteResult> ExecutePaymentAsync(GatewayExecuteRequest request, CancellationToken cancellationToken = default)
    {
        var data = await PostAsync("v2/ExecutePayment", new
        {
            request.PaymentMethodId,
            InvoiceValue = request.Amount,
            DisplayCurrencyIso = request.CurrencyIso,
            CustomerName = request.CustomerName,
            CustomerEmail = string.IsNullOrWhiteSpace(request.CustomerEmail) ? null : request.CustomerEmail,
            CustomerMobile = MobileDigits(request.CustomerMobile),
            CustomerReference = request.Reference,
            UserDefinedField = request.Reference,
            CallBackUrl = request.CallBackUrl,
            ErrorUrl = request.ErrorUrl,
            Language = "ar"
        }, cancellationToken);

        var invoiceId = data.TryGetProperty("InvoiceId", out var id) ? id.ToString() : "";
        var url = Text(data, "PaymentURL") ?? "";
        if (string.IsNullOrEmpty(invoiceId) || string.IsNullOrEmpty(url)) throw new PaymentGatewayException("لم تُرجع بوابة الدفع رابط الدفع.");
        return new GatewayExecuteResult(invoiceId, url);
    }

    public async Task<GatewayPaymentStatus> GetPaymentStatusAsync(string key, string keyType, CancellationToken cancellationToken = default)
    {
        var data = await PostAsync("v2/GetPaymentStatus", new { Key = key, KeyType = keyType }, cancellationToken);
        string? paymentId = null, method = null, error = null;
        if (data.TryGetProperty("InvoiceTransactions", out var transactions) && transactions.ValueKind == JsonValueKind.Array)
        {
            // The latest attempt decides what the traveler sees.
            var last = transactions.EnumerateArray().LastOrDefault();
            if (last.ValueKind == JsonValueKind.Object)
            {
                paymentId = Text(last, "PaymentId");
                method = Text(last, "PaymentGateway");
                error = Text(last, "Error");
            }
        }

        var status = Text(data, "InvoiceStatus") ?? "Pending";
        if (status == "Pending" && error != null) status = "Failed";
        return new GatewayPaymentStatus(
            data.TryGetProperty("InvoiceId", out var id) ? id.ToString() : key,
            status,
            Number(data, "InvoiceValue") ?? 0,
            Text(data, "CustomerReference"),
            paymentId,
            method,
            error);
    }

    private async Task<JsonElement> PostAsync(string path, object body, CancellationToken cancellationToken)
    {
        if (!IsConfigured) throw new PaymentGatewayException("الدفع الإلكتروني غير مفعّل بعد.");
        using var response = await _http.PostAsync(path, JsonContent(body), cancellationToken);
        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        JsonElement root;
        try
        {
            root = JsonDocument.Parse(text).RootElement.Clone();
        }
        catch (JsonException)
        {
            _logger.LogWarning("MyFatoorah {Path}: HTTP {Status}, unreadable body", path, (int)response.StatusCode);
            throw new PaymentGatewayException("تعذر الاتصال ببوابة الدفع. حاول مرة أخرى.");
        }

        var success = root.TryGetProperty("IsSuccess", out var ok) && ok.ValueKind == JsonValueKind.True;
        if (!response.IsSuccessStatusCode || !success)
        {
            var message = Text(root, "Message");
            _logger.LogWarning("MyFatoorah {Path}: HTTP {Status}, {Message}, {Errors}", path, (int)response.StatusCode, message,
                root.TryGetProperty("ValidationErrors", out var errors) ? errors.ToString() : "");
            throw new PaymentGatewayException("رفضت بوابة الدفع الطلب. حاول مرة أخرى أو اختر طريقة أخرى.");
        }
        return root.TryGetProperty("Data", out var data) ? data : default;
    }

    private static StringContent JsonContent(object body) =>
        new(JsonSerializer.Serialize(body, JsonOptions), System.Text.Encoding.UTF8, "application/json");

    private static string? Text(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null ? value.ToString() : null;

    private static decimal? Number(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value)
            ? value.ValueKind == JsonValueKind.Number ? value.GetDecimal()
            : decimal.TryParse(value.ToString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed) ? parsed : null
            : null;

    /// <summary>MyFatoorah takes the local number without country code (max 11 digits).</summary>
    private static string? MobileDigits(string? phone)
    {
        var digits = new string((phone ?? "").Where(char.IsDigit).ToArray());
        if (digits.StartsWith("965") && digits.Length > 8) digits = digits[3..];
        return digits.Length is >= 7 and <= 11 ? digits : null;
    }
}

/// <summary>
/// Development stand-in for MyFatoorah (no key needed): invoices live in memory and the payment
/// page is /api/booking-flow/simulated-gateway, where the tester picks success, failure or cancel.
/// </summary>
public sealed class SimulatedPaymentGateway : IPaymentGateway
{
    private static readonly ConcurrentDictionary<string, SimulatedInvoice> Invoices = new();
    private static int _sequence = 900000;

    public sealed class SimulatedInvoice
    {
        public string InvoiceId { get; init; } = "";
        public decimal Amount { get; init; }
        public string Reference { get; init; } = "";
        public string Method { get; init; } = "";
        public string CallBackUrl { get; init; } = "";
        public string ErrorUrl { get; init; } = "";
        public string Status { get; set; } = "Pending";
        public string? PaymentId { get; set; }
        public string? Error { get; set; }
    }

    public bool IsConfigured => true;

    public static SimulatedInvoice? Find(string invoiceId) => Invoices.GetValueOrDefault(invoiceId);

    public static SimulatedInvoice? FindByPayment(string paymentId) => Invoices.Values.FirstOrDefault(item => item.PaymentId == paymentId);

    public Task<IReadOnlyList<GatewayPaymentMethod>> InitiatePaymentAsync(decimal amount, string currencyIso, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<GatewayPaymentMethod>>(new[]
        {
            new GatewayPaymentMethod(1, "kn", "كي نت", "KNET", null, amount, false),
            new GatewayPaymentMethod(2, "vm", "بطاقة", "VISA/MASTER", null, amount, false),
            new GatewayPaymentMethod(11, "ap", "أبل باي", "Apple Pay", null, amount, false)
        });

    public Task<GatewayExecuteResult> ExecutePaymentAsync(GatewayExecuteRequest request, CancellationToken cancellationToken = default)
    {
        var id = Interlocked.Increment(ref _sequence).ToString(CultureInfo.InvariantCulture);
        Invoices[id] = new SimulatedInvoice
        {
            InvoiceId = id,
            Amount = request.Amount,
            Reference = request.Reference,
            Method = request.PaymentMethodId switch { 1 => "KNET", 11 => "Apple Pay", _ => "VISA/MASTER" },
            CallBackUrl = request.CallBackUrl,
            ErrorUrl = request.ErrorUrl
        };
        var origin = new Uri(request.CallBackUrl).GetLeftPart(UriPartial.Authority);
        return Task.FromResult(new GatewayExecuteResult(id, $"{origin}/api/booking-flow/simulated-gateway?invoiceId={id}"));
    }

    public Task<GatewayPaymentStatus> GetPaymentStatusAsync(string key, string keyType, CancellationToken cancellationToken = default)
    {
        var invoice = keyType == "PaymentId" ? FindByPayment(key) : Find(key);
        if (invoice == null) throw new PaymentGatewayException("الفاتورة غير موجودة.");
        return Task.FromResult(new GatewayPaymentStatus(invoice.InvoiceId, invoice.Status, invoice.Amount, invoice.Reference, invoice.PaymentId, invoice.Method, invoice.Error));
    }
}
