using System.Text.Json;
using IMRCRaceControl.Core;
using IMRCRaceControl.Models;
using IMRCRaceControl.Services;

static class RentalTransponderChecks
{
    public static async Task<int> Run(bool isolated)
    {
        var count = 0;
        void Check(bool ok, string message) { if (!ok) throw new Exception("Rental: " + message); count++; }
        void Reject(Action action, string message)
        {
            try { action(); } catch (Exception ex) when (ex is InvalidOperationException or InvalidDataException) { count++; return; }
            throw new Exception("Rental validation did not reject: " + message);
        }
        var defaults = new AppSettings();
        defaults.RentalTransponders.NewNicknameStyle = "ColorNumber";
        defaults.RentalTransponders.NewLabelColor = "Red";
        var d1 = RentalTransponderRegistry.Add(defaults, "91");
        var d2 = RentalTransponderRegistry.Add(defaults, "92");
        Check(d1.Nickname == "Red #1" && d2.Nickname == "Red #2", "saved defaults apply to every addition");
        defaults.RentalTransponders.NewLabelColor = "Random";
        var d3 = RentalTransponderRegistry.Add(defaults, "93");
        Check(d3.LabelNumber == 3 && RentalTransponderRegistry.Colors.Contains(d3.Color), "random color keeps globally sequential numbering");
        Reject(() => RentalTransponderRegistry.SetLabel(defaults, d3.Id, "Black", 1), "number duplicate across colors");
        defaults.RentalTransponders.Inventory.Remove(d2);
        defaults.RentalTransponders.NewNicknameStyle = "Custom";
        defaults.RentalTransponders.CustomNicknamePattern = "Club rental";
        var d4 = RentalTransponderRegistry.Add(defaults, "94");
        Check(d4.LabelNumber == 2 && d4.Nickname == "Club rental #2", "lowest unused number and reusable custom prefix");
        var report = new RaceReport();
        defaults.RentalTransponders.PrintInColor = true;
        defaults.RentalTransponders.PrintFontSize = 15;
        var formatter = new RentalPrintFormatting(defaults.RentalTransponders, report);
        Check(formatter.Format("00091;777") == "Red #1; 777", "rental marked on printouts while personal number remains intact");
        Check(report.TextStyles.Count == 0, "rental print font inherits same size and styling as other transponders");
        defaults.RentalTransponders.PrintFormat = "NicknameAndNumber";
        Check(formatter.Format("91") == "Red #1", "nickname and physical number print format");
        defaults.RentalTransponders.NewNicknameStyle = "ColorNumber";
        defaults.RentalTransponders.NewLabelColor = "Green";
        RentalTransponderRegistry.ResetNicknames(defaults, d1.Id);
        Check(defaults.RentalTransponders.Inventory.Single(x => x.Id == d1.Id).Nickname == "Green #1" && defaults.RentalTransponders.Inventory.Single(x => x.Id == d3.Id).Nickname == d3.Nickname, "selected reset respects saved defaults and leaves others alone");
        RentalTransponderRegistry.ResetNicknames(defaults);
        Check(defaults.RentalTransponders.Inventory.All(x => x.Nickname == $"Green #{x.LabelNumber}"), "reset all matches saved nickname defaults");
        Check(ClassPrintColors.Choices.All(c => new ClassDefinition { PrintColor = c.Hex }.PrintColor == c.Hex) && new ClassDefinition { PrintColor = "#000000" }.PrintColor == ClassPrintColors.Choices[0].Hex, "class print colors accept safe palette and reject dark colors");
        var settings = new AppSettings();
        var a = RentalTransponderRegistry.Add(settings, " 000123 ");
        Check(a.Number == "123" && a.Nickname == "123***", "canonical number and default nickname");
        Reject(() => RentalTransponderRegistry.Add(settings, "00123"), "leading-zero duplicate ID");
        Reject(() => RentalTransponderRegistry.Add(settings, "124", "  123*** "), "case/whitespace duplicate nickname");
        foreach (var invalid in new[] { "", "0", "0000", "12a3", "12 34", "-8", "1;2" }) Reject(() => RentalTransponderRegistry.Add(settings, invalid), "invalid ID " + invalid);
        var b = RentalTransponderRegistry.Add(settings, "124");
        RentalTransponderRegistry.SetLabel(settings, a.Id, "Red", 1);
        RentalTransponderRegistry.SetLabel(settings, b.Id, "Black", 5);
        Check(settings.RentalTransponders.Inventory[0].Nickname == "Red #1" && settings.RentalTransponders.Inventory[1].Nickname == "Black #5", "preset labels");
        Reject(() => RentalTransponderRegistry.SetLabel(settings, b.Id, "Red", 1, "Custom alias"), "same color/number with different nickname");
        Reject(() => RentalTransponderRegistry.SetLabel(settings, b.Id, "Black", 6, " red   #1 "), "custom duplicate nickname");
        Reject(() => RentalTransponderRegistry.SetLabel(settings, b.Id, "Blue", -1), "invalid label number");
        Check(settings.RentalTransponders.Inventory[1].Nickname == "Black #5", "failed edits leave existing record intact");
        RentalTransponderRegistry.SetLabel(settings, b.Id, "Black", 5, "Spare rocket");
        Check(settings.RentalTransponders.Inventory[1].Nickname == "Spare rocket", "custom nickname");
        for (var i = 1000; i < 2105; i++) RentalTransponderRegistry.Add(settings, i.ToString());
        Check(settings.RentalTransponders.Inventory.Count == 1107, "inventory exceeds live decoder buffer with no count limit");
        foreach (var rental in settings.RentalTransponders.Inventory.Take(125).ToList())
        {
            var label = RentalTransponderRegistry.RandomLabel(settings, rental.Id);
            RentalTransponderRegistry.SetLabel(settings, rental.Id, label.Color, label.Number);
            RentalTransponderRegistry.ValidateState(settings.RentalTransponders);
        }
        Check(settings.RentalTransponders.Inventory.Select(x => x.Nickname.ToUpperInvariant()).Distinct().Count() == settings.RentalTransponders.Inventory.Count, "random labels remain unique");
        var corrupt = JsonSerializer.Deserialize<RentalTransponderState>(JsonSerializer.Serialize(settings.RentalTransponders))!;
        corrupt.Inventory[1].Number = corrupt.Inventory[0].Number;
        Reject(() => RentalTransponderRegistry.ValidateState(corrupt), "duplicate data cannot be saved");
        var driver = new DriverProfile { FirstName = "Racer", LastName = "One" };
        var other = new DriverProfile { FirstName = "Racer", LastName = "Two" };
        var cls = new ClassDefinition { Name = "21.5" }; var cls2 = new ClassDefinition { Name = "17.5" };
        settings.Drivers = [driver, other]; settings.Classes = [cls, cls2];
        RaceDayEventDefinition Event(int week, bool completed = false) => new()
        {
            Name = "Week " + week, EventDate = new DateTime(2026,1,1).AddDays(week * 7), Status = completed ? EventStatus.Completed : EventStatus.Active,
            ClassIds = [cls.Id, cls2.Id], DriverAssignments = [new() { DriverId = driver.Id, ClassId = cls.Id, TransponderId = "PERSONAL" }, new() { DriverId = driver.Id, ClassId = cls2.Id }, new() { DriverId = other.Id, ClassId = cls.Id }]
        };
        var ev = Event(1); settings.Events.Add(ev);
        var future = new EventSessionDefinition { ClassId = cls.Id, ProgressStatus = EventSessionStatus.Planned, Entrants = [new() { DriverId = driver.Id, TransponderId = "PERSONAL" }] };
        var finished = new EventSessionDefinition { ClassId = cls.Id, ProgressStatus = EventSessionStatus.Completed, Entrants = [new() { DriverId = driver.Id, TransponderId = "HISTORIC" }] };
        ev.Sessions = [future, finished];
        RentalTransponderRegistry.Assign(settings, a.Id, ev.Id, driver.Id, cls.Id);
        Check(future.Entrants[0].TransponderId == "PERSONAL;123", "assignment updates future lineup and keeps personal ID");
        Check(finished.Entrants[0].TransponderId == "HISTORIC", "assignment leaves completed lineup intact");
        RentalTransponderRegistry.Assign(settings, a.Id, ev.Id, driver.Id, cls2.Id);
        RentalTransponderRegistry.Assign(settings, a.Id, ev.Id, driver.Id, cls.Id);
        Check(settings.RentalTransponders.History.Count(x => x.RentalId == a.Id) == 2, "same racer multiple classes; repeated assignment is idempotent");
        Reject(() => RentalTransponderRegistry.Assign(settings, a.Id, ev.Id, other.Id, cls.Id), "rental cannot be lent to another racer until returned");
        RentalTransponderRegistry.Return(settings, a.Id);
        Check(future.Entrants[0].TransponderId == "PERSONAL" && finished.Entrants[0].TransponderId == "HISTORIC", "return removes only rental from future lineup");
        Check(settings.RentalTransponders.History.All(x => x.ReturnedAt is not null), "return closes active loans and retains history");
        RentalTransponderRegistry.Assign(settings, a.Id, ev.Id, other.Id, cls.Id);
        Check(settings.RentalTransponders.History.Last().DriverName == "Racer Two", "return and reassignment retain both racers");
        RentalTransponderRegistry.Return(settings, a.Id); ev.Status = EventStatus.Completed;
        Reject(() => RentalTransponderRegistry.Assign(settings, a.Id, ev.Id, driver.Id, cls.Id), "completed event protected");
        for (var week = 2; week <= 3; week++)
        {
            var e = Event(week, true); e.DriverAssignments[0].TransponderId = "000123"; settings.Events.Add(e);
        }
        settings.Events.Add(Event(4)); settings.Events.Add(Event(5));
        RentalTransponderRegistry.CaptureAssignments(settings);
        RentalTransponderRegistry.CaptureAssignments(settings);
        var last = RentalTransponderRegistry.History(settings, a.Id, 3);
        Check(last.Select(x => x.EventName).Distinct().SequenceEqual(new[] { "Week 3", "Week 2", "Week 1" }), "weeks 4 and 5 without use do not displace weeks 1, 2, 3");
        Check(last.Count(x => x.EventName == "Week 1") == 3, "all racers and classes retained for each selected event");
        driver.FirstName = "Renamed"; cls.Name = "Renamed class";
        settings.Events.Remove(ev);
        Check(RentalTransponderRegistry.History(settings, a.Id, 3).Any(x => x.EventName == "Week 1" && x.DriverName == "Racer One" && x.ClassName == "21.5"), "snapshots survive renamed racer/class and deleted event");
        var direct = Event(6); direct.DriverAssignments[0].TransponderId = "124"; settings.Events.Add(direct);
        RentalTransponderRegistry.CaptureAssignments(settings);
        Check(RentalTransponderRegistry.History(settings, b.Id).Any(x => x.EventId == direct.Id), "assignments made outside rental page are captured");
        var savedUses = settings.RentalTransponders.History.Count;
        var oldId = b.Id;
        RentalTransponderRegistry.Remove(settings, oldId);
        Check(!settings.RentalTransponders.Inventory.Any(x => x.Id == oldId), "remove rental from inventory");
        Check(settings.RentalTransponders.History.Count == savedUses && direct.DriverAssignments[0].TransponderId == "124", "removal preserves history and event roster");
        var readded = RentalTransponderRegistry.Add(settings, "124");
        Check(readded.Id == oldId && RentalTransponderRegistry.History(settings, oldId).Any(x => x.EventId == direct.Id), "readding rental reconnects preserved history");
        var legacy = new RentalTransponderState { Inventory = [new() { Number = "999", Nickname = "Transponder 999***" }] };
        RentalTransponderRegistry.ValidateState(legacy);
        Check(legacy.Inventory[0].Nickname == "999***", "existing default nicknames shortened");
        var restored = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings))!;
        Check(restored.RentalTransponders.Inventory.Count == 1107 && RentalTransponderRegistry.History(restored, a.Id, 3).Select(x => x.EventId).Distinct().Count() == 3, "backup serialization retains inventory and usage-based history");
        if (isolated)
        {
            if (!Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData).StartsWith("/tmp/imrc-callout-test-data")) throw new Exception("Isolated directory required");
            var db = new RaceControlDatabaseService(); await db.SaveApplicationStateAsync(settings);
            var reopened = (await new RaceControlDatabaseService().LoadApplicationStateAsync())!;
            Check(reopened.RentalTransponders.Inventory.Count == 1107 && reopened.RentalTransponders.History.Count == settings.RentalTransponders.History.Count, "SQLite restart preserves all rentals and history");
            var previousCount = reopened.RentalTransponders.Inventory.Count;
            reopened.RentalTransponders.Inventory[1].Number = reopened.RentalTransponders.Inventory[0].Number;
            try { await db.SaveApplicationStateAsync(reopened); throw new Exception("Duplicate database write was accepted"); }
            catch (InvalidDataException) { count++; }
            Check((await db.LoadApplicationStateAsync())!.RentalTransponders.Inventory.Count == previousCount, "invalid write preserves last saved state");
        }
        return count;
    }
}
