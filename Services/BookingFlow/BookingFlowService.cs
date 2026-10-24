using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RowadUmrahSystem.Web.Data;
using RowadUmrahSystem.Web.Models;

namespace RowadUmrahSystem.Web.Services.BookingFlow;

/// <summary>A traveler of a booking as the room allocation needs it (infants are skipped: no bed).</summary>
public sealed record AllocationTraveler(int TravelerId, string Kind, string Gender, string Nationality);

/// <summary>
/// Database side of the booking flow: seats per departure, payment holds, beds (placement and
/// nationality rebalancing, spec § 5) and the payment confirmation (spec § 7).
/// </summary>
public sealed class BookingFlowService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    // One allocation at a time: two payments starting together never take the same bed.
    private static readonly SemaphoreSlim AllocationLock = new(1, 1);

    private readonly ApplicationDbContext _context;
    private readonly BookingConfigStore _configStore;
    private readonly IAuthEmailSender _emailSender;
    private readonly ILogger<BookingFlowService> _logger;

    public BookingFlowService(ApplicationDbContext context, BookingConfigStore configStore, IAuthEmailSender emailSender, ILogger<BookingFlowService> logger)
    {
        _context = context;
        _configStore = configStore;
        _emailSender = emailSender;
        _logger = logger;
    }

    /// <summary>A booking takes seats and beds when paid, or while its payment hold runs.</summary>
    public static IQueryable<Booking> Holding(IQueryable<Booking> bookings, DateTime now) => bookings.Where(booking =>
        booking.Status == BookingStatuses.Confirmed ||
        (booking.Status == BookingStatuses.PendingPayment && booking.HoldExpiresAt != null && booking.HoldExpiresAt > now));

    /// <summary>Seats taken on a departure by men, women and children (infants travel on a lap).</summary>
    public async Task<Dictionary<string, int>> SeatsTakenAsync(string packageId, IEnumerable<string> departures, int? exceptBookingId = null)
    {
        var dates = departures.ToList();
        var now = DateTime.UtcNow;
        var rows = await Holding(_context.Bookings.AsNoTracking(), now)
            .Where(booking => booking.PackageId == packageId && booking.DepartureDate != null && dates.Contains(booking.DepartureDate) && booking.Id != (exceptBookingId ?? 0))
            .Select(booking => new { booking.DepartureDate, Seats = booking.Adults + booking.Children })
            .ToListAsync();
        return rows.GroupBy(row => row.DepartureDate!).ToDictionary(group => group.Key, group => group.Sum(row => row.Seats));
    }

    /// <summary>Rooms of each type still free in a hotel for a departure.</summary>
    public async Task<Dictionary<(string HotelId, string RoomType), int>> RoomsTakenAsync(string packageId, string departure, int? exceptBookingId = null)
    {
        var now = DateTime.UtcNow;
        var holding = Holding(_context.Bookings, now).Select(booking => booking.Id);
        var rooms = await _context.RoomAssignments.AsNoTracking()
            .Where(item => item.PackageId == packageId && item.DepartureDate == departure && holding.Contains(item.BookingId) && item.BookingId != (exceptBookingId ?? 0))
            .Select(item => new { item.HotelId, item.RoomType, item.City, item.RoomNumber })
            .Distinct()
            .ToListAsync();
        return rooms.GroupBy(item => (item.HotelId, item.RoomType)).ToDictionary(group => group.Key, group => group.Count());
    }

    /// <summary>Pending payments past their hold become "expired" and give back their beds.</summary>
    public async Task ReleaseExpiredHoldsAsync()
    {
        var now = DateTime.UtcNow;
        var expired = await _context.Bookings
            .Where(booking => booking.Status == BookingStatuses.PendingPayment && booking.PaymentStatus != BookingPaymentStatuses.Paid &&
                booking.HoldExpiresAt != null && booking.HoldExpiresAt <= now)
            .ToListAsync();
        if (expired.Count == 0) return;

        var ids = expired.Select(booking => booking.Id).ToList();
        _context.RoomAssignments.RemoveRange(_context.RoomAssignments.Where(item => ids.Contains(item.BookingId)));
        foreach (var booking in expired)
        {
            booking.Status = BookingStatuses.Expired;
            booking.HoldExpiresAt = null;
            booking.UpdatedAt = now;
        }
        await _context.SaveChangesAsync();
    }

    /// <summary>
    /// Holds beds for a booking (replacing any it had): private rooms for a family, otherwise
    /// beds in shared single-sex rooms. Returns an Arabic message when a room type is full.
    /// </summary>
    public async Task<string?> AllocateAsync(Booking booking, IReadOnlyList<AllocationTraveler> travelers, IReadOnlyList<DraftStay> stays, BookingConfig config)
    {
        await AllocationLock.WaitAsync();
        try
        {
            var existing = _context.RoomAssignments.Where(item => item.BookingId == booking.Id);
            _context.RoomAssignments.RemoveRange(existing);
            await _context.SaveChangesAsync();

            var sleepers = travelers.Where(item => BookingQuote.IsPaying(item.Kind)).ToList();
            var created = new List<RoomAssignment>();
            foreach (var stay in stays)
            {
                var hotel = config.Hotel(stay.HotelId);
                if (hotel == null) return "الفندق المختار غير متاح.";

                if (booking.FamilyMode)
                {
                    var queue = new Queue<AllocationTraveler>(sleepers);
                    foreach (var line in stay.Rooms.Where(item => item.Quantity > 0).OrderByDescending(item => hotel.Room(item.Type)?.Beds ?? 0))
                    {
                        var room = hotel.Room(line.Type);
                        if (room == null) return "نوع الغرفة غير متاح.";
                        var pool = await LoadPoolAsync(booking, stay.City, hotel.Id, room);
                        var family = new List<BedOccupant>();
                        while (queue.Count > 0 && family.Count < room.Beds * line.Quantity)
                        {
                            var traveler = queue.Dequeue();
                            family.Add(new BedOccupant(booking.Id, traveler.TravelerId, traveler.Gender, traveler.Nationality));
                        }
                        if (!pool.TryPlaceFamily(family, line.Quantity)) return FullMessage(hotel, room);
                        created.AddRange(ToAssignments(booking, stay.City, hotel.Id, room.Type, pool, family, isFamily: true));
                    }
                }
                else
                {
                    foreach (var gender in new[] { "male", "female" })
                    {
                        var block = sleepers.Where(item => item.Gender == gender)
                            .Select(item => new BedOccupant(booking.Id, item.TravelerId, gender, item.Nationality)).ToList();
                        if (block.Count == 0) continue;
                        var room = hotel.Room(gender == "male" ? stay.SharedBeds?.Men : stay.SharedBeds?.Women);
                        if (room == null) return "اختر نوع الغرفة المشتركة.";
                        var pool = await LoadPoolAsync(booking, stay.City, hotel.Id, room);
                        if (!pool.TryPlaceShared(block)) return FullMessage(hotel, room);
                        created.AddRange(ToAssignments(booking, stay.City, hotel.Id, room.Type, pool, block, isFamily: false));
                    }
                }
            }

            _context.RoomAssignments.AddRange(created);
            await _context.SaveChangesAsync();
            return null;
        }
        finally
        {
            AllocationLock.Release();
        }
    }

    /// <summary>
    /// Nationality rebalancing of every shared pool of a departure (after a payment or a
    /// cancellation), until the rooming list freezes. Each move is written to RoomMoveLogs.
    /// </summary>
    /// <summary>The rooming list freezes RoomingFreezeDaysBeforeDeparture days before departure.</summary>
    public static bool IsFrozen(string departure, BookingConfig config) =>
        DateOnly.TryParseExact(departure, "yyyy-MM-dd", out var date) &&
        date.AddDays(-config.RoomingFreezeDaysBeforeDeparture) <= Controllers.Api.PackageDepartures.TodayInKuwait();

    /// <summary>Runs a manual change of beds (back-office) under the same lock as the allocation.</summary>
    public async Task<T> WithBedsLockAsync<T>(Func<Task<T>> action)
    {
        await AllocationLock.WaitAsync();
        try
        {
            return await action();
        }
        finally
        {
            AllocationLock.Release();
        }
    }

    /// <summary>Cancels a booking: its seats and beds are freed and the shared rooms rebalanced.</summary>
    public async Task CancelAsync(Booking booking)
    {
        var config = await _configStore.ReadAsync();
        _context.RoomAssignments.RemoveRange(_context.RoomAssignments.Where(item => item.BookingId == booking.Id));
        booking.Status = BookingStatuses.Cancelled;
        booking.HoldExpiresAt = null;
        booking.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        if (booking.DepartureDate != null) await RebalanceAsync(booking.PackageId, booking.DepartureDate, config);
    }

    public async Task RebalanceAsync(string packageId, string departure, BookingConfig config)
    {
        if (IsFrozen(departure, config)) return;

        await AllocationLock.WaitAsync();
        try
        {
            var now = DateTime.UtcNow;
            var confirmed = _context.Bookings.Where(booking => booking.Status == BookingStatuses.Confirmed).Select(booking => booking.Id);
            var assignments = await _context.RoomAssignments
                .Where(item => item.PackageId == packageId && item.DepartureDate == departure && !item.IsFamily && confirmed.Contains(item.BookingId))
                .ToListAsync();

            foreach (var group in assignments.GroupBy(item => (item.City, item.HotelId, item.RoomType)))
            {
                var room = config.Hotel(group.Key.HotelId)?.Room(group.Key.RoomType);
                if (room == null) continue;
                var rooms = group.GroupBy(item => item.RoomNumber).Select(byRoom =>
                {
                    var poolRoom = new PoolRoom(byRoom.Key, room.Beds);
                    poolRoom.Occupants.AddRange(byRoom.Select(item => new BedOccupant(item.BookingId, item.TravelerId, item.Gender, item.Nationality, item.IsLocked)));
                    return poolRoom;
                });
                var pool = new RoomPool(room.RoomsPerDeparture, room.Beds, rooms);
                var moves = pool.Rebalance();
                if (moves.Count == 0) continue;

                var byTraveler = group.ToDictionary(item => (item.BookingId, item.TravelerId));
                foreach (var poolRoom in pool.Rooms)
                {
                    foreach (var occupant in poolRoom.Occupants)
                    {
                        var assignment = byTraveler[(occupant.BookingId, occupant.TravelerId)];
                        if (assignment.RoomNumber == poolRoom.Number) continue;
                        assignment.RoomNumber = poolRoom.Number;
                        assignment.UpdatedAt = now;
                    }
                }
                _context.RoomMoveLogs.AddRange(moves.Select(move => new RoomMoveLog
                {
                    BookingId = move.BookingId,
                    TravelerId = move.TravelerId,
                    PackageId = packageId,
                    DepartureDate = departure,
                    City = group.Key.City,
                    HotelId = group.Key.HotelId,
                    RoomType = group.Key.RoomType,
                    FromRoom = move.FromRoom,
                    ToRoom = move.ToRoom,
                    Reason = move.Reason,
                    MovedAt = now
                }));
            }
            await _context.SaveChangesAsync();
        }
        finally
        {
            AllocationLock.Release();
        }
    }

    /// <summary>
    /// Applies a status verified with the gateway (server to server; never the app's word).
    /// Paid with the right amount and reference: the booking is confirmed, beds are kept (placed
    /// again if the hold had expired), rooms are rebalanced and the traveler is told.
    /// </summary>
    public async Task ApplyPaymentStatusAsync(Booking booking, GatewayPaymentStatus status)
    {
        if (booking.PaymentStatus == BookingPaymentStatuses.Paid) return;
        var now = DateTime.UtcNow;

        if (status.Status == "Paid")
        {
            if (status.InvoiceValue != booking.TotalAmount || (status.CustomerReference != null && status.CustomerReference != booking.Reference))
            {
                _logger.LogError("Payment {Invoice} for {Reference}: amount {Paid} or reference {CustomerReference} does not match {Total}",
                    status.InvoiceId, booking.Reference, status.InvoiceValue, status.CustomerReference, booking.TotalAmount);
                booking.LastPaymentError = "مبلغ الدفع لا يطابق الحجز. تواصل مع روّاد.";
                booking.UpdatedAt = now;
                await _context.SaveChangesAsync();
                return;
            }

            var config = await _configStore.ReadAsync();
            booking.PaymentStatus = BookingPaymentStatuses.Paid;
            booking.Status = BookingStatuses.Confirmed;
            booking.PaidAt = now;
            booking.PaymentId = status.PaymentId;
            booking.PaymentInvoiceId = status.InvoiceId;
            booking.PaymentMethod = status.PaymentMethod ?? booking.PaymentMethod;
            booking.HoldExpiresAt = null;
            booking.LastPaymentError = null;
            booking.UpdatedAt = now;
            await _context.SaveChangesAsync();

            if (!await _context.RoomAssignments.AnyAsync(item => item.BookingId == booking.Id))
            {
                var problem = await AllocateAsync(booking, await AllocationTravelersAsync(booking.Id), Stays(booking), config);
                if (problem != null)
                {
                    _logger.LogWarning("Paid booking {Reference} without beds: {Problem}", booking.Reference, problem);
                    booking.NeedsRoomReview = true;
                    await _context.SaveChangesAsync();
                }
            }

            if (booking.DepartureDate != null) await RebalanceAsync(booking.PackageId, booking.DepartureDate, config);
            await NotifyConfirmedAsync(booking);
            return;
        }

        booking.LastPaymentError = status.Status == "Canceled" ? "أُلغيت عملية الدفع." : status.Error ?? "لم يكتمل الدفع.";
        booking.UpdatedAt = now;
        await _context.SaveChangesAsync();
    }

    public static IReadOnlyList<DraftStay> Stays(Booking booking) =>
        string.IsNullOrEmpty(booking.StaysJson) ? Array.Empty<DraftStay>() : JsonSerializer.Deserialize<List<DraftStay>>(booking.StaysJson, JsonOptions) ?? new List<DraftStay>();

    public async Task<IReadOnlyList<AllocationTraveler>> AllocationTravelersAsync(int bookingId) =>
        (await _context.BookingTravelers.AsNoTracking().Where(link => link.BookingId == bookingId)
            .Select(link => new { link.TravelerId, link.Kind, link.Gender, link.Nationality })
            .ToListAsync())
        .Select(link => new AllocationTraveler(link.TravelerId, link.Kind == "adult" ? (link.Gender == "female" ? "woman" : "man") : link.Kind, link.Gender ?? "male", link.Nationality ?? ""))
        .ToList();

    private async Task<RoomPool> LoadPoolAsync(Booking booking, string city, string hotelId, RoomTypeConfig room)
    {
        var now = DateTime.UtcNow;
        var departure = booking.DepartureDate ?? "";
        var holding = Holding(_context.Bookings, now).Select(item => item.Id);
        var assignments = await _context.RoomAssignments.AsNoTracking()
            .Where(item => item.PackageId == booking.PackageId && item.DepartureDate == departure && item.City == city && item.HotelId == hotelId &&
                item.RoomType == room.Type && item.BookingId != booking.Id && holding.Contains(item.BookingId))
            .ToListAsync();
        var rooms = assignments.GroupBy(item => item.RoomNumber).Select(group =>
        {
            var poolRoom = new PoolRoom(group.Key, room.Beds, group.Any(item => item.IsFamily)) { Locked = group.Any(item => item.IsLocked) };
            poolRoom.Occupants.AddRange(group.Select(item => new BedOccupant(item.BookingId, item.TravelerId, item.Gender, item.Nationality, item.IsLocked)));
            return poolRoom;
        });
        return new RoomPool(room.RoomsPerDeparture, room.Beds, rooms);
    }

    private static IEnumerable<RoomAssignment> ToAssignments(Booking booking, string city, string hotelId, string roomType, RoomPool pool, IReadOnlyList<BedOccupant> placed, bool isFamily)
    {
        var now = DateTime.UtcNow;
        foreach (var room in pool.Rooms)
        {
            foreach (var occupant in room.Occupants.Where(placed.Contains))
            {
                yield return new RoomAssignment
                {
                    BookingId = booking.Id,
                    TravelerId = occupant.TravelerId,
                    PackageId = booking.PackageId,
                    DepartureDate = booking.DepartureDate ?? "",
                    City = city,
                    HotelId = hotelId,
                    RoomType = roomType,
                    RoomNumber = room.Number,
                    Gender = occupant.Gender,
                    Nationality = occupant.Nationality,
                    IsFamily = isFamily,
                    CreatedAt = now,
                    UpdatedAt = now
                };
            }
        }
    }

    private static string FullMessage(HotelConfig hotel, RoomTypeConfig room) =>
        $"لم تعد هناك أسرّة متاحة في {RoomLabels.Of(room.Type)} في {hotel.Name} لهذا الموعد. اختر نوعاً آخر أو فندقاً آخر.";

    private async Task NotifyConfirmedAsync(Booking booking)
    {
        try
        {
            var notification = new Notification
            {
                Title = "تمّ تأكيد رحلتك",
                Message = $"رقم الحجز {booking.Reference}. نسأل الله أن يكتب لك عمرة مقبولة.",
                Type = "booking-confirmed",
                TargetGroup = $"user:{booking.UserId}",
                Url = $"rowadumrah://trip/{booking.Reference}"
            };
            _context.Notifications.Add(notification);
            await _context.SaveChangesAsync();
            _context.UserNotifications.Add(new UserNotification { UserId = booking.UserId, NotificationId = notification.Id });
            await _context.SaveChangesAsync();

            var email = await _context.Users.Where(user => user.Id == booking.UserId).Select(user => user.Email).FirstOrDefaultAsync();
            if (!string.IsNullOrWhiteSpace(email))
                await _emailSender.SendHtmlAsync(email, $"تمّ تأكيد رحلتك - {booking.Reference}", ConfirmationEmail(booking));
        }
        catch (Exception exception)
        {
            // The payment is confirmed whatever happens to the message.
            _logger.LogWarning(exception, "Confirmation message for {Reference} not sent", booking.Reference);
        }
    }

    private static string ConfirmationEmail(Booking booking)
    {
        var lines = string.IsNullOrEmpty(booking.PriceBreakdownJson)
            ? new List<QuoteLine>()
            : JsonSerializer.Deserialize<List<QuoteLine>>(booking.PriceBreakdownJson, JsonOptions) ?? new List<QuoteLine>();
        var rows = string.Join("", lines.Select(line =>
            $"<tr><td style=\"padding:6px 0\">{WebUtility.HtmlEncode(line.Label)}</td><td style=\"padding:6px 0;text-align:left\">{BookingQuote.Amount(line.Amount)} {WebUtility.HtmlEncode(booking.Currency)}</td></tr>"));
        return $$"""
            <div style="font-family:Arial,sans-serif;direction:rtl;text-align:right;color:#1B1A17">
              <p style="color:#8A6526">لبّيك اللهمّ عمرة</p>
              <h2>تمّ تأكيد رحلتك</h2>
              <p>نسأل الله أن يكتب لك عمرة مقبولة.</p>
              <p>رقم الحجز: <b style="direction:ltr">{{WebUtility.HtmlEncode(booking.Reference)}}</b></p>
              <p>{{WebUtility.HtmlEncode(booking.PackageName)}} · المغادرة {{WebUtility.HtmlEncode(booking.DepartureDate ?? "")}}</p>
              <table style="width:100%;border-top:1px solid #E2CDA2;margin-top:12px">{{rows}}
                <tr><td style="padding:8px 0;font-weight:700">الإجمالي</td><td style="padding:8px 0;text-align:left;font-weight:700">{{BookingQuote.Amount(booking.TotalAmount)}} {{WebUtility.HtmlEncode(booking.Currency)}}</td></tr>
              </table>
              <p>الخطوات القادمة: نُصدر التأشيرات ونُعلمك فوراً، ثم يتواصل معك مشرف الرحلة قبل السفر.</p>
            </div>
            """;
    }
}

public static class RoomLabels
{
    public static string Of(string type) => type switch
    {
        "double" => "غرفة ثنائية",
        "triple" => "غرفة ثلاثية",
        "quad" => "غرفة رباعية",
        "single" => "غرفة فردية",
        _ => type
    };

    public static string SharedBed(string type) => $"سرير في {Of(type)} مشتركة";

    /// <summary>"٢" for 2, in Arabic sentences.</summary>
    public static string ArabicNumber(int value) => string.Concat(value.ToString(System.Globalization.CultureInfo.InvariantCulture).Select(digit => (char)('٠' + (digit - '0'))));
}
