using System.Text.Json.Serialization;
using IMRCRaceControl.Core;

namespace IMRCRaceControl.Models;

public sealed class EventRankingEntry
{
    public string ClassId { get; set; } = string.Empty;
    public string ClassName { get; set; } = string.Empty;
    public int Rank { get; set; }
    public string DriverId { get; set; } = string.Empty;
    public string DriverName { get; set; } = string.Empty;
    public int RoundsCompleted { get; set; }
    public int BestRoundPosition { get; set; }
    public int LatestRoundPosition { get; set; }
    public int TotalLaps { get; set; }
    public double? BestLapSeconds { get; set; }
    public double? BestRoundElapsedSeconds { get; set; }
    public double AverageRoundPosition { get; set; }

    [JsonIgnore]
    public string BestLapDisplay =>
        BestLapSeconds.HasValue
            ? RaceEngine.FormatLap(TimeSpan.FromSeconds(BestLapSeconds.Value))
            : "—";

    [JsonIgnore]
    public string BestRoundDisplay => BestRoundPosition <= 0 ? "—" : BestRoundPosition.ToString();

    [JsonIgnore]
    public string LatestRoundDisplay => LatestRoundPosition <= 0 ? "—" : LatestRoundPosition.ToString();

    [JsonIgnore]
    public string AverageRoundDisplay => RoundsCompleted <= 0 ? "—" : AverageRoundPosition.ToString("0.00");
}
