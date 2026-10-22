using IMRCRaceControl.Core;
namespace IMRCRaceControl;
public partial class MainWindow
{
    internal NextRacePreview? GetNextRacePreview() => _practiceActive ? null : NextRacePreview.Build(GetQueueSessions(), _loadedSession?.Id);
}
