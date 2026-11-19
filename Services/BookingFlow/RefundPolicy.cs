using System.Text.Json;
using System.Text.Json.Serialization;

namespace RowadUmrahSystem.Web.Services.BookingFlow;

/// <summary>
/// The cancellation and refund matrix required by appendix (ب) of the Terms, loaded from
/// App_Data/refund-policy.json.
///
/// Appendix (ب) requires the matrix to exist before the service is sold, and forbids adopting any
/// percentage, amount or "non-refundable" description that has not been matched against the real
/// supplier contracts. Article 72 adds two hard rules that this type enforces in code:
///   - a refund is settled SEPARATELY for each component (visa, ticket, hotel, transport,
///     insurance), never as one lump sum;
///   - the whole amount may not be treated as non-refundable on the strength of a generic phrase.
/// </summary>
public sealed record RefundPolicy(
    string Version,
    string Status,
    bool VerifiedAgainstSupplierContracts,
    string Currency,
    IReadOnlyList<RefundComponent> Components,
    IReadOnlyList<RefundReason> Reasons,
    IReadOnlyList<RefundRule> Rules,
    RefundForbidden? Forbidden)
{
    /// <summary>Only an active, contract-checked matrix may produce a figure shown to a customer.</summary>
    public bool IsActive => Status == "active" && VerifiedAgainstSupplierContracts;

    public RefundComponent? Component(string id) =>
        Components.FirstOrDefault(component => component.Id == id);

    /// <summary>
    /// The rule that governs one (component, stage, reason) at a given distance from departure.
    /// The most specific rule wins — the one that names the fewest components, stages and reasons —
    /// so a rule written for one exact case always beats a broad default. Ties keep file order.
    /// </summary>
    public RefundRule? Match(string component, string stage, string reason, int? daysBeforeDeparture)
    {
        RefundRule? best = null;
        var bestWeight = int.MaxValue;
        foreach (var rule in Rules)
        {
            if (!rule.Components.Contains(component)) continue;
            if (!rule.Stages.Contains(stage)) continue;
            if (!rule.Reasons.Contains(reason)) continue;
            if (!rule.AppliesAt(daysBeforeDeparture)) continue;
            var weight = rule.Components.Count + rule.Stages.Count + rule.Reasons.Count;
            if (weight >= bestWeight) continue;
            best = rule;
            bestWeight = weight;
        }
        return best;
    }

    /// <summary>
    /// Everything that makes the matrix unusable, in plain words. An empty list means the matrix
    /// may be switched to "active". This is the check appendix (ب) asks for, done by the machine
    /// instead of by memory.
    /// </summary>
    public IReadOnlyList<string> Problems()
    {
        var problems = new List<string>();
        var generic = Forbidden?.GenericBasisPhrases ?? Array.Empty<string>();

        foreach (var rule in Rules)
        {
            var name = $"rule [{string.Join("/", rule.Components)} · {string.Join("/", rule.Stages)} · {string.Join("/", rule.Reasons)}]";
            if (rule.Components.Count == 0 || rule.Stages.Count == 0 || rule.Reasons.Count == 0)
                problems.Add($"{name}: names no component, stage or reason.");

            foreach (var component in rule.Components)
                if (Component(component) is null) problems.Add($"{name}: unknown component \"{component}\".");

            var basis = rule.Deduction.Basis?.Trim() ?? "";
            // Article 72: a deduction has to say WHY, and "non-refundable" is not a why.
            if (rule.Deduction.Kind != "none" && basis.Length == 0)
                problems.Add($"{name}: deducts without stating a basis (article 72).");
            foreach (var phrase in generic)
                if (basis.Length > 0 && basis.Trim() == phrase)
                    problems.Add($"{name}: the basis \"{phrase}\" is a generic description, which article 72 forbids as a ground for withholding.");
            if (rule.Deduction.Kind == "all" && basis.Length < 20)
                problems.Add($"{name}: withholds the whole amount on a basis too short to be verifiable (article 72).");
            if (rule.Deduction.Kind == "percentage" && (rule.Deduction.Value <= 0 || rule.Deduction.Value > 100))
                problems.Add($"{name}: percentage {rule.Deduction.Value} is not between 0 and 100.");
            if (!RefundDeduction.Kinds.Contains(rule.Deduction.Kind))
                problems.Add($"{name}: unknown deduction kind \"{rule.Deduction.Kind}\".");
        }

        // Article 72 again: a price line that bundles several components cannot be settled until
        // the agency says how it divides.
        foreach (var component in Components)
            if (component.Split is { Count: > 0 })
            {
                var total = component.Split.Sum(part => part.Share);
                if (Math.Abs(total - 1m) > 0.001m)
                    problems.Add($"component \"{component.Id}\": the split adds up to {total}, not 1.");
                foreach (var part in component.Split)
                {
                    if (Component(part.Component) is null) problems.Add($"component \"{component.Id}\": split names unknown component \"{part.Component}\".");
                    if (string.IsNullOrWhiteSpace(part.Basis)) problems.Add($"component \"{component.Id}\": the {part.Component} share has no basis.");
                }
            }

        // Every (component, stage, reason) the matrix claims to cover must actually be covered.
        // A bundled component is the exception: it is never settled directly, it is split first
        // and each share is then settled by the rules of its own component.
        foreach (var component in Components)
        {
            if (component.Bundled && component.Split is { Count: > 0 }) continue;
            foreach (var stage in component.Stages)
                foreach (var reason in Reasons)
                    if (Match(component.Id, stage, reason.Id, null) is null)
                        problems.Add($"no rule for {component.Id} / {stage} / {reason.Id}.");
        }

        return problems;
    }

    private static readonly JsonSerializerOptions ReadOptions = new(JsonSerializerDefaults.Web)
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public static RefundPolicy Load(string path) =>
        JsonSerializer.Deserialize<RefundPolicy>(File.ReadAllText(path), ReadOptions)
        ?? throw new InvalidOperationException($"refund policy is empty: {path}");
}

