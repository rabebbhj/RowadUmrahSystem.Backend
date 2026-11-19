namespace RowadUmrahSystem.Web.Models;

/// <summary>
/// Compte « نقاط روّاد » d'un utilisateur : solde de points, portefeuille en dinars,
/// code de parrainage et lien vers le parrain.
///
/// Un seul enregistrement par utilisateur. Cree a l'inscription.
/// </summary>
public class RewardAccount
{
    public int Id { get; set; }

    public string UserId { get; set; } = string.Empty;

    /// <summary>Code de parrainage unique de cet utilisateur (format RWD-XXXX).</summary>
    public string ReferralCode { get; set; } = string.Empty;

    /// <summary>UserId du parrain, ou null si l'utilisateur s'est inscrit sans code.</summary>
    public string? ReferredByUserId { get; set; }

    /// <summary>Solde de points actuel (somme des transactions non expirees).</summary>
    public int PointsBalance { get; set; }

    /// <summary>Solde du portefeuille en dinars koweitiens (3 decimales = fils).</summary>
    public decimal WalletBalance { get; set; }

    /// <summary>Nombre de parrainages reussis (filleuls qui ont verifie leur numero).</summary>
    public int ReferralCount { get; set; }

    /// <summary>Points de parrainage gagnes ce mois-ci (plafond mensuel dans RewardConfig).</summary>
    public int MonthlyReferralPoints { get; set; }

    /// <summary>Mois du dernier comptage de parrainage (AAAA-MM). Remis a zero quand le mois change.</summary>
    public string? MonthlyReferralMonth { get; set; }

    /// <summary>عضو ذهبي, عضو فضي, etc. Pour l'affichage de la carte.</summary>
    public string Tier { get; set; } = "عضو ذهبي";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Un mouvement de points : gain ou depense. L'historique complet est conserve.
/// Le solde dans RewardAccount est la somme de toutes les transactions non expirees.
/// </summary>
public class PointsTransaction
{
    public int Id { get; set; }

    public string UserId { get; set; } = string.Empty;

    /// <summary>Positif = gain, negatif = depense.</summary>
    public int Amount { get; set; }

    /// <summary>Solde apres cette transaction (pour affichage rapide).</summary>
    public int BalanceAfter { get; set; }

    /// <summary>
    /// Type de transaction :
    /// welcome, referral_signup, referral_booking, booking_earn, redeem, expire, admin_adjust
    /// </summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>Description lisible en arabe pour l'historique.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>Id de la reservation liee, si applicable.</summary>
    public int? BookingId { get; set; }

    /// <summary>UserId du filleul/parrain lie, si c'est une transaction de parrainage.</summary>
    public string? RelatedUserId { get; set; }

    /// <summary>Nom affichable de la personne liee (« أحمد سجّل برمزك »).</summary>
    public string? RelatedUserName { get; set; }

    /// <summary>Date d'expiration de ces points (null si deja depenses ou expires).</summary>
    public DateTime? ExpiresAt { get; set; }

    /// <summary>true si ces points ont expire et ne comptent plus dans le solde.</summary>
    public bool IsExpired { get; set; }

    /// <summary>pending = pas encore confirme (ex. parrainage premier booking avant voyage).</summary>
    public string Status { get; set; } = PointsStatuses.Confirmed;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public static class PointsStatuses
{
    public const string Confirmed = "confirmed";
    public const string Pending = "pending";
    public const string Expired = "expired";
    public const string Cancelled = "cancelled";
}

/// <summary>
/// Un mouvement sur le portefeuille en dinars. Separe des points car c'est de l'argent reel.
/// </summary>
public class WalletTransaction
{
    public int Id { get; set; }

    public string UserId { get; set; } = string.Empty;

    /// <summary>Positif = credit, negatif = debit. En fils (3 decimales).</summary>
    public decimal Amount { get; set; }

    /// <summary>Solde apres cette transaction.</summary>
    public decimal BalanceAfter { get; set; }

    /// <summary>points_convert, refund, admin_adjust, redeem</summary>
    public string Type { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public int? BookingId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
