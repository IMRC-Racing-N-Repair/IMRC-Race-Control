using IMRCRaceControl.Models;
namespace IMRCRaceControl.Core;
public sealed record NextRacePreview(string Heading, string Names)
{
    public static NextRacePreview Build(IEnumerable<EventSessionDefinition> source, string? currentId)
    {
        var races = source.Where(s => s.ProgressStatus != EventSessionStatus.Skipped).OrderBy(s => s.QueueOrder).ToList();
        var index = races.FindIndex(s => string.Equals(s.Id, currentId, StringComparison.OrdinalIgnoreCase));
        var next = index >= 0 ? races.Skip(index + 1).FirstOrDefault() : RaceSchedule.InitialRace(races);
        if (next is null) return new("NEXT RACE UP", index >= 0 ? "Final race of this round · No further races scheduled" : "No races scheduled");
        var session = next.SessionType == SessionType.Race ? $"{next.HeatMainDisplay}-Main" + (next.MainRunCount > 1 ? $" {next.MainLeg}/{next.MainRunCount}" : "")
            : $"Heat {next.Heat}/{races.Where(s => s.ClassId == next.ClassId && s.SessionType == next.SessionType && s.Round == next.Round).Select(s => s.Heat).DefaultIfEmpty(1).Max()}";
        return new($"NEXT UP · Race {next.RaceNumber} · {next.ClassName} · {next.RoundLabel}, {session}",
            next.Entrants.Count == 0 ? "Lineup pending" : string.Join("     •     ", next.Entrants.OrderBy(e => e.CarNumber).Select(e => $"{e.CarNumber}  {e.DisplayName}")));
    }
}
