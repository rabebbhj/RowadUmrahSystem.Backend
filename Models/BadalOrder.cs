namespace RowadUmrahSystem.Web.Models
{
    /// <summary>Life of a «عمرة البدل» order. Mirrors badalFlow.js on the phone.</summary>
    public static class BadalStatuses
    {
        public const string Draft = "draft";
        public const string PendingPayment = "pending_payment";
        public const string Paid = "paid";
        public const string Assigned = "assigned";
        public const string Scheduled = "scheduled";
        public const string InProgress = "in_progress";
        public const string QaReview = "qa_review";
        public const string Completed = "completed";
        public const string Refunded = "refunded";
        public const string Cancelled = "cancelled";
    }

    /// <summary>
    /// Who the omra may be performed for. A living, ABLE person is never eligible: the order is
    /// refused here as well as on the phone, so a modified client cannot get around the rule.
    /// </summary>
    public static class BadalBeneficiaryStatuses
    {
        public const string Deceased = "deceased";
        public const string Incapable = "incapable";
        public const string Capable = "capable";
    }

    public static class BadalRites
    {
        public const string Ihram = "ihram";
        public const string EnterHaram = "enter_haram";
        public const string Tawaf = "tawaf";
        public const string Maqam = "maqam";
        public const string Sai = "sai";
        public const string Halq = "halq";

        /// <summary>The six rites, in the order the client follows them.</summary>
        public static readonly string[] All = { Ihram, EnterHaram, Tawaf, Maqam, Sai, Halq };
    }

    /// <summary>
    /// One omra ordered by a client (المُوكِّل) for a deceased or lastingly unable person
    /// (المُنابُ عنه), performed by a verified pilgrim (المعتمر).
    /// One order = one omra = one beneficiary. The price is ALWAYS set here, never by the phone.
    /// </summary>
    public class BadalOrder
    {
        public int Id { get; set; }

        /// <summary>Human reference shown to the client, e.g. RW-B-1042.</summary>
        public string Reference { get; set; } = string.Empty;

        public string UserId { get; set; } = string.Empty;

        /// <summary>Client-generated key so a retried request never creates a second order.</summary>
        public string? ClientRequestId { get; set; }

        public string BeneficiaryFullName { get; set; } = string.Empty;

        /// <summary>"m" or "f": decides «رحمه الله» / «رحمها الله».</summary>
        public string BeneficiaryGender { get; set; } = "m";

        /// <summary>"deceased" or "incapable". "capable" is never stored: it is refused.</summary>
        public string BeneficiaryStatus { get; set; } = string.Empty;

        public string? BeneficiaryRelation { get; set; }

        /// <summary>Recited during the tawaf and the sa'y. Moderated before it reaches the pilgrim.</summary>
        public string? PersonalDua { get; set; }
        public bool PersonalDuaApproved { get; set; }
        public string? PersonalDuaRejectedReason { get; set; }

        /// <summary>"asap", "friday", "ramadan" or "date".</summary>
        public string TimingType { get; set; } = "asap";
        public DateTime? TimingDate { get; set; }

        /// <summary>When the client ticked the mandatory declaration. Kept as proof.</summary>
        public DateTime DeclarationAcceptedAt { get; set; }

        public decimal Price { get; set; }
        public string CurrencyIso { get; set; } = "KWD";

        public string Status { get; set; } = BadalStatuses.PendingPayment;

        public string? PaymentInvoiceId { get; set; }
        public string? PaymentId { get; set; }
        public string? PaymentMethod { get; set; }
        public string PaymentStatus { get; set; } = "unpaid";
        public string? LastPaymentError { get; set; }
        public string? PaymentReturnUrl { get; set; }
        public DateTime? PaidAt { get; set; }

        public int? PerformerId { get; set; }
        public BadalPerformer? Performer { get; set; }
        public DateTime? AssignedAt { get; set; }
        public DateTime? ScheduledAt { get; set; }
        public DateTime? StartedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
        public DateTime? RefundedAt { get; set; }
        public string? RefundReason { get; set; }

        public string? CertificateNumber { get; set; }
        public string? CertificatePdfPath { get; set; }
        public DateTime? CertificateIssuedAt { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        public List<BadalRiteEvent> Rites { get; set; } = new();
    }

    /// <summary>Proof that one rite was performed: when, where, and the photos or videos.</summary>
    public class BadalRiteEvent
    {
        public int Id { get; set; }
        public int BadalOrderId { get; set; }
        public BadalOrder? Order { get; set; }

        /// <summary>One of BadalRites.</summary>
        public string Rite { get; set; } = string.Empty;

        public DateTime? StartedAt { get; set; }
        public DateTime? CompletedAt { get; set; }

        public double? Latitude { get; set; }
        public double? Longitude { get; set; }
        public double? AccuracyMeters { get; set; }

        /// <summary>Named zone checked by the quality review: miqat_tanim, mataf, masaa…</summary>
        public string? Zone { get; set; }

        /// <summary>Tawaf and sa'y only: the number of rounds counted.</summary>
        public int? Rounds { get; set; }

        /// <summary>Ihram only: the intention actually spoken at the miqat.</summary>
        public string? NiyyaText { get; set; }

        public List<BadalMedia> Media { get; set; } = new();
    }

    /// <summary>
    /// A photo or a short video of one rite. Stored privately: the client reaches it through a
    /// signed URL, never a public path.
    /// </summary>
    public class BadalMedia
    {
        public int Id { get; set; }
        public int BadalRiteEventId { get; set; }
        public BadalRiteEvent? RiteEvent { get; set; }

        /// <summary>"photo" or "video".</summary>
        public string Kind { get; set; } = "photo";

        /// <summary>Path on the private store; never served as-is.</summary>
        public string StoragePath { get; set; } = string.Empty;

        public DateTime TakenAt { get; set; }
        public long SizeBytes { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// A verified pilgrim who performs the omra. The client only ever sees the first name and the
    /// city: the full name is never exposed.
    /// </summary>
    public class BadalPerformer
    {
        public int Id { get; set; }

        /// <summary>Linked account when the pilgrim signs in to the app.</summary>
        public string? UserId { get; set; }

        public string FirstName { get; set; } = string.Empty;

        /// <summary>Kept for the back-office only.</summary>
        public string FullName { get; set; } = string.Empty;

        /// <summary>"makkah" or "jeddah": decides the miqat he leaves from.</summary>
        public string City { get; set; } = "makkah";

        /// <summary>Comma-separated language codes, e.g. "ar,en".</summary>
        public string Languages { get; set; } = "ar";

        public double Rating { get; set; }
        public int BadalCount { get; set; }

        /// <summary>Identity checked by the agency.</summary>
        public bool Verified { get; set; }

        /// <summary>He performed his OWN omra first: a requirement of the rule.</summary>
        public bool OwnUmrahDone { get; set; }

        public bool Active { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
