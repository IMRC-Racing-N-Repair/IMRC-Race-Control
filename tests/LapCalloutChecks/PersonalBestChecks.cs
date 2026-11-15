using IMRCRaceControl.Core;
using IMRCRaceControl.Models;
using System.Text.Json;

public static class PersonalBestChecks
{
    public static int Run()
    {
        var a = new RaceDayEventDefinition
        {
            Id = "event1", Name = "Imported results", Status = EventStatus.Completed,
            EventDate = new DateTime(2026, 11, 1),
            Sessions = [new EventSessionDefinition
            {
                Id = "race1", ClassId = "classA", ClassName = "Stock",
                ProgressStatus = EventSessionStatus.Completed,
                Entrants = [new SessionEntrant { DriverId = "driver1", DriverName = "Test Racer", CarNumber = 5 }],
                Results = [new StandingRow { CarNumber = 5, BestLap = "6.123", Laps = 30 }]
            }]
        };
        var b = new RaceDayEventDefinition
        {
            Id = "event2", Name = "Later race", Status = EventStatus.Completed,
            EventDate = new DateTime(2026, 11, 2),
            Sessions = [
                new EventSessionDefinition
                {
                    Id = "race2", ClassId = "classA", ClassName = "Stock",
                    ProgressStatus = EventSessionStatus.Completed,
                    Entrants = [new SessionEntrant { DriverId = "driver1", CarNumber = 6 }],
                    Results = [new StandingRow { CarNumber = 6, BestLap = "6.5", Laps = 30 }],
                    Laps = [new LapRecord(1,6,"Test Racer",1,TimeSpan.FromSeconds(5.95),
                        TimeSpan.FromSeconds(5.95),DateTimeOffset.Parse("2026-11-02T12:00:00Z"),"Race",false)]
                },
                new EventSessionDefinition
                {
                    Id = "race3", ClassId = "classB", ClassName = "Pro",
                    ProgressStatus = EventSessionStatus.Completed,
                    Entrants = [new SessionEntrant { DriverId = "driver1", CarNumber = 5 }],
                    Results = [new StandingRow { CarNumber = 5, BestLap = "4.2", Laps = 30 }]
                }
            ]
        };
        var list = PersonalBests.FromEvents([a, b]);
        if (list.Count != 2 || PersonalBests.Lookup(list, "driver1","classA")?.LapSeconds != 5.95 ||
            PersonalBests.Lookup(list, "driver1","classB")?.LapSeconds != 4.2)
            throw new Exception("Personal best grouping by stable driver/class failed.");
        if (!PersonalBests.Beats(PersonalBests.Lookup(list,"driver1","classA"),5.949) ||
            PersonalBests.Beats(PersonalBests.Lookup(list,"driver1","classA"),5.95))
            throw new Exception("Live personal best comparisons failed.");
        var snapshot = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(
            new AppSettings { DriverPersonalBests = list }))!;
        if (snapshot.DriverPersonalBests.Count != 2) throw new Exception("Personal best persistence failed.");
        return 4;
    }
}
