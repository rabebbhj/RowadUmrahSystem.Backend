using Microsoft.EntityFrameworkCore;
using RowadUmrahSystem.Web.Data;
using RowadUmrahSystem.Web.Models;

namespace RowadUmrahSystem.Web.Services.BookingFlow;

/// <summary>
/// Hourly booking chores (spec § 7.2 and § 8.1): gives back the beds of expired payment holds,
/// tells travelers when their rooming list is frozen (room number now visible) and reminds them
/// the day before departure. Each message is sent once per booking.
/// </summary>
public sealed class BookingRemindersService : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<BookingRemindersService> _logger;

    public BookingRemindersService(IServiceScopeFactory scopes, ILogger<BookingRemindersService> logger)
    {
        _scopes = scopes;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Let the startup migrations finish first.
        try { await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken); } catch (OperationCanceledException) { return; }
        using var timer = new PeriodicTimer(TimeSpan.FromHours(1));
        do
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                _logger.LogWarning(exception, "Booking reminders failed");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    public async Task RunOnceAsync(CancellationToken cancellationToken = default)
    {
        using var scope = _scopes.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var flow = scope.ServiceProvider.GetRequiredService<BookingFlowService>();
        var config = await scope.ServiceProvider.GetRequiredService<BookingConfigStore>().ReadAsync();
        await flow.ReleaseExpiredHoldsAsync();

        var today = Controllers.Api.PackageDepartures.TodayInKuwait();
        var tomorrow = today.AddDays(1).ToString("yyyy-MM-dd");
        var freezeLimit = today.AddDays(config.RoomingFreezeDaysBeforeDeparture).ToString("yyyy-MM-dd");
        var todayText = today.ToString("yyyy-MM-dd");
        var bookings = await context.Bookings.AsNoTracking()
            .Where(booking => booking.Status == BookingStatuses.Confirmed && booking.DepartureDate != null &&
                string.Compare(booking.DepartureDate, todayText) > 0 && string.Compare(booking.DepartureDate, freezeLimit) <= 0)
            .Select(booking => new { booking.Id, booking.UserId, booking.Reference, booking.DepartureDate })
            .ToListAsync(cancellationToken);

        foreach (var booking in bookings)
        {
            await SendOnceAsync(context, booking.UserId, $"booking:{booking.Id}:rooming", "غرفتك جاهزة",
                "اكتمل توزيع الغرف لرحلتك: رقم غرفتك ظاهر الآن في تفاصيل الحجز.", booking.Reference, cancellationToken);
            if (booking.DepartureDate == tomorrow)
                await SendOnceAsync(context, booking.UserId, $"booking:{booking.Id}:eve", "رحلتك غداً",
                    "غداً تبدأ رحلتك إلى بيت الله. راجع موعد الباص ومكانه في تفاصيل الحجز، ويتواصل معك مشرف الرحلة.", booking.Reference, cancellationToken);
        }
    }

    private static async Task SendOnceAsync(ApplicationDbContext context, string userId, string key, string title, string message, string reference, CancellationToken cancellationToken)
    {
        if (await context.Notifications.AnyAsync(item => item.TargetGroup == key, cancellationToken)) return;
        var notification = new Notification { Title = title, Message = message, Type = "booking", TargetGroup = key, Url = $"rowadumrah://trip/{reference}" };
        context.Notifications.Add(notification);
        await context.SaveChangesAsync(cancellationToken);
        context.UserNotifications.Add(new UserNotification { UserId = userId, NotificationId = notification.Id });
        await context.SaveChangesAsync(cancellationToken);
    }
}
