using IMRCRaceControl.Models;

namespace IMRCRaceControl.Core;

public sealed record PracticeCrossing(PracticeCardViewModel Card, bool Created, bool LapAdded, bool DriverResolved);

public static class PracticeTracker
{
    public static string UnknownId(string tag) => "unknown:" + tag.Trim().ToUpperInvariant();

    public static PracticeCrossing Record(IList<PracticeCardViewModel> cards, DriverProfile? driver,
        string transponder, string source, DateTimeOffset timestamp)
    {
        transponder = transponder.Trim();
        if (transponder.Length == 0) throw new ArgumentException("Transponder is required.");
        var unknownId = UnknownId(transponder);
        var identity = driver?.Id ?? unknownId;
        var card = cards.FirstOrDefault(c => !c.IsSaving && c.DriverId == unknownId)
            ?? cards.FirstOrDefault(c => !c.IsSaving && c.DriverId == identity);
        if (card is null)
        {
            card = new PracticeCardViewModel
            {
                DriverId = identity, DriverName = driver?.DisplayName ?? "Unknown driver",
                TransponderId = transponder, StartedAt = timestamp, LastCrossingAt = timestamp
            };
            cards.Add(card);
            return new(card, true, false, driver is not null);
        }
        var resolved = driver is not null && card.DriverId == unknownId;
        if (resolved) card.ResolveDriver(driver!.Id, driver.DisplayName);
        var lap = timestamp - card.LastCrossingAt;
        if (lap < TimeSpan.FromMilliseconds(250)) return new(card, false, false, resolved);
        card.LastCrossingAt = timestamp;
        card.AddLap(new PracticeLapEntry
        {
            LapNumber = card.LapCount + 1, LapTime = lap, Timestamp = timestamp, Source = source
        });
        return new(card, false, true, resolved);
    }
}
