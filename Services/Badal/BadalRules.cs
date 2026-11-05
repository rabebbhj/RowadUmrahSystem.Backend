using RowadUmrahSystem.Web.Models;

namespace RowadUmrahSystem.Web.Services.Badal;

/// <summary>A reason an order is refused. `Code` is the SAME string as on the phone.</summary>
public sealed record BadalRuleIssue(string Field, string Code);

public sealed class BadalBeneficiaryRequest
{
    public string? FullName { get; set; }
    public string? Gender { get; set; }
    public string? Status { get; set; }
    public string? Relation { get; set; }
}

public sealed class BadalTimingRequest
{
    public string? Type { get; set; }
    public string? Date { get; set; }
}

/// <summary>
/// What the phone sends. Note what is NOT here: no price and no total. The price is read from the
/// back-office configuration by the server, never accepted from the client.
/// </summary>
public sealed class BadalOrderRequest
{
    public BadalBeneficiaryRequest? Beneficiary { get; set; }
    public string? PersonalDua { get; set; }
    public BadalTimingRequest? Timing { get; set; }
    public bool DeclarationAccepted { get; set; }
    public string? ClientRequestId { get; set; }
}

/// <summary>
/// The rules of «عمرة البدل», on the server side.
///
/// This is the deliberate twin of RowadUmrahSystem.Mobile/src/badal/badalFlow.js: the same codes,
/// the same limits, the same verdicts. A modified application, an old version, or a direct call to
/// the API must never be able to place an order that the app would have refused —
/// tests/badal-parity-checks.cjs compares the two, case by case.
///
/// The religious rules themselves come from the agency's specification and are pending validation
/// by its religious referent.
/// </summary>
public static class BadalRules
{
    public const int DuaMax = 300;
    public const int SlaDays = 7;

    public static readonly string[] Genders = { "m", "f" };
    public static readonly string[] Timings = { "asap", "friday", "ramadan", "date" };
    public static readonly string[] Relations = { "father", "mother", "grandfather", "grandmother", "other" };

    /// <summary>Valid only for a deceased person or one who is lastingly unable.</summary>
    public static bool IsEligible(string? beneficiaryStatus) =>
        beneficiaryStatus == BadalBeneficiaryStatuses.Deceased ||
        beneficiaryStatus == BadalBeneficiaryStatuses.Incapable;

    /// <summary>"capable", "missing", or null when the beneficiary is eligible.</summary>
    public static string? EligibilityReason(string? beneficiaryStatus)
    {
        if (IsEligible(beneficiaryStatus)) return null;
        return beneficiaryStatus == BadalBeneficiaryStatuses.Capable ? "capable" : "missing";
    }

    /// <summary>«رحمه الله» / «رحمها الله», only for a deceased person.</summary>
    public static string? Honorific(string? beneficiaryStatus, string? gender)
    {
        if (beneficiaryStatus != BadalBeneficiaryStatuses.Deceased) return null;
        return gender == "f" ? "رحمها الله" : "رحمه الله";
    }

    public static string FirstName(string? fullName) => Words(fullName).FirstOrDefault() ?? string.Empty;

    public static int NameWordCount(string? fullName) => Words(fullName).Length;

    /// <summary>The intention the pilgrim speaks at the miqat.</summary>
    public static string NiyyaText(string? fullName)
    {
        var name = FirstName(fullName);
        return name.Length == 0 ? "«لبّيك عمرةً عن …»" : $"«لبّيك عمرةً عن {name}»";
    }

    private static string[] Words(string? value) =>
        (value ?? string.Empty).Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static bool IsIsoDay(string? value) =>
        !string.IsNullOrEmpty(value) && DateTime.TryParseExact(value, "yyyy-MM-dd",
            System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out _);

    /// <summary>Everything that stops the order. Empty means it may go to payment.</summary>
    public static IReadOnlyList<BadalRuleIssue> Validate(BadalOrderRequest request)
    {
        var issues = new List<BadalRuleIssue>();
        var beneficiary = request.Beneficiary;

        if (NameWordCount(beneficiary?.FullName) < 2) issues.Add(new("fullName", "name_too_short"));
        if (beneficiary?.Gender is null || !Genders.Contains(beneficiary.Gender)) issues.Add(new("gender", "gender_missing"));

        var reason = EligibilityReason(beneficiary?.Status);
        if (reason != null) issues.Add(new("status", reason == "capable" ? "capable_not_allowed" : "status_missing"));

        if ((request.PersonalDua ?? string.Empty).Length > DuaMax) issues.Add(new("personalDua", "dua_too_long"));

        var timing = request.Timing;
        if (timing?.Type is null || !Timings.Contains(timing.Type)) issues.Add(new("timing", "timing_missing"));
        else if (timing.Type == "date" && !IsIsoDay(timing.Date)) issues.Add(new("timing", "date_missing"));

        if (!string.IsNullOrEmpty(beneficiary?.Relation) && !Relations.Contains(beneficiary.Relation))
            issues.Add(new("relation", "relation_unknown"));

        if (!request.DeclarationAccepted) issues.Add(new("declaration", "declaration_required"));
        return issues;
    }

    public static bool CanSubmit(BadalOrderRequest request) => Validate(request).Count == 0;

    /// <summary>Deadline to perform the omra, or null while no pilgrim is assigned.</summary>
    public static DateTime? SlaDeadline(DateTime? assignedAt, int days = SlaDays) =>
        assignedAt?.AddDays(days);

    /// <summary>True when the promise is broken and the order must be refunded in full.</summary>
    public static bool IsSlaBreached(BadalOrder order, DateTime now, int days = SlaDays)
    {
        if (order.AssignedAt == null) return false;
        if (order.Status is BadalStatuses.Completed or BadalStatuses.Refunded or BadalStatuses.Cancelled) return false;
        var deadline = SlaDeadline(order.AssignedAt, days);
        return deadline != null && deadline <= now;
    }
}
