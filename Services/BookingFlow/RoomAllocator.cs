namespace RowadUmrahSystem.Web.Services.BookingFlow;

/// <summary>A traveler holding a bed. Infants have no bed and never appear here.</summary>
public sealed record BedOccupant(int BookingId, int TravelerId, string Gender, string Nationality, bool Locked = false);

/// <summary>One room of a pool. A family room is private: its empty beds are never resold.</summary>
public sealed class PoolRoom
{
    public PoolRoom(int number, int beds, bool family = false)
    {
        Number = number;
        Beds = beds;
        Family = family;
    }

    public int Number { get; }
    public int Beds { get; }
    public bool Family { get; set; }
    public bool Locked { get; set; }
    public List<BedOccupant> Occupants { get; } = new();

    /// <summary>The sex of a shared room is set by its first occupant.</summary>
    public string? Gender => Family ? null : Occupants.FirstOrDefault()?.Gender;
    public int Free => Family ? 0 : Beds - Occupants.Count;
    public bool IsEmpty => Occupants.Count == 0 && !Family;
    public bool Homogeneous => !Family && Occupants.Count > 0 && Occupants.Select(item => item.Nationality).Distinct().Count() == 1;
}

/// <summary>A move done by the nationality rebalancing, for the audit trail.</summary>
public sealed record BedMove(int TravelerId, int BookingId, int FromRoom, int ToRoom, string Reason);

/// <summary>
/// Rooms of one type in one hotel for one departure, and the placement rules of spec § 5:
/// family rooms are private; other travelers get a bed in a single-sex shared room, grouped by
/// nationality, and the pool is rebalanced by nationality after each payment or cancellation.
/// </summary>
public sealed class RoomPool
{
    public RoomPool(int capacity, int beds, IEnumerable<PoolRoom>? rooms = null)
    {
        Capacity = capacity;
        Beds = beds;
        Rooms = rooms?.OrderBy(room => room.Number).ToList() ?? new List<PoolRoom>();
    }

    /// <summary>Rooms of this type that Rowad holds for the departure.</summary>
    public int Capacity { get; }
    public int Beds { get; }
    public List<PoolRoom> Rooms { get; }

    public int FreeRooms => Capacity - Rooms.Count(room => !room.IsEmpty);

    /// <summary>Private rooms for a family: <paramref name="quantity"/> empty rooms, filled in order.</summary>
    public bool TryPlaceFamily(IReadOnlyList<BedOccupant> family, int quantity)
    {
        if (quantity <= 0) return family.Count == 0;
        if (FreeRooms < quantity) return false;

        var taken = new List<PoolRoom>();
        for (var index = 0; index < quantity; index++)
        {
            var room = TakeEmptyRoom();
            room.Family = true;
            taken.Add(room);
        }

        var cursor = 0;
        foreach (var occupant in family)
        {
            while (cursor < taken.Count && taken[cursor].Occupants.Count >= Beds) cursor++;
            if (cursor >= taken.Count)
            {
                foreach (var room in taken) Release(room);
                return false;
            }
            taken[cursor].Occupants.Add(occupant);
        }
        return true;
    }

    /// <summary>
    /// Beds in shared rooms for travelers of one booking and one sex. The block stays together
    /// when it can; otherwise each traveler is placed alone. Priority (spec § 5.3): same sex and
    /// same nationality with enough free beds, then an empty room, then same sex with another
    /// nationality; at equal priority the fullest room first. All or nothing.
    /// </summary>
    public bool TryPlaceShared(IReadOnlyList<BedOccupant> block)
    {
        if (block.Count == 0) return true;
        if (block.Select(item => item.Gender).Distinct().Count() != 1) throw new ArgumentException("A shared block has one sex.");

        var together = FindRoom(block);
        if (together != null)
        {
            together.Occupants.AddRange(block);
            return true;
        }

        var placed = new List<(PoolRoom Room, BedOccupant Occupant)>();
        foreach (var occupant in block)
        {
            var room = FindRoom(new[] { occupant });
            if (room == null)
            {
                foreach (var (from, item) in placed) from.Occupants.Remove(item);
                return false;
            }
            room.Occupants.Add(occupant);
            placed.Add((room, occupant));
        }
        return true;
    }

    public void RemoveBooking(int bookingId)
    {
        foreach (var room in Rooms.ToList())
        {
            room.Occupants.RemoveAll(item => item.BookingId == bookingId);
            if (room.Occupants.Count == 0) Release(room);
        }
    }

