using System.Text;
using System.Text.Json;
using IMRCRaceControl.Core;
using IMRCRaceControl.Models;
using IMRCRaceControl.Services;
using IMRCRaceControl.Transport;
using NetMQ;
using NetMQ.Sockets;

static class V141Checks
{
    public static async Task<int> Run()
    {
        var count = 0;
        void Check(bool value, string label) { if (!value) throw new Exception(label); count++; }
        void Near(double expected, double value, string label) => Check(Math.Abs(expected - value) < .000001, label);
        var now = DateTimeOffset.UtcNow;
        // Independent I-Lap example from the published SerialLapCounters protocol notes.
        var bytes = Convert.FromHexString("0140093230340933343536373839093332342E3632300D0A");
        var ilap = new AutomaticDecoder();
        var hit = ilap.Feed(bytes, now).Passings.Single();
        Check(hit.TransponderId == "3456789" && hit.DecoderId == 204 && hit.ClockMicroseconds == 324620000 && hit.Protocol == "I-Lap", "I-Lap independent passing vector");
        for (var i = 1; i < bytes.Length - 1; i++)
        {
            var split = new AutomaticDecoder();
            Check(split.Feed(bytes.AsSpan(0, i), now).Passings.Count == 0, "I-Lap partial frame");
            Check(split.Feed(bytes.AsSpan(i), now).Passings.Count == 1, "I-Lap fragmented completion");
        }
        Check(ilap.Feed(bytes, now).Passings.Count == 0, "I-Lap duplicate suppressed");
        ilap.ResetConnection();
        Check(ilap.Feed(bytes, now).Passings.Count == 0, "I-Lap reconnect duplicate suppressed");
        var init = Convert.FromHexString("012509323034093009300D0A");
        Check(ilap.Feed(init, now).Passings.Count == 0, "I-Lap startup is not a passing");
        Check(ilap.Feed(bytes, now).Passings.Count == 1, "I-Lap initialization resets dedup epoch");
        Check(new AutomaticDecoder().Feed(Encoding.ASCII.GetBytes("\x01@\t204\t0001234\t4.448\r\n"), now).Passings.Single().TransponderId == "0001234", "I-Lap retains fixed-width identity");
        foreach (var invalid in new[] { "\x01@\t204\t3456789\t-1\r\n", "\x01@\t204\t123\t1.000\r\n", "@\t204\t3456789\t4.000\r\n", "\x01%\t204\t0\t0\r\n" })
            Check(new AutomaticDecoder().Feed(Encoding.ASCII.GetBytes(invalid), now).Passings.Count == 0, "I-Lap invalid/status is not lap");

        var open = new OpenStintDecoder();
        DecoderReadResult Read(string line, DateTimeOffset? at = null) => open.FeedMessage(Encoding.ASCII.GetBytes(line), at ?? now);
        hit = Read("P 1618706341 OPN 1615544 3.50 64 89113 24.3").Passings.Single();
        Check(hit.TransponderId == "1615544" && hit.Hits == 64 && hit.ClockMicroseconds == 1618706341000, "OpenStint independent vector and millisecond clock");
        var later = Read("P 1618714251 OPN 1615544 -3.08 40 94345 21.7 future", now.AddMilliseconds(7910)).Passings.Single();
        Near(7.910, (later.Timestamp - hit.Timestamp).TotalSeconds, "OpenStint timing precision and extensions");
        Check(Read("P 1618714251 OPN 1615544 -3.08 40 94345 21.7").Passings.Count == 0, "OpenStint duplicate suppressed");
        open.ResetConnection();
        Check(Read("P 1618714251 OPN 1615544 -3.08 40 94345 21.7").Passings.Count == 0, "OpenStint reconnect duplicate suppressed");
        Check(Read("P 1618720000 AMB 0 -3.0 40 0.25").Passings.Single().TransponderId == "0", "OpenStint accepts zero ID and older EVM field");
        Check(Read("P 20 OPN 12345 -3 40 10000").Passings.Single().Timestamp == now, "OpenStint restart clock reanchors");
        foreach (var line in new[] { "S 1792039754 -41.018744 5.08 0 0 3.01", "T 1618721816 OPN 1615544 905370", "L 1792041851 DONE 1001 12", "P 1 OPN 1 NaN 10 1", "P 1 OPN -1 1 10 1", "P 1 OPN 1 1 -10 1", "P 1 OPN 1 1 10", "P 1 OPN 1 1 10 1\nP 2 OPN 1 1 10 1" })
            Check(Read(line).Passings.Count == 0, "OpenStint diagnostics and malformed records never count laps");
        Check(new AutomaticDecoder().Feed(Encoding.ASCII.GetBytes("P 20 OPN 12345 -3 40 10000\n"), now).Passings.Count == 0, "OpenStint text is only decoded on its selected transport");
        var packet = new TaskCompletionSource<DecoderIngressService.FormattedPacket>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using (var ingress = new DecoderIngressService())
        {
            ingress.PacketFormatted += p => packet.TrySetResult(p);
            ingress.Enqueue("OpenStint localhost:5556", Encoding.ASCII.GetBytes("P 100 OPN 0 -2 20 90000"), now);
            var decoded = await packet.Task.WaitAsync(TimeSpan.FromSeconds(3));
            Check(decoded.Passings.Single().TransponderId == "0" && decoded.Protocol == "OpenStint", "Ingress routes whole ZeroMQ message without newline");
        }

        // Real TCP/ZeroMQ socket test: single-frame receipt, status, cancellation and reconnect.
        using var stop = new CancellationTokenSource();
        var portReady = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var publisher = Task.Factory.StartNew(() =>
        {
            using var socket = new PublisherSocket();
            socket.Options.Linger = TimeSpan.Zero;
            portReady.SetResult(socket.BindRandomPort("tcp://127.0.0.1"));
            while (!stop.IsCancellationRequested)
            {
                socket.SendFrame("P 100 OPN 1234567 -3 20 90000 20");
                stop.Token.WaitHandle.WaitOne(50);
            }
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        try
        {
            var port = await portReady.Task.WaitAsync(TimeSpan.FromSeconds(3));
            await using var transport = new OpenStintTransport("127.0.0.1", port);
            var received = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
            transport.BytesReceived += (_, e) => received.TrySetResult(e.Data);
            await transport.ConnectAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(8));
            var data = await received.Task.WaitAsync(TimeSpan.FromSeconds(3));
            Check(transport.IsConnected && new OpenStintDecoder().FeedMessage(data, now).Passings.Single().TransponderId == "1234567", "Real ZeroMQ connection reaches decoder");
            await transport.DisconnectAsync().WaitAsync(TimeSpan.FromSeconds(3));
            Check(!transport.IsConnected, "OpenStint disconnect ends worker promptly");
            await transport.ConnectAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(8));
            Check(transport.IsConnected, "OpenStint transport reconnects");
        }
        finally { stop.Cancel(); await publisher; }

        var driver = new DriverProfile { FirstName = "A" };
        var other = new DriverProfile { FirstName = "B" };
        var race = new EventSessionDefinition { SessionType = SessionType.Race, ProgressStatus = EventSessionStatus.Completed, ClassId = "class", StartedAt = now };
        race.Entrants = [new() { CarNumber = 1, DriverId = driver.Id }, new() { CarNumber = 2, DriverId = other.Id }];
        race.Results = [new() { CarNumber = 1, Position = 1, Laps = 21 }, new() { CarNumber = 2, Position = 2, Laps = 21 }];
        for (var car = 1; car <= 2; car++) for (var n = 1; n <= 21; n++)
            race.Laps.Add(new(race.Laps.Count + 1, car, "Test", n, TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(n * 4), now.AddSeconds(n * 4), "test", n == 21));
        var ev = new RaceDayEventDefinition { Sessions = [race] };
        var settings = new DriverRatingSettings();
        DriverRatingResult Rate() => DriverRatingCalculator.Calculate([ev], [driver, other], settings);
        double Safety() => Rate().Standings.Single(r => r.DriverId == driver.Id).Safety;
        var clean = Safety();
        race.Laps[5] = race.Laps[5] with { LapTime = TimeSpan.FromSeconds(4.4) };
        Check(Safety() < clean && Rate().Standings.Single(r => r.DriverId == driver.Id).EstimatedIncidents == 0, "Consistency affects SR even without accident threshold");
        race.Laps[5] = race.Laps[5] with { LapTime = TimeSpan.FromSeconds(8) };
        Near((300 + 5 * 19) / 120d - .25, Safety(), "Accident reduces consistency and receives separate penalty");
        var sr = Safety(); settings.AccidentPenalty = .5;
        Near(sr - .25, Safety(), "Accident penalty independently adjustable");
        var history = Rate().History.Single(r => r.DriverId == driver.Id);
        Near(3, history.SafetyBefore, "History shows SR before race"); Near(Safety(), history.SafetyAfter, "History shows SR after race");
        var stored = new AppSettings { OpenStintHost = "decoder.local", OpenStintPort = 5557, DriverRatingSettings = settings };
        var restored = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(stored))!;
        Check(restored.OpenStintHost == "decoder.local" && restored.OpenStintPort == 5557 && restored.DriverRatingSettings.AccidentPenalty == .5, "New settings roundtrip");
        return count;
    }
}
