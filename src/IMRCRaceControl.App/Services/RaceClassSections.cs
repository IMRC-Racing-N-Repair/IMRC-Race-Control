using System.Collections;
using System.Windows.Data;
using IMRCRaceControl.Models;

namespace IMRCRaceControl.Services;

internal static class RaceClassSections
{
    // Keep the source rows and their scoring order intact. Views provide the section headers.
    internal static void Attach(IEnumerable rows)
    {
        var groups = CollectionViewSource.GetDefaultView(rows).GroupDescriptions;
        if (groups.Count == 0) groups.Add(new PropertyGroupDescription(nameof(StandingRow.ClassGroupName)));
    }
}
