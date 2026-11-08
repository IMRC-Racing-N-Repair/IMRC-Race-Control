using System.Text.Json;
using IMRCRaceControl.Models;

namespace IMRCRaceControl.Dialogs;

public partial class RoundMainBuilderDialog
{
    private sealed record LineupScope(string ClassId, SessionType Type, int Round)
    {
        public static LineupScope For(EventSessionDefinition session) => new(session.ClassId, session.SessionType,
            session.SessionType == SessionType.Race ? 1 : session.Round);
    }

    private readonly Dictionary<LineupScope,List<EventSessionDefinition>> _stagedLineups = new();
    private List<EventSessionDefinition> PreviewSessions => _stagedLineups.Count == 0 ? _raceEvent.Sessions :
        _raceEvent.Sessions.Where(s => !_stagedLineups.ContainsKey(LineupScope.For(s)))
            .Concat(_stagedLineups.Values.SelectMany(s => s)).ToList();

    private bool CurrentLineupDirty => !_browsingSavedLineups &&
        (!_editingExistingScope && HeatPlans.Count > 0 ||
         _lineupSnapshot is not null && _lineupSnapshot != JsonSerializer.Serialize(HeatPlans));
    private bool HasPendingEdits => HasPendingSchedule || _stagedLineups.Count > 0 || CurrentLineupDirty;
    public bool ReplacesLineup(EventSessionDefinition session) => _stagedLineups.ContainsKey(LineupScope.For(session));

    private void StageCurrentLineup()
    {
        if (!CurrentLineupDirty) return;
        var type = CurrentMode == BuildMode.QualifyingRound ? SessionType.Qualifying : SessionType.Race;
        var round = type == SessionType.Qualifying ? CurrentRound() : 1;
        var built = BuildCurrentLineup();
        foreach (var cls in SelectedClasses())
            _stagedLineups[new LineupScope(cls.Id,type,round)] = built.Where(s => s.ClassId == cls.Id).ToList();
        RememberLineup();
    }

    private void PrepareSaveBatch()
    {
        StageCurrentLineup();
        BuiltSessions = _stagedLineups.Values.SelectMany(s=>s).ToList();
        OrderOnlySave = _stagedLineups.Count == 0;
    }
}
