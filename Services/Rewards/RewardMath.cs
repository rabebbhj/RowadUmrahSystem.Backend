namespace RowadUmrahSystem.Web.Services.Rewards;

/// <summary>
/// Le calcul du programme « نقاط روّاد » : points, portefeuille, et ce qui reste a payer.
///
/// CE FICHIER NE TOUCHE NI A LA BASE NI AU RESEAU
/// Il ne contient que du calcul, pour qu'il soit verifiable sans rien demarrer. C'est voulu :
/// ici une erreur de trois decimales n'est pas un defaut d'affichage, c'est de l'argent en moins
/// dans la caisse de l'agence ou en trop sur la facture du pelerin.
///
/// LE DINAR KOWEITIEN A TROIS DECIMALES
/// 1 د.ك = 1000 fils. Tout montant est un multiple du fils. Un calcul qui produirait 5,0005 د.ك
/// n'a pas de sens : il faudrait l'arrondir, et chaque arrondi vole un centieme a quelqu'un.
/// D'ou la regle de configuration ci-dessous, qui supprime le probleme a la racine.
/// </summary>
public sealed record RewardConfig
{
    /// <summary>Points offerts au parrain quand le filleul verifie son numero.</summary>
    public int SignupVerifiedPoints { get; init; } = 100;
    /// <summary>Points offerts au filleul a l'inscription.</summary>
    public int WelcomePoints { get; init; } = 50;
    /// <summary>Points offerts au parrain quand le filleul paie sa premiere reservation.</summary>
    public int FirstBookingPoints { get; init; } = 500;
    /// <summary>Points gagnes par dinar REELLEMENT paye.</summary>
    public int EarnPerKwd { get; init; } = 1;
    /// <summary>Combien de points valent un dinar.</summary>
    public int PointsPerKwd { get; init; } = 100;
    /// <summary>Part maximale du total qu'on peut regler en points, en pourcentage.</summary>
    public int MaxRedeemPercent { get; init; } = 20;
    /// <summary>Duree de vie des points, en mois.</summary>
    public int PointsExpiryMonths { get; init; } = 12;
    /// <summary>Plafond de points de parrainage gagnables dans un mois, par parrain.</summary>
    public int MonthlyReferralPointsCap { get; init; } = 3000;

    /// <summary>
    /// Ce qui rend la configuration inutilisable. Une liste vide veut dire qu'elle est sure.
    ///
    /// La regle importante est la derniere : un point doit valoir un nombre ENTIER de fils.
    /// Avec 100 points par dinar, un point vaut 10 fils, et toute conversion tombe juste. Avec
    /// 3 points par dinar, un point vaudrait 333,33 fils : chaque echange laisserait une miette,
    /// et ces miettes finiraient par ne plus tomber du bon cote.
    /// </summary>
    public IReadOnlyList<string> Problems()
    {
        var problems = new List<string>();
        if (PointsPerKwd <= 0) problems.Add("PointsPerKwd doit etre positif.");
        else if (1000 % PointsPerKwd != 0)
        {
            problems.Add($"PointsPerKwd={PointsPerKwd} ne divise pas 1000 : un point ne vaudrait pas un nombre entier de fils. Valeurs sures : 1, 2, 4, 5, 8, 10, 20, 25, 40, 50, 100, 125, 200, 250, 500, 1000.");
        }
        if (MaxRedeemPercent is < 0 or > 100) problems.Add("MaxRedeemPercent doit etre entre 0 et 100.");
        if (EarnPerKwd < 0) problems.Add("EarnPerKwd ne peut pas etre negatif.");
        if (PointsExpiryMonths <= 0) problems.Add("PointsExpiryMonths doit etre positif.");
        if (SignupVerifiedPoints < 0 || WelcomePoints < 0 || FirstBookingPoints < 0)
        {
            problems.Add("Les recompenses ne peuvent pas etre negatives.");
        }
        return problems;
    }

    /// <summary>Combien de fils vaut un point. N'a de sens que si Problems() est vide.</summary>
    public int FilsPerPoint => 1000 / PointsPerKwd;
}

/// <summary>Ce que le client demande a utiliser.</summary>
public sealed record RedeemRequest(decimal BookingTotal, decimal WalletBalance, int PointsBalance, bool UseWallet, int RequestedPoints);

/// <summary>Le decompte, tel qu'il sera affiche ET facture. Toutes les valeurs sont definitives.</summary>
public sealed record RedeemResult(
    decimal BookingTotal,
    int PointsUsed,
    decimal PointsDiscount,
    decimal WalletUsed,
    decimal RemainingToPay,
    int PointsEarned,
    int MaxPointsAllowed,
    IReadOnlyList<string> Notices);

public static class RewardMath
{
    /// <summary>Arrondi au fils. Le dinar koweitien n'a pas de quatrieme decimale.</summary>
    public static decimal ToFils(decimal amount) => Math.Round(amount, 3, MidpointRounding.ToZero);

