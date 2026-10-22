using System.IO;
using System.Media;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using IMRCRaceControl.Models;

namespace IMRCRaceControl.Services;

public sealed class ToneService
{
    public static readonly IReadOnlyList<string> SupportedExtensions = new[] { ".wav", ".mp3", ".wma" };

    private readonly List<MediaPlayer> _activePlayers = [];

    public void Play(TonePattern pattern, ToneLength length, string? customFile = null)
    {
        if (pattern == TonePattern.Silent) return;
        if (IsSupportedFile(customFile) && File.Exists(customFile))
        {
            PlayCustomFile(customFile!, length);
            return;
        }

        _ = Task.Run(() =>
        {
            try
            {
                using var stream = BuildWave(pattern, length);
                using var player = new SoundPlayer(stream);
                player.PlaySync();
            }
            catch
            {
                // Audio must never interfere with scoring.
            }
        });
    }

    public static bool IsSupportedFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        var extension = Path.GetExtension(path);
        return SupportedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase);
    }

    public static string ImportCustomTone(string sourcePath, string cueName)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
            return string.Empty;
        if (!File.Exists(sourcePath))
            throw new FileNotFoundException("The selected audio file no longer exists.", sourcePath);
        if (!IsSupportedFile(sourcePath))
            throw new InvalidOperationException("Supported custom audio formats are WAV, MP3, and WMA.");

        var folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "IMRCRaceControl",
            "Audio");
        Directory.CreateDirectory(folder);

        var safeCue = string.Concat(cueName.Where(ch => char.IsLetterOrDigit(ch) || ch is '_' or '-'));
        if (string.IsNullOrWhiteSpace(safeCue))
            safeCue = "cue";

        var destination = Path.Combine(folder, safeCue + Path.GetExtension(sourcePath).ToLowerInvariant());
        if (!string.Equals(Path.GetFullPath(sourcePath), Path.GetFullPath(destination), StringComparison.OrdinalIgnoreCase))
            File.Copy(sourcePath, destination, overwrite: true);

        return destination;
    }

    private void PlayCustomFile(string path, ToneLength length)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null)
            return;

        dispatcher.BeginInvoke(new Action(() =>
        {
            MediaPlayer? player = null;
            DispatcherTimer? stopTimer = null;

            void Cleanup()
            {
                try { stopTimer?.Stop(); } catch { }
                try { player?.Stop(); } catch { }
                try { player?.Close(); } catch { }
                if (player is not null)
                    _activePlayers.Remove(player);
            }

            try
            {
                player = new MediaPlayer();
                _activePlayers.Add(player);

                player.MediaOpened += (_, _) =>
                {
                    try
                    {
                        player.Position = TimeSpan.Zero;
                        player.Volume = 0.85;
                        player.Play();

                        // Race sounds should always be cues, not long-form audio.
                        var maxDuration = length == ToneLength.Short
                            ? TimeSpan.FromMilliseconds(450)
                            : TimeSpan.FromSeconds(length == ToneLength.Long ? 5 : 3);

                        stopTimer = new DispatcherTimer(DispatcherPriority.Background)
                        {
                            Interval = maxDuration
                        };
                        stopTimer.Tick += (_, _) => Cleanup();
                        stopTimer.Start();
                    }
                    catch
                    {
                        Cleanup();
                    }
                };
                player.MediaEnded += (_, _) => Cleanup();
                player.MediaFailed += (_, _) => Cleanup();
                player.Open(new Uri(path, UriKind.Absolute));
            }
            catch
            {
                Cleanup();
            }
        }));
    }

    private static MemoryStream BuildWave(TonePattern pattern, ToneLength length)
    {
        var scale = length == ToneLength.Short ? 0.48 : length == ToneLength.Long ? 1.8 : 1.0;
        var notes = pattern switch
        {
            TonePattern.AirHorn => new[] { (145, 1200, 0) },
            TonePattern.LongBeep => new[] { (550, 1200, 0) },
            TonePattern.DriverFinish => new[] { (1600, 150, 40), (800, 240, 0) },
            TonePattern.Halfway => new[] { (500, 180, 100), (500, 180, 0) },
            TonePattern.StartChime => new[] { (980, 90, 35), (1320, 150, 0) },
            TonePattern.WarningTriple => new[] { (430, 100, 45), (430, 100, 45), (430, 150, 0) },
            TonePattern.FinishDrop => new[] { (1100, 120, 15), (820, 140, 15), (560, 210, 0) },
            TonePattern.PassingTick => new[] { (1500, 70, 0) },
            TonePattern.HighPing => new[] { (1850, 125, 0) },
            TonePattern.DoublePulse => new[] { (720, 105, 40), (900, 125, 0) },
            _ => new[] { (1000, 100, 0) }
        };

        const int sampleRate = 44100;
        const short channels = 1;
        const short bits = 16;
        const double amplitude = 0.22;

        var samples = new List<short>();
        foreach (var (frequency, durationMs, pauseMs) in notes)
        {
            var duration = Math.Max(25, (int)Math.Round(durationMs * scale));
            var pause = Math.Max(0, (int)Math.Round(pauseMs * scale));
            var toneSamples = sampleRate * duration / 1000;

            for (var i = 0; i < toneSamples; i++)
            {
                var fadeSamples = Math.Min(toneSamples / 4, sampleRate / 200);
                var envelope = 1.0;
                if (fadeSamples > 0)
                {
                    if (i < fadeSamples)
                        envelope = i / (double)fadeSamples;
                    else if (toneSamples - i < fadeSamples)
                        envelope = (toneSamples - i) / (double)fadeSamples;
                }

                var phase = 2.0 * Math.PI * frequency * i / sampleRate;
                // Two low horn notes and restrained harmonics, normalized to avoid clipping.
                var value = pattern == TonePattern.AirHorn
                    ? (Math.Sin(phase) + 0.75 * Math.Sin(phase * 4.0 / 3.0) + 0.35 * Math.Sin(phase * 2) + 0.2 * Math.Sin(phase * 3)) / 2.3
                    : Math.Sin(phase);
                samples.Add((short)(short.MaxValue * amplitude * envelope * value));
            }

            var pauseSamples = sampleRate * pause / 1000;
            for (var i = 0; i < pauseSamples; i++)
                samples.Add(0);
        }

        var dataSize = samples.Count * sizeof(short);
        var stream = new MemoryStream(44 + dataSize);
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.ASCII, leaveOpen: true))
        {
            writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
            writer.Write(36 + dataSize);
            writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVE"));
            writer.Write(System.Text.Encoding.ASCII.GetBytes("fmt "));
            writer.Write(16);
            writer.Write((short)1);
            writer.Write(channels);
            writer.Write(sampleRate);
            writer.Write(sampleRate * channels * bits / 8);
            writer.Write((short)(channels * bits / 8));
            writer.Write(bits);
            writer.Write(System.Text.Encoding.ASCII.GetBytes("data"));
            writer.Write(dataSize);
            foreach (var sample in samples)
                writer.Write(sample);
        }

        stream.Position = 0;
        return stream;
    }
}
