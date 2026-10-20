using System.ComponentModel.DataAnnotations;

namespace RowadUmrahSystem.Web.ViewModels.Api;

public sealed class AssistantTurn
{
    [Required, RegularExpression("^(user|assistant)$")]
    public string Role { get; set; } = "user";
    [Required, StringLength(3000, MinimumLength = 1)]
    public string Content { get; set; } = "";
}
public sealed class AssistantChatRequest
{
    [Required, StringLength(3000, MinimumLength = 1)]
    public string Message { get; set; } = "";
    [MaxLength(8)]
    public AssistantTurn[]? History { get; set; }
    public AssistantTripContext? Context { get; set; }
}
/// <summary>
/// What the mobile app knows about the pilgrim's trip. It is supplied by the client,
/// so the service treats every value as untrusted data, never as instructions.
/// </summary>
public sealed class AssistantTripContext
{
    [StringLength(80)] public string? TravelerName { get; set; }
    [StringLength(120)] public string? PackageName { get; set; }
    [StringLength(40)] public string? DepartureDate { get; set; }
    [Range(1, 60)] public int? DurationDays { get; set; }
    [StringLength(120)] public string? Hotel { get; set; }
    [StringLength(80)] public string? Transport { get; set; }
    [Range(1, 50)] public int? TravelersCount { get; set; }
    [StringLength(40)] public string? BookingReference { get; set; }
    [StringLength(80)] public string? BookingStatus { get; set; }
    public bool? PaymentVerified { get; set; }
    [RegularExpression("^(ihram|tawaf|sai|halq)$")] public string? ManasekStep { get; set; }
    [Range(0, 7)] public int? TawafRound { get; set; }
}
public sealed record AssistantReply(string Reply, IReadOnlyList<string> Actions);
public sealed class AssistantSpeechRequest
{
    [Required, StringLength(3000, MinimumLength = 1)]
    public string Text { get; set; } = "";
}
