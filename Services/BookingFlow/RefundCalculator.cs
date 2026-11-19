namespace RowadUmrahSystem.Web.Services.BookingFlow;

/// <summary>What the customer paid for one component, and how far that component has got.</summary>
/// <param name="ActualSupplierCost">What the supplier or authority really retains, when known (articles 73, 74).</param>
/// <param name="PerformedValue">Value of the part already delivered, when known (article 72).</param>
public sealed record RefundComponentState(
    string Component,
    string Stage,
    decimal Paid,
    decimal? ActualSupplierCost = null,
    decimal? PerformedValue = null);

/// <summary>
/// A cancellation as article 71 frames it: the reason, and the moment the request was RECEIVED —
/// that moment, not the moment it is processed, decides the financial consequences.
/// </summary>
public sealed record RefundRequest(
    string Reason,
    DateTime RequestReceivedAtUtc,
    DateOnly? DepartureDate,
    IReadOnlyList<RefundComponentState> Components);

public sealed record RefundLine(
    string Component,
    string Label,
    string Stage,
    decimal Paid,
    decimal Deducted,
    decimal Refundable,
    string Basis,
    string State,
    string? Missing);

public sealed record RefundResult(
    string Status,
    decimal Paid,
    decimal Deducted,
    decimal Refundable,
    string Currency,
    DateTime RequestReceivedAtUtc,
    int? DaysBeforeDeparture,
    IReadOnlyList<RefundLine> Lines,
    IReadOnlyList<string> Missing);

/// <summary>
/// Works out what is owed back after a cancellation, component by component, as article 72
/// requires: « يتم عند الاقتضاء احتساب الاسترداد بصورة منفصلة بالنسبة إلى التأشيرة وتذكرة الطيران
/// والفندق والنقل والتأمين وغيرها من مكونات الحجز ».
///
/// The design decision that matters: when the matrix does not settle a component, this returns
/// "undetermined" for it and says exactly what is missing. It never falls back to zero and never
/// falls back to a full refund. Guessing in either direction is what article 72 forbids, and a
/// figure the agency cannot justify is worse than no figure at all — so the booking goes to the
/// back office with a worksheet instead of to the customer with an invented number.
///
/// Pure: no database, no clock, no configuration beyond the matrix it is handed.
/// </summary>
public static class RefundCalculator
{
    public const string Computed = "computed";
    public const string NeedsReview = "needs_review";
    public const string PolicyDraft = "policy_draft";

    private const string LineComputed = "computed";
    private const string LineUndetermined = "undetermined";

    public static RefundResult Calculate(RefundPolicy policy, RefundRequest request)
    {
        var daysBeforeDeparture = DaysBefore(request.DepartureDate, request.RequestReceivedAtUtc);
        var lines = new List<RefundLine>();
        var missing = new List<string>();

        foreach (var state in request.Components)
        {
            var component = policy.Component(state.Component);
            if (component is null)
            {
                lines.Add(Undetermined(state, state.Component, $"\"{state.Component}\" is not a component of the refund matrix."));
                missing.Add($"{state.Component}: unknown component.");
                continue;
            }

            // A bundled line cannot be settled per component until the agency says how it divides.
            if (component.Split is { Count: > 0 })
            {
                foreach (var part in component.Split)
                {
                    var share = new RefundComponentState(
                        part.Component,
                        state.Stage,
                        Round(state.Paid * part.Share),
                        state.ActualSupplierCost is { } cost ? Round(cost * part.Share) : null,
                        state.PerformedValue is { } performed ? Round(performed * part.Share) : null);
                    Settle(policy, request, share, daysBeforeDeparture, lines, missing);
                }
                continue;
            }
            if (component.Bundled)
            {
                lines.Add(Undetermined(state, component.Label,
                    $"\"{component.Id}\" bundles several services; article 72 needs each settled separately, and the matrix has no split for it."));
                missing.Add($"{component.Id}: declare how its price divides (components[].split in refund-policy.json).");
                continue;
            }

            Settle(policy, request, state, daysBeforeDeparture, lines, missing);
        }

        var paid = lines.Sum(line => line.Paid);
        var deducted = lines.Where(line => line.State == LineComputed).Sum(line => line.Deducted);
        var refundable = lines.Where(line => line.State == LineComputed).Sum(line => line.Refundable);

        var status = !policy.IsActive ? PolicyDraft
            : missing.Count > 0 ? NeedsReview
            : Computed;
        if (!policy.IsActive)
            missing.Insert(0, "the refund matrix is still a draft: appendix (ب) requires it to be matched against the supplier contracts before the service is sold.");

        return new RefundResult(
            status, Round(paid), Round(deducted), Round(refundable), policy.Currency,
            request.RequestReceivedAtUtc, daysBeforeDeparture, lines, missing);
    }