    /// <summary>
    /// Le decompte d'un paiement.
    ///
    /// L'ORDRE COMPTE : les points s'appliquent d'abord, parce que leur plafond se calcule sur le
    /// TOTAL de la reservation. Si le portefeuille passait avant, le plafond se calculerait sur
    /// un montant deja reduit, et le client perdrait une partie de ses points sans raison.
    ///
    /// CE QUI EST PAYE EN POINTS OU EN PORTEFEUILLE NE RAPPORTE PAS DE POINTS. Sans cela, on
    /// pourrait tourner en rond : gagner des points, les depenser, et regagner des points
    /// dessus — de l'argent cree a partir de rien.
    /// </summary>
    public static RedeemResult Redeem(RewardConfig config, RedeemRequest request)
    {
        var notices = new List<string>();

        var total = ToFils(Math.Max(0m, request.BookingTotal));
        var walletBalance = ToFils(Math.Max(0m, request.WalletBalance));
        var pointsBalance = Math.Max(0, request.PointsBalance);

        // Le plafond : la part du total reglable en points, convertie en points.
        var maxDiscount = ToFils(total * config.MaxRedeemPercent / 100m);
        var maxPointsByCap = (int)(maxDiscount * 1000m / config.FilsPerPoint);

        var requested = Math.Max(0, request.RequestedPoints);
        var pointsUsed = Math.Min(requested, Math.Min(pointsBalance, maxPointsByCap));

        if (requested > pointsBalance)
        {
            notices.Add("points-insuffisants");
        }
        else if (requested > maxPointsByCap)
        {
            notices.Add("plafond-points-atteint");
        }

        var pointsDiscount = pointsUsed * config.FilsPerPoint / 1000m;

        // Le portefeuille couvre ce qui reste, sans jamais depasser le solde ni le reste a payer.
        var afterPoints = total - pointsDiscount;
        var walletUsed = request.UseWallet ? Math.Min(walletBalance, afterPoints) : 0m;
        walletUsed = ToFils(Math.Max(0m, walletUsed));
        if (request.UseWallet && walletBalance > afterPoints && afterPoints >= 0m)
        {
            notices.Add("portefeuille-partiellement-utilise");
        }

        var remaining = ToFils(Math.Max(0m, afterPoints - walletUsed));

        // On ne gagne des points que sur l'argent reellement sorti de la poche.
        var earned = (int)Math.Floor(remaining * config.EarnPerKwd);

        return new RedeemResult(
            total,
            pointsUsed,
            ToFils(pointsDiscount),
            walletUsed,
            remaining,
            earned,
            maxPointsByCap,
            notices);
    }

    /// <summary>Ce que valent des points en dinars, pour l'affichage.</summary>
    public static decimal PointsToKwd(RewardConfig config, int points) =>
        ToFils(Math.Max(0, points) * config.FilsPerPoint / 1000m);

    // --- Le code de parrainage ---

    /// <summary>
    /// L'alphabet des codes. Ni 0 ni O, ni 1 ni I : sur une capture d'ecran envoyee par WhatsApp,
    /// personne ne sait les distinguer, et le filleul saisit alors un code qui n'existe pas.
    /// </summary>
    public const string CodeAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    public const string CodePrefix = "RWD-";
    /// <summary>Longueur de la partie tiree au sort. Le code complet fait 8 caracteres.</summary>
    public const int CodeRandomLength = 4;

    /// <summary>Fabrique un code a partir d'une source de hasard fournie (testable).</summary>
    public static string MakeCode(Func<int, int> nextBelow)
    {
        var characters = new char[CodeRandomLength];
        for (var i = 0; i < CodeRandomLength; i++)
        {
            characters[i] = CodeAlphabet[nextBelow(CodeAlphabet.Length)];
        }
        return CodePrefix + new string(characters);
    }

    /// <summary>
    /// Nettoie ce que le filleul a tape : espaces, minuscules, tiret oublie. Rend "" si le
    /// resultat n'est pas un code possible.
    ///
    /// ON NE DEVINE PAS. Si quelqu'un tape un 0 ou un O — deux caracteres absents de l'alphabet —
    /// c'est qu'il a mal lu une lettre, et on ne peut pas savoir laquelle. La « reparer » au
    /// jugé donnerait un code valide appartenant a QUELQU'UN D'AUTRE, et les points iraient a un
    /// inconnu. Mieux vaut dire « ce code n'existe pas » et laisser le filleul relire.
    /// </summary>
    public static string NormalizeCode(string? typed)
    {
        if (string.IsNullOrWhiteSpace(typed)) return "";
        var cleaned = new string(typed.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
        if (cleaned.StartsWith("RWD", StringComparison.Ordinal)) cleaned = cleaned[3..];
        if (cleaned.Length != CodeRandomLength) return "";
        return cleaned.All(CodeAlphabet.Contains) ? CodePrefix + cleaned : "";
    }

    /// <summary>Un code bien forme ? (forme seulement : l'existence se verifie en base.)</summary>
    public static bool LooksLikeCode(string? code) =>
        !string.IsNullOrEmpty(code)
        && code.StartsWith(CodePrefix, StringComparison.Ordinal)
        && code.Length == CodePrefix.Length + CodeRandomLength
        && code[CodePrefix.Length..].All(CodeAlphabet.Contains);
}