/// <summary>One settleable part of a booking, in the sense of article 72.</summary>
/// <param name="Bundled">
/// True when this single price line pays for several of the services article 72 settles
/// separately. Such a component cannot be refunded until <see cref="Split"/> says how it divides.
/// </param>
public sealed record RefundComponent(
    string Id,
    string Label,
    IReadOnlyList<string> QuoteLines,
    IReadOnlyList<string> Stages,
    string? Article,
    bool Bundled,
    IReadOnlyList<RefundSplitPart>? Split);

/// <summary>How a bundled price line divides into the components article 72 settles separately.</summary>
public sealed record RefundSplitPart(string Component, decimal Share, string? Basis);

public sealed record RefundReason(string Id, string Label, string? Article);

/// <summary>Window, in days before departure, inside which a rule applies (article 71).</summary>
public sealed record RefundWindow(int? From, int? To);

public sealed record RefundRule(
    IReadOnlyList<string> Components,
    IReadOnlyList<string> Stages,
    IReadOnlyList<string> Reasons,
    RefundDeduction Deduction,
    [property: JsonPropertyName("daysBeforeDeparture")] RefundWindow? DaysBeforeDeparture)
{
    /// <summary>
    /// Article 71 makes the time the request is received decisive, so a rule may be limited to a
    /// window. A rule with no window applies whatever the date; a rule WITH one never applies when
    /// the date is unknown, because guessing would be the opposite of what article 71 asks.
    /// </summary>
    public bool AppliesAt(int? daysBeforeDeparture)
    {
        if (DaysBeforeDeparture is null) return true;
        if (daysBeforeDeparture is null) return false;
        if (DaysBeforeDeparture.From is { } from && daysBeforeDeparture < from) return false;
        if (DaysBeforeDeparture.To is { } to && daysBeforeDeparture > to) return false;
        return true;
    }
}

public sealed record RefundDeduction(string Kind, decimal Value, string? Basis)
{
    /// <summary>
    /// none              — nothing is withheld.
    /// percentage        — a share of what was paid for that component.
    /// fixed             — a fixed amount, capped at what was paid.
    /// performedValue    — the value of what was actually delivered (article 72); supplied per booking.
    /// actualSupplierCost— what the supplier or the authority really retains (articles 73, 74); supplied per booking.
    /// all               — the whole component, which article 72 allows only on a stated, verifiable basis.
    /// </summary>
    public static readonly IReadOnlyList<string> Kinds =
        new[] { "none", "percentage", "fixed", "performedValue", "actualSupplierCost", "all" };
}

public sealed record RefundForbidden(IReadOnlyList<string> GenericBasisPhrases);
