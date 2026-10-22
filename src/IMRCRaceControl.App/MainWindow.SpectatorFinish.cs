using System.Collections.ObjectModel;
using System.Text.Json;
using IMRCRaceControl.Models;

namespace IMRCRaceControl;

public partial class MainWindow
{
    // Spectator output stays independent of operator navigation until NEXT RACE.
    private sealed record FinishedSpectatorFrame(string Header, string Subheader, string Event,
        string Clock, string State, string Fastest, string Projection,
        ObservableCollection<StandingRow> Rows, ObservableCollection<RoundProjectionRow> Positions,
        string WebJson);
    private FinishedSpectatorFrame? _finishedSpectatorFrame;

    private void HoldFinishedSpectatorFrame()
    {
        if (_finishedSpectatorFrame is not null || _recallMode || _practiceActive ||
            _raceEngine.PracticeMode || _raceEngine.State != RaceState.Complete) return;
        // Deep copies prevent later timing, practice and highlight updates changing the results.
        var rows = JsonSerializer.Deserialize<ObservableCollection<StandingRow>>(JsonSerializer.Serialize(Standings))!;
        var positions = JsonSerializer.Deserialize<ObservableCollection<RoundProjectionRow>>(JsonSerializer.Serialize(RoundProjectionRows))!;
        _finishedSpectatorFrame = new(SpectatorRaceHeader, SpectatorRaceSubheader, SpectatorEventText,
            SpectatorMasterClock, SpectatorRaceState, SpectatorFastestText, SpectatorProjectionText,
            rows, positions, BuildWebSnapshot());
    }

    private void ReleaseFinishedSpectatorFrame() => _finishedSpectatorFrame = null;
}
