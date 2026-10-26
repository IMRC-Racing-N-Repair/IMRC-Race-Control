using IMRCRaceControl.Models;

namespace IMRCRaceControl.Core;

public static class RentalTransponderRegistry
{
    public static readonly string[] Colors = ["Red", "Black", "Blue", "Green", "Yellow", "Orange", "Purple", "Pink"];
    public static string Number(string value)
    {
        value = value.Trim();
        if (value.Length == 0 || value.Any(c => c is < '0' or > '9'))
            throw new InvalidOperationException("Enter a transponder number using digits only.");
        var normalized = value.TrimStart('0');
        if (normalized.Length == 0) throw new InvalidOperationException("A transponder number must be greater than zero.");
        return normalized;
    }
    public static string Nickname(string value) => string.Join(" ", value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    public static string DefaultNickname(string number) => $"Transponder {number}***";
    public static IEnumerable<string> Split(string? value) => (value ?? "").Split([';', ',', ' ', '\r', '\n', '\t'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
    private static bool Matches(string a, string b) => a.TrimStart('0').Equals(b.TrimStart('0'), StringComparison.OrdinalIgnoreCase);

    private static void Validate(RentalTransponderState state, RentalTransponder candidate)
    {
        if (state.Inventory.Any(x => x.Id != candidate.Id && Number(x.Number) == Number(candidate.Number)))
            throw new InvalidOperationException($"Transponder {candidate.Number} is already in the rental inventory. Select its existing record.");
        if (string.IsNullOrWhiteSpace(candidate.Nickname)) throw new InvalidOperationException("Enter a nickname.");
        if (state.Inventory.Any(x => x.Id != candidate.Id && Nickname(x.Nickname).Equals(Nickname(candidate.Nickname), StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"The nickname '{candidate.Nickname}' is already in use. Choose a unique nickname.");
        if (candidate.Color != "Default" && (!Colors.Contains(candidate.Color) || candidate.LabelNumber < 1))
            throw new InvalidOperationException("Select a label color and a number greater than zero.");
        if (candidate.Color != "Default" && state.Inventory.Any(x => x.Id != candidate.Id && x.Color == candidate.Color && x.LabelNumber == candidate.LabelNumber))
            throw new InvalidOperationException($"{candidate.Color} #{candidate.LabelNumber} is already in use. Choose another color/number combination.");
    }
    public static void ValidateState(RentalTransponderState state)
    {
        state.Inventory ??= []; state.History ??= [];
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var numbers = new HashSet<string>(StringComparer.Ordinal);
        var nicknames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var labels = new HashSet<(string, int)>();
        foreach (var rental in state.Inventory)
        {
            if (string.IsNullOrWhiteSpace(rental.Id) || !ids.Add(rental.Id)) throw new InvalidDataException("Duplicate rental record identity. Rental data has not been discarded.");
            if (!numbers.Add(Number(rental.Number))) throw new InvalidDataException($"Duplicate rental transponder number: {rental.Number}.");
            if (string.IsNullOrWhiteSpace(rental.Nickname) || !nicknames.Add(Nickname(rental.Nickname))) throw new InvalidDataException($"Empty or duplicate rental nickname: {rental.Nickname}.");
            if (rental.Color != "Default" && (!Colors.Contains(rental.Color) || rental.LabelNumber < 1 || !labels.Add((rental.Color, rental.LabelNumber))))
                throw new InvalidDataException($"Invalid or duplicate rental label: {rental.Color} #{rental.LabelNumber}.");
        }
    }

    public static RentalTransponder Add(AppSettings settings, string number, string nickname = "")
    {
        var rental = new RentalTransponder { Number = Number(number) };
        rental.Nickname = string.IsNullOrWhiteSpace(nickname) ? DefaultNickname(rental.Number) : Nickname(nickname);
        Validate(settings.RentalTransponders, rental);
        settings.RentalTransponders.Inventory.Add(rental);
        CaptureAssignments(settings);
        return rental;
    }
    public static void SetLabel(AppSettings settings, string id, string color, int number, string customNickname = "")
    {
        var old = settings.RentalTransponders.Inventory.Single(x => x.Id == id);
        var updated = new RentalTransponder { Id = old.Id, Number = old.Number, Color = color, LabelNumber = color == "Default" ? 0 : number };
        updated.Nickname = !string.IsNullOrWhiteSpace(customNickname) ? Nickname(customNickname) :
            color == "Default" ? DefaultNickname(old.Number) : $"{color} #{number}";
        Validate(settings.RentalTransponders, updated);
        settings.RentalTransponders.Inventory[settings.RentalTransponders.Inventory.IndexOf(old)] = updated;
    }
    public static (string Color, int Number) RandomLabel(AppSettings settings, string id)
    {
        var state = settings.RentalTransponders;
        bool Available(string color, int n) => !state.Inventory.Any(x =>
            (x.Color == color && x.LabelNumber == n) || Nickname(x.Nickname).Equals($"{color} #{n}", StringComparison.OrdinalIgnoreCase));
        for (var i = 0; i < 64; i++)
        {
            var color = Colors[Random.Shared.Next(Colors.Length)];
            var n = Random.Shared.Next(1, Math.Max(101, state.Inventory.Count + 2));
            if (Available(color, n)) return (color, n);
        }
        // There is always a free label among count+1 candidates; no finite label pool.
        for (var n = 1; ; n++) foreach (var color in Colors) if (Available(color, n)) return (color, n);
    }
    public static IReadOnlyList<RentalTransponderUse> History(AppSettings settings, string id, int? lastEvents = null)
    {
        var rows = settings.RentalTransponders.History.Where(x => x.RentalId == id)
            .OrderByDescending(x => x.EventDate).ThenByDescending(x => x.AssignedAt).ToList();
        if (lastEvents is null) return rows;
        var events = rows.Select(x => x.EventId).Distinct().Take(lastEvents.Value).ToHashSet();
        return rows.Where(x => events.Contains(x.EventId)).ToList();
    }
    private static RentalTransponderUse Snapshot(AppSettings settings, RentalTransponder rental, RaceDayEventDefinition ev, string driverId, string classId, string? driverName = null) => new()
    {
        RentalId = rental.Id, TransponderNumber = rental.Number, RentalNickname = rental.Nickname,
        EventId = ev.Id, EventName = ev.Name, EventDate = ev.EventDate.Date,
        DriverId = driverId, DriverName = settings.Drivers.FirstOrDefault(x => x.Id == driverId)?.DisplayName ?? driverName ?? "Unknown racer",
        ClassId = classId, ClassName = settings.Classes.FirstOrDefault(x => x.Id == classId)?.Name ?? "Unknown class",
        ReturnedAt = ev.Status == EventStatus.Completed ? ev.CompletedAt ?? DateTimeOffset.Now : null
    };
    public static void CaptureAssignments(AppSettings settings)
    {
        var state = settings.RentalTransponders;
        var inventory = state.Inventory.ToDictionary(x => Number(x.Number), StringComparer.Ordinal);
        var known = state.History.GroupBy(h => (h.RentalId, h.EventId, h.DriverId, h.ClassId)).ToDictionary(g => g.Key, g => g.ToList());
        foreach (var ev in settings.Events)
        {
            void Capture(string driverId, string classId, string numbers, string? name = null, bool completed = false)
            {
                foreach (var token in Split(numbers).Select(x => x.TrimStart('0')).Distinct())
                {
                    if (!inventory.TryGetValue(token, out var rental)) continue;
                    var key = (rental.Id, ev.Id, driverId, classId);
                    if (known.TryGetValue(key, out var previous))
                    {
                        if (ev.Status == EventStatus.Completed) foreach (var row in previous.Where(x => x.ReturnedAt is null)) row.ReturnedAt = ev.CompletedAt ?? DateTimeOffset.Now;
                        continue;
                    }
                    var use = Snapshot(settings, rental, ev, driverId, classId, name);
                    if (completed) use.ReturnedAt ??= DateTimeOffset.Now;
                    state.History.Add(use);
                    known[key] = [use];
                }
            }
            foreach (var assignment in ev.DriverAssignments) Capture(assignment.DriverId, assignment.ClassId, assignment.TransponderId);
            foreach (var session in ev.Sessions.Where(s => s.ProgressStatus == EventSessionStatus.Completed))
                foreach (var entrant in session.Entrants) Capture(entrant.DriverId, session.ClassId, entrant.TransponderId, entrant.DisplayName, true);
        }
    }
    public static void Assign(AppSettings settings, string rentalId, string eventId, string driverId, string classId)
    {
        var rental = settings.RentalTransponders.Inventory.Single(x => x.Id == rentalId);
        var ev = settings.Events.Single(x => x.Id == eventId);
        if (ev.Status != EventStatus.Active) throw new InvalidOperationException("Choose an active event. Completed events are kept as history.");
        if (!ev.ClassIds.Contains(classId)) throw new InvalidOperationException("Choose a class included in this event.");
        var assignment = ev.DriverAssignments.FirstOrDefault(x => x.DriverId == driverId && x.ClassId == classId)
            ?? throw new InvalidOperationException("Register this racer in the event/class before assigning a rental.");
        var active = settings.RentalTransponders.History.Where(x => x.RentalId == rentalId && x.ReturnedAt is null).ToList();
        if (active.Any(x => x.EventId != eventId || x.DriverId != driverId))
            throw new InvalidOperationException($"This rental is assigned to {active[0].DriverName} at {active[0].EventName}. Return it before reassigning it.");
        if (ev.DriverAssignments.Any(x => x.DriverId != driverId && x.ClassId == classId && Split(x.TransponderId).Any(n => Matches(n, rental.Number))))
            throw new InvalidOperationException("This transponder is already assigned to another racer in this class.");
        var numbers = Split(assignment.TransponderId).ToList();
        if (!numbers.Any(n => Matches(n, rental.Number))) numbers.Add(rental.Number);
        assignment.TransponderId = string.Join(";", numbers);
        UpdateFuture(ev, assignment);
        if (!active.Any(x => x.EventId == eventId && x.DriverId == driverId && x.ClassId == classId))
            settings.RentalTransponders.History.Add(Snapshot(settings, rental, ev, driverId, classId));
    }
    public static void Return(AppSettings settings, string rentalId)
    {
        var rental = settings.RentalTransponders.Inventory.Single(x => x.Id == rentalId);
        foreach (var ev in settings.Events.Where(e => e.Status == EventStatus.Active))
            foreach (var a in ev.DriverAssignments.Where(a => Split(a.TransponderId).Any(n => Matches(n, rental.Number))))
            {
                a.TransponderId = string.Join(";", Split(a.TransponderId).Where(n => !Matches(n, rental.Number)));
                UpdateFuture(ev, a);
            }
        foreach (var use in settings.RentalTransponders.History.Where(x => x.RentalId == rentalId && x.ReturnedAt is null)) use.ReturnedAt = DateTimeOffset.Now;
    }
    private static void UpdateFuture(RaceDayEventDefinition ev, EventDriverAssignment a)
    {
        foreach (var session in ev.Sessions.Where(s => s.ClassId == a.ClassId && s.ProgressStatus is EventSessionStatus.Planned or EventSessionStatus.Loaded))
            foreach (var entrant in session.Entrants.Where(e => e.DriverId == a.DriverId)) entrant.TransponderId = a.TransponderId;
    }
}
