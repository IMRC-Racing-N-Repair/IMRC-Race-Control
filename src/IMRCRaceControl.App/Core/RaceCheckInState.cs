using IMRCRaceControl.Models;
namespace IMRCRaceControl.Core;

public sealed class RaceCheckInState
{
    public string SessionId { get; private set; } = "";
    public HashSet<int> CheckedCars { get; } = [];

    public void SelectSession(string sessionId, bool restart = false)
    {
        if (!restart && string.Equals(SessionId, sessionId, StringComparison.OrdinalIgnoreCase)) return;
        SessionId = sessionId;
        CheckedCars.Clear();
    }

    public void Clear() { SessionId = ""; CheckedCars.Clear(); }

    public List<string> MissingNames(IEnumerable<RacerEntry> racers) => racers
        .Where(r => r.Enabled && !CheckedCars.Contains(r.CarNumber))
        .OrderBy(r => r.CarNumber).Select(r => r.DisplayName).ToList();

    public static string WarningMessage(IEnumerable<string> names) =>
        string.Join("\n", names.Select(name => $"{name} is not checked in."));
}
