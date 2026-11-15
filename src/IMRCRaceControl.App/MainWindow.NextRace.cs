using IMRCRaceControl.Core;
namespace IMRCRaceControl;
public partial class MainWindow
{
    internal NextRacePreview? GetNextRacePreview() =>
        _practiceActive || _activeEvent?.Status != Models.EventStatus.Active
            ? null
            : NextRacePreview.Build(GetQueueSessions(), _loadedSession?.Id);
}
