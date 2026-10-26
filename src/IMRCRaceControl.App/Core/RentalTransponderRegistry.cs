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
    public static int NextLabelNumber(RentalTransponderState state, string? excludingId = null)
    {
        var used = state.Inventory.Where(x => x.Id != excludingId && x.LabelNumber > 0).Select(x => x.LabelNumber).ToHashSet();
        for (var n = 1; ; n++) if (!used.Contains(n)) return n;
    }
    public static void NormalizeDefaults(RentalTransponderState state)
    {
        state.Inventory ??= []; state.History ??= [];
        foreach (var rental in state.Inventory)
            if (rental.Nickname == $"Transponder {rental.Number}***") rental.Nickname = DefaultNickname(rental.Number);
        if (state.NewNicknameStyle is not ("Transponder" or "ColorNumber" or "Custom")) state.NewNicknameStyle = "Transponder";
        if (state.NewLabelColor != "Random" && !Colors.Contains(state.NewLabelColor)) state.NewLabelColor = "Random";
        if (string.IsNullOrWhiteSpace(state.CustomNicknamePattern)) state.CustomNicknamePattern = "Rental #{n}";
        if (state.PrintFormat is not ("Nickname" or "NicknameAndNumber" or "Number")) state.PrintFormat = "Nickname";
        state.PrintFontSize = double.IsFinite(state.PrintFontSize) ? Math.Clamp(state.PrintFontSize, 8, 18) : 12;
        // Older drafts allowed the same ordinal in different colors. Preserve the
        // first, renumber later automatic labels, and leave historical snapshots intact.
        var pairs = new HashSet<(string, int)>();
        foreach (var rental in state.Inventory.Where(x => x.Color != "Default" && x.LabelNumber > 0))
            if (!pairs.Add((rental.Color, rental.LabelNumber))) throw new InvalidDataException("Duplicate rental color/number label. Existing data has been preserved.");
        var reserved = state.Inventory.Where(x => x.LabelNumber > 0).Select(x => x.LabelNumber).ToHashSet();
        var seen = new HashSet<int>();
        var names = state.Inventory.Select(x => Nickname(x.Nickname)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var rental in state.Inventory.Where(x => x.LabelNumber > 0))
        {
            if (seen.Add(rental.LabelNumber)) continue;
            var automatic = rental.Color != "Default" && rental.Nickname == $"{rental.Color} #{rental.LabelNumber}";
            var next = 1;
            while (reserved.Contains(next) || (automatic && names.Contains($"{rental.Color} #{next}"))) next++;
            if (automatic) { names.Remove(rental.Nickname); rental.Nickname = $"{rental.Color} #{next}"; names.Add(rental.Nickname); }
            rental.LabelNumber = next; reserved.Add(next); seen.Add(next);
        }
    }
    public static string GeneratedNickname(RentalTransponderState state, string number, string color, int labelNumber)
    {
        if (state.NewNicknameStyle == "ColorNumber") return $"{color} #{labelNumber}";
        if (state.NewNicknameStyle != "Custom") return DefaultNickname(number);
        var pattern = state.CustomNicknamePattern;
        if (!pattern.Contains("{n}", StringComparison.Ordinal) && !pattern.Contains("{number}", StringComparison.Ordinal)) pattern += " #{n}";
        return Nickname(pattern.Replace("{n}", labelNumber.ToString()).Replace("{number}", number).Replace("{color}", color));
    }
    public static string Nickname(string value) => string.Join(" ", value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    public static string DefaultNickname(string number) => $"{number}***";
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
        if (candidate.LabelNumber > 0 && state.Inventory.Any(x => x.Id != candidate.Id && x.LabelNumber == candidate.LabelNumber))
            throw new InvalidOperationException($"Rental label number {candidate.LabelNumber} is already in use. Each number is unique across all colors.");
    }
    public static void ValidateState(RentalTransponderState state)
    {
        NormalizeDefaults(state);
        var ordinals = new HashSet<int>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var numbers = new HashSet<string>(StringComparer.Ordinal);
        var nicknames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var labels = new HashSet<(string, int)>();
        foreach (var rental in state.Inventory)
        {
            if (string.IsNullOrWhiteSpace(rental.Id) || !ids.Add(rental.Id)) throw new InvalidDataException("Duplicate rental record identity. Rental data has not been discarded.");
            if (!numbers.Add(Number(rental.Number))) throw new InvalidDataException($"Duplicate rental transponder number: {rental.Number}.");
            if (string.IsNullOrWhiteSpace(rental.Nickname) || !nicknames.Add(Nickname(rental.Nickname))) throw new InvalidDataException($"Empty or duplicate rental nickname: {rental.Nickname}.");
            if (rental.LabelNumber > 0 && !ordinals.Add(rental.LabelNumber)) throw new InvalidDataException("Duplicate rental label number.");
            if (rental.Color != "Default" && (!Colors.Contains(rental.Color) || rental.LabelNumber < 1 || !labels.Add((rental.Color, rental.LabelNumber))))
                throw new InvalidDataException($"Invalid or duplicate rental label: {rental.Color} #{rental.LabelNumber}.");
        }
    }

    public static RentalTransponder Add(AppSettings settings, string number, string nickname = "")
    {
        var state = settings.RentalTransponders;
        NormalizeDefaults(state);
        var color = state.NewLabelColor == "Random" ? Colors[Random.Shared.Next(Colors.Length)] : state.NewLabelColor;
        var rental = new RentalTransponder { Number = Number(number), Color = state.NewNicknameStyle == "Transponder" ? "Default" : color, LabelNumber = NextLabelNumber(state) };
        rental.Nickname = string.IsNullOrWhiteSpace(nickname) ? GeneratedNickname(state, rental.Number, color, rental.LabelNumber) : Nickname(nickname);
        var previous = state.History.FirstOrDefault(h => h.TransponderNumber.TrimStart('0') == rental.Number);
        if (previous is not null && !state.Inventory.Any(r => r.Id == previous.RentalId)) rental.Id = previous.RentalId;
        Validate(settings.RentalTransponders, rental);
        settings.RentalTransponders.Inventory.Add(rental);
        CaptureAssignments(settings);
        return rental;
    }
    public static void Remove(AppSettings settings, string id)
    {
        var state = settings.RentalTransponders;
        var rental = state.Inventory.Single(x => x.Id == id);
        CaptureAssignments(settings);
        state.Inventory.Remove(rental);
        foreach (var use in state.History.Where(x => x.RentalId == id && x.ReturnedAt is null))
            use.ReturnedAt = DateTimeOffset.Now;
    }
    public static void SetLabel(AppSettings settings, string id, string color, int number, string customNickname = "")
    {
        if (number < 0) throw new InvalidOperationException("Label numbers must be positive.");
        var old = settings.RentalTransponders.Inventory.Single(x => x.Id == id);
        var updated = new RentalTransponder { Id = old.Id, Number = old.Number, Color = color, LabelNumber = number > 0 ? number : NextLabelNumber(settings.RentalTransponders, id) };
        updated.Nickname = !string.IsNullOrWhiteSpace(customNickname) ? Nickname(customNickname) :
            color == "Default" ? DefaultNickname(old.Number) : $"{color} #{updated.LabelNumber}";
        Validate(settings.RentalTransponders, updated);
        settings.RentalTransponders.Inventory[settings.RentalTransponders.Inventory.IndexOf(old)] = updated;
    }
    public static (string Color, int Number) RandomLabel(AppSettings settings, string id) =>
        (Colors[Random.Shared.Next(Colors.Length)], NextLabelNumber(settings.RentalTransponders, id));
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
        var currentKeys = new HashSet<(string, string, string, string)>();
        foreach (var ev in settings.Events)
        {
            void Capture(string driverId, string classId, string numbers, string? name = null, bool completed = false)
            {
                foreach (var token in Split(numbers).Select(x => x.TrimStart('0')).Distinct())
                {
                    if (!inventory.TryGetValue(token, out var rental)) continue;
                    var key = (rental.Id, ev.Id, driverId, classId);
                    var isCurrent = !completed && ev.Status == EventStatus.Active;
                    if (isCurrent) currentKeys.Add(key);
                    if (known.TryGetValue(key, out var previous))
                    {
                        if (ev.Status == EventStatus.Completed) foreach (var row in previous.Where(x => x.ReturnedAt is null)) row.ReturnedAt = ev.CompletedAt ?? DateTimeOffset.Now;
                        if (!isCurrent || previous.Any(x => x.ReturnedAt is null)) continue;
                    }
                    var use = Snapshot(settings, rental, ev, driverId, classId, name);
                    if (completed) use.ReturnedAt ??= DateTimeOffset.Now;
                    state.History.Add(use);
                    if (!known.TryGetValue(key, out var uses)) known[key] = uses = [];
                    uses.Add(use);
                }
            }
            foreach (var assignment in ev.DriverAssignments) Capture(assignment.DriverId, assignment.ClassId, assignment.TransponderId);
            foreach (var session in ev.Sessions.Where(s => s.ProgressStatus == EventSessionStatus.Completed))
                foreach (var entrant in session.Entrants) Capture(entrant.DriverId, session.ClassId, entrant.TransponderId, entrant.DisplayName, true);
        }
        foreach (var use in state.History.Where(h => h.ReturnedAt is null && !currentKeys.Contains((h.RentalId, h.EventId, h.DriverId, h.ClassId))))
            use.ReturnedAt = DateTimeOffset.Now;
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