    /// <summary>
    /// Nationality rebalancing (spec § 5.4) inside this pool: moves a booking's block to a free bed
    /// of an occupied room of the same sex, or swaps two blocks of the same size, while that
    /// increases the number of single-nationality rooms, then lowers the nationalities per mixed
    /// room. Family rooms and locked travelers or rooms never move.
    /// </summary>
    public IReadOnlyList<BedMove> Rebalance(int maxSteps = 200)
    {
        var moves = new List<BedMove>();
        for (var step = 0; step < maxSteps; step++)
        {
            var current = Score();
            Action? best = null;
            Func<IEnumerable<BedMove>>? bestMoves = null;
            var bestScore = current;

            var shared = Rooms.Where(room => !room.Family && !room.Locked && room.Occupants.Count > 0).ToList();
            foreach (var from in shared)
            {
                foreach (var block in Blocks(from))
                {
                    // Move the block to a free bed of another occupied room of the same sex.
                    foreach (var to in shared.Where(room => room != from && room.Gender == from.Gender && room.Free >= block.Count))
                    {
                        Apply(block, from, to);
                        var score = Score();
                        Apply(block, to, from);
                        if (Better(score, bestScore))
                        {
                            bestScore = score;
                            var (b, f, t) = (block, from, to);
                            best = () => Apply(b, f, t);
                            bestMoves = () => b.Select(item => new BedMove(item.TravelerId, item.BookingId, f.Number, t.Number, "nationality-move"));
                        }
                    }

                    // Swap with a block of the same size in another room of the same sex.
                    foreach (var other in shared.Where(room => room.Number > from.Number && room.Gender == from.Gender))
                    {
                        foreach (var partner in Blocks(other).Where(item => item.Count == block.Count && item[0].BookingId != block[0].BookingId))
                        {
                            Swap(block, from, partner, other);
                            var score = Score();
                            Swap(block, other, partner, from);
                            if (Better(score, bestScore))
                            {
                                bestScore = score;
                                var (b, f, p, o) = (block, from, partner, other);
                                best = () => Swap(b, f, p, o);
                                bestMoves = () => b.Select(item => new BedMove(item.TravelerId, item.BookingId, f.Number, o.Number, "nationality-swap"))
                                    .Concat(p.Select(item => new BedMove(item.TravelerId, item.BookingId, o.Number, f.Number, "nationality-swap")));
                            }
                        }
                    }
                }
            }

            if (best == null) break;
            moves.AddRange(bestMoves!());
            best();
        }

        foreach (var room in Rooms.Where(room => room.Occupants.Count == 0 && !room.Family).ToList()) Release(room);
        return moves;
    }

    /// <summary>(single-nationality rooms, − nationalities summed over mixed rooms): higher is better.</summary>
    public (int Homogeneous, int MixedPenalty) Score()
    {
        var shared = Rooms.Where(room => !room.Family && room.Occupants.Count > 0).ToList();
        var homogeneous = shared.Count(room => room.Homogeneous);
        var penalty = shared.Where(room => !room.Homogeneous).Sum(room => room.Occupants.Select(item => item.Nationality).Distinct().Count());
        return (homogeneous, -penalty);
    }

    private static bool Better((int Homogeneous, int MixedPenalty) candidate, (int Homogeneous, int MixedPenalty) reference) =>
        candidate.Homogeneous > reference.Homogeneous ||
        (candidate.Homogeneous == reference.Homogeneous && candidate.MixedPenalty > reference.MixedPenalty);

    /// <summary>Travelers of one booking in a room move together; a locked traveler pins the block.</summary>
    private static IEnumerable<List<BedOccupant>> Blocks(PoolRoom room) => room.Occupants
        .GroupBy(item => item.BookingId)
        .Select(group => group.ToList())
        .Where(group => group.All(item => !item.Locked))
        .ToList();

    private static void Apply(List<BedOccupant> block, PoolRoom from, PoolRoom to)
    {
        foreach (var item in block) from.Occupants.Remove(item);
        to.Occupants.AddRange(block);
    }

    private static void Swap(List<BedOccupant> first, PoolRoom firstRoom, List<BedOccupant> second, PoolRoom secondRoom)
    {
        Apply(first, firstRoom, secondRoom);
        Apply(second, secondRoom, firstRoom);
    }

    private PoolRoom? FindRoom(IReadOnlyList<BedOccupant> block)
    {
        var gender = block[0].Gender;
        var nationalities = block.Select(item => item.Nationality).ToHashSet();
        var candidates = Rooms.Where(room => !room.Family && !room.Locked && room.Occupants.Count > 0 && room.Gender == gender && room.Free >= block.Count).ToList();

        var sameNationality = candidates
            .Where(room => room.Occupants.All(item => nationalities.Contains(item.Nationality)))
            .OrderBy(room => room.Free).ThenBy(room => room.Number).FirstOrDefault();
        if (sameNationality != null) return sameNationality;

        if (FreeRooms > 0 && block.Count <= Beds) return TakeEmptyRoom();

        return candidates.OrderBy(room => room.Free).ThenBy(room => room.Number).FirstOrDefault();
    }

    /// <summary>The lowest free room number (rooms are numbered 1..Capacity inside the pool).</summary>
    private PoolRoom TakeEmptyRoom()
    {
        var empty = Rooms.FirstOrDefault(room => room.IsEmpty && !room.Locked);
        if (empty != null) return empty;
        var used = Rooms.Select(room => room.Number).ToHashSet();
        var number = Enumerable.Range(1, Capacity + Rooms.Count).First(value => !used.Contains(value));
        var created = new PoolRoom(number, Beds);
        Rooms.Add(created);
        Rooms.Sort((a, b) => a.Number.CompareTo(b.Number));
        return created;
    }

    private void Release(PoolRoom room)
    {
        room.Occupants.Clear();
        room.Family = false;
        if (!room.Locked) Rooms.Remove(room);
    }
}