    private static void Settle(
        RefundPolicy policy, RefundRequest request, RefundComponentState state,
        int? daysBeforeDeparture, List<RefundLine> lines, List<string> missing)
    {
        var component = policy.Component(state.Component);
        var label = component?.Label ?? state.Component;

        if (component is not null && !component.Stages.Contains(state.Stage))
        {
            lines.Add(Undetermined(state, label, $"\"{state.Stage}\" is not a stage of \"{state.Component}\"."));
            missing.Add($"{state.Component}: unknown stage \"{state.Stage}\".");
            return;
        }

        var rule = policy.Match(state.Component, state.Stage, request.Reason, daysBeforeDeparture);
        if (rule is null)
        {
            var window = daysBeforeDeparture is { } days ? $" at {days} days before departure" : "";
            lines.Add(Undetermined(state, label,
                $"the matrix settles nothing for {state.Component} / {state.Stage} / {request.Reason}{window}."));
            missing.Add($"{state.Component} / {state.Stage} / {request.Reason}: add a rule with its basis to refund-policy.json.");
            return;
        }

        var deduction = rule.Deduction;
        decimal deducted;
        switch (deduction.Kind)
        {
            case "none":
                deducted = 0m;
                break;
            case "percentage":
                deducted = state.Paid * deduction.Value / 100m;
                break;
            case "fixed":
                deducted = Math.Min(deduction.Value, state.Paid);
                break;
            case "all":
                deducted = state.Paid;
                break;
            case "performedValue":
                if (state.PerformedValue is not { } performed)
                {
                    lines.Add(Undetermined(state, label, "the value of what was already delivered is not recorded for this booking (article 72)."));
                    missing.Add($"{state.Component}: record the value of the part already delivered.");
                    return;
                }
                deducted = performed;
                break;
            case "actualSupplierCost":
                if (state.ActualSupplierCost is not { } cost)
                {
                    lines.Add(Undetermined(state, label, "what the supplier or the authority actually retains is not recorded for this booking (articles 73, 74)."));
                    missing.Add($"{state.Component}: record the amount the supplier or authority actually retains.");
                    return;
                }
                deducted = cost;
                break;
            default:
                lines.Add(Undetermined(state, label, $"unknown deduction kind \"{deduction.Kind}\"."));
                missing.Add($"{state.Component}: unknown deduction kind \"{deduction.Kind}\".");
                return;
        }

        // Nothing may be withheld beyond what was actually paid for that component, and a refund
        // is never negative.
        deducted = Math.Clamp(Round(deducted), 0m, Round(state.Paid));
        lines.Add(new RefundLine(
            state.Component, label, state.Stage,
            Round(state.Paid), deducted, Round(state.Paid) - deducted,
            deduction.Basis ?? "", LineComputed, null));
    }

    private static RefundLine Undetermined(RefundComponentState state, string label, string why) =>
        new(state.Component, label, state.Stage, Round(state.Paid), 0m, 0m, "", LineUndetermined, why);

    /// <summary>
    /// Days between the moment the request was received and departure (article 71). Negative when
    /// the request arrives after departure — a no-show is not an early cancellation.
    /// </summary>
    private static int? DaysBefore(DateOnly? departure, DateTime receivedAtUtc) =>
        departure is { } date ? date.DayNumber - DateOnly.FromDateTime(receivedAtUtc).DayNumber : null;

    /// <summary>Kuwaiti dinar: three decimals.</summary>
    private static decimal Round(decimal value) => Math.Round(value, 3, MidpointRounding.AwayFromZero);
}
