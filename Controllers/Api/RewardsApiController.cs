using System.Security.Cryptography;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RowadUmrahSystem.Web.Data;
using RowadUmrahSystem.Web.Models;
using RowadUmrahSystem.Web.Services.Rewards;

namespace RowadUmrahSystem.Web.Controllers.Api;

[ApiController]
[Route("api/rewards")]
[Authorize]
public class RewardsApiController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly RewardConfig _config = new();

    public RewardsApiController(ApplicationDbContext context, UserManager<ApplicationUser> userManager)
    {
        _context = context;
        _userManager = userManager;
    }

    /// <summary>GET /api/rewards/me — solde, code parrainage, tier.</summary>
    [HttpGet("me")]
    public async Task<IActionResult> Me()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Unauthorized();

        var account = await EnsureAccount(user);

        return Ok(new
        {
            account.PointsBalance,
            PointsValueKwd = RewardMath.PointsToKwd(_config, account.PointsBalance),
            account.WalletBalance,
            account.ReferralCode,
            account.ReferralCount,
            account.Tier,
            ExpiresAt = await NextExpiry(user.Id),
            PointsPerKwd = _config.PointsPerKwd,
            MaxRedeemPercent = _config.MaxRedeemPercent
        });
    }

    /// <summary>GET /api/rewards/history?page=1 — historique des transactions de points.</summary>
    [HttpGet("history")]
    public async Task<IActionResult> History([FromQuery] int page = 1)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Unauthorized();

        const int pageSize = 20;
        var skip = (Math.Max(1, page) - 1) * pageSize;

        var transactions = await _context.PointsTransactions
            .Where(t => t.UserId == user.Id)
            .OrderByDescending(t => t.CreatedAt)
            .Skip(skip)
            .Take(pageSize)
            .Select(t => new
            {
                t.Id,
                t.Amount,
                t.Type,
                t.Description,
                t.RelatedUserName,
                t.Status,
                t.CreatedAt
            })
            .ToListAsync();

        var total = await _context.PointsTransactions.CountAsync(t => t.UserId == user.Id);

        return Ok(new { items = transactions, total, page, pageSize });
    }

    /// <summary>GET /api/rewards/wallet-history?page=1 — historique du portefeuille.</summary>
    [HttpGet("wallet-history")]
    public async Task<IActionResult> WalletHistory([FromQuery] int page = 1)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Unauthorized();

        const int pageSize = 20;
        var skip = (Math.Max(1, page) - 1) * pageSize;

        var transactions = await _context.WalletTransactions
            .Where(t => t.UserId == user.Id)
            .OrderByDescending(t => t.CreatedAt)
            .Skip(skip)
            .Take(pageSize)
            .Select(t => new
            {
                t.Id,
                t.Amount,
                t.Type,
                t.Description,
                t.CreatedAt
            })
            .ToListAsync();

        var total = await _context.WalletTransactions.CountAsync(t => t.UserId == user.Id);

        return Ok(new { items = transactions, total, page, pageSize });
    }

    /// <summary>POST /api/rewards/convert-to-wallet — convertir des points en solde wallet.</summary>
    [HttpPost("convert-to-wallet")]
    public async Task<IActionResult> ConvertToWallet([FromBody] ConvertRequest request)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Unauthorized();

        var account = await EnsureAccount(user);
        if (request.Points <= 0) return BadRequest(new { error = "points-invalides" });
        if (request.Points > account.PointsBalance) return BadRequest(new { error = "points-insuffisants" });

        var kwdValue = RewardMath.PointsToKwd(_config, request.Points);

        account.PointsBalance -= request.Points;
        account.WalletBalance += kwdValue;
        account.UpdatedAt = DateTime.UtcNow;

        _context.PointsTransactions.Add(new PointsTransaction
        {
            UserId = user.Id,
            Amount = -request.Points,
            BalanceAfter = account.PointsBalance,
            Type = "redeem",
            Description = $"تحويل {request.Points} نقطة إلى المحفظة",
            Status = PointsStatuses.Confirmed,
        });

        _context.WalletTransactions.Add(new WalletTransaction
        {
            UserId = user.Id,
            Amount = kwdValue,
            BalanceAfter = account.WalletBalance,
            Type = "points_convert",
            Description = $"تحويل {request.Points} نقطة",
        });

        await _context.SaveChangesAsync();

        return Ok(new { account.PointsBalance, account.WalletBalance });
    }

    /// <summary>POST /api/rewards/apply-referral — saisir un code de parrainage.</summary>
    [HttpPost("apply-referral")]
    public async Task<IActionResult> ApplyReferral([FromBody] ReferralRequest request)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Unauthorized();

        var account = await EnsureAccount(user);

        if (account.ReferredByUserId != null) return BadRequest(new { error = "deja-parraine" });

        var code = RewardMath.NormalizeCode(request.Code);
        if (string.IsNullOrEmpty(code)) return BadRequest(new { error = "code-invalide" });
        if (code == account.ReferralCode) return BadRequest(new { error = "propre-code" });

        var referrer = await _context.RewardAccounts.FirstOrDefaultAsync(r => r.ReferralCode == code);
        if (referrer == null) return BadRequest(new { error = "code-introuvable" });
        if (referrer.UserId == user.Id) return BadRequest(new { error = "propre-code" });

        account.ReferredByUserId = referrer.UserId;
        account.UpdatedAt = DateTime.UtcNow;

        // Points de bienvenue pour le filleul.
        if (_config.WelcomePoints > 0)
        {
            account.PointsBalance += _config.WelcomePoints;
            _context.PointsTransactions.Add(new PointsTransaction
            {
                UserId = user.Id,
                Amount = _config.WelcomePoints,
                BalanceAfter = account.PointsBalance,
                Type = "welcome",
                Description = "هدية ترحيب",
                RelatedUserId = referrer.UserId,
                ExpiresAt = DateTime.UtcNow.AddMonths(_config.PointsExpiryMonths),
                Status = PointsStatuses.Confirmed,
            });
        }

        // Points de parrainage pour le parrain (plafond mensuel).
        var currentMonth = DateTime.UtcNow.ToString("yyyy-MM");
        if (referrer.MonthlyReferralMonth != currentMonth)
        {
            referrer.MonthlyReferralPoints = 0;
            referrer.MonthlyReferralMonth = currentMonth;
        }

        var pointsToGive = _config.SignupVerifiedPoints;
        if (referrer.MonthlyReferralPoints + pointsToGive > _config.MonthlyReferralPointsCap)
        {
            pointsToGive = Math.Max(0, _config.MonthlyReferralPointsCap - referrer.MonthlyReferralPoints);
        }

        if (pointsToGive > 0)
        {
            referrer.PointsBalance += pointsToGive;
            referrer.MonthlyReferralPoints += pointsToGive;
            referrer.ReferralCount++;
            referrer.UpdatedAt = DateTime.UtcNow;

            var referrerUser = await _userManager.FindByIdAsync(referrer.UserId);

            _context.PointsTransactions.Add(new PointsTransaction
            {
                UserId = referrer.UserId,
                Amount = pointsToGive,
                BalanceAfter = referrer.PointsBalance,
                Type = "referral_signup",
                Description = $"{user.FullName} سجّل برمزك",
                RelatedUserId = user.Id,
                RelatedUserName = user.FullName,
                ExpiresAt = DateTime.UtcNow.AddMonths(_config.PointsExpiryMonths),
                Status = PointsStatuses.Confirmed,
            });
        }

        await _context.SaveChangesAsync();

        return Ok(new
        {
            account.PointsBalance,
            referrerName = (await _userManager.FindByIdAsync(referrer.UserId))?.FullName
        });
    }

    /// <summary>GET /api/rewards/referral-link — le code et le lien de partage.</summary>
    [HttpGet("referral-link")]
    public async Task<IActionResult> ReferralLink()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Unauthorized();

        var account = await EnsureAccount(user);

        return Ok(new
        {
            code = account.ReferralCode,
            link = $"https://ruwadomra.com/invite/{account.ReferralCode}",
            name = user.FullName
        });
    }

    /// <summary>
    /// POST /api/rewards/preview-redeem — avant le paiement, calculer ce que donnerait
    /// l'utilisation de points et/ou du portefeuille.
    /// </summary>
    [HttpPost("preview-redeem")]
    public async Task<IActionResult> PreviewRedeem([FromBody] PreviewRedeemRequest request)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Unauthorized();

        var account = await EnsureAccount(user);

        var result = RewardMath.Redeem(_config, new RedeemRequest(
            request.BookingTotal,
            account.WalletBalance,
            account.PointsBalance,
            request.UseWallet,
            request.RequestedPoints));

        return Ok(new
        {
            result.BookingTotal,
            result.PointsUsed,
            result.PointsDiscount,
            result.WalletUsed,
            result.RemainingToPay,
            result.PointsEarned,
            result.MaxPointsAllowed,
            result.Notices,
            WalletBalance = account.WalletBalance,
            PointsBalance = account.PointsBalance
        });
    }

    // --- Helpers ---

    private async Task<RewardAccount> EnsureAccount(ApplicationUser user)
    {
        var account = await _context.RewardAccounts.FirstOrDefaultAsync(r => r.UserId == user.Id);
        if (account != null) return account;

        account = new RewardAccount
        {
            UserId = user.Id,
            ReferralCode = GenerateUniqueCode(),
        };
        _context.RewardAccounts.Add(account);
        await _context.SaveChangesAsync();
        return account;
    }

    private string GenerateUniqueCode()
    {
        for (var i = 0; i < 50; i++)
        {
            var code = RewardMath.MakeCode(max => RandomNumberGenerator.GetInt32(max));
            if (!_context.RewardAccounts.Any(r => r.ReferralCode == code)) return code;
        }
        throw new InvalidOperationException("Impossible de generer un code unique apres 50 essais.");
    }

    private async Task<string?> NextExpiry(string userId)
    {
        var next = await _context.PointsTransactions
            .Where(t => t.UserId == userId && t.Amount > 0 && !t.IsExpired && t.ExpiresAt != null)
            .OrderBy(t => t.ExpiresAt)
            .Select(t => t.ExpiresAt)
            .FirstOrDefaultAsync();
        return next?.ToString("MM/yyyy");
    }
}

public record ConvertRequest(int Points);
public record ReferralRequest(string Code);
public record PreviewRedeemRequest(decimal BookingTotal, bool UseWallet, int RequestedPoints);
