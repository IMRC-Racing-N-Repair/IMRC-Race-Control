using System.Text.Json;
using System.Threading.Channels;
using IMRCRaceControl.Models;
using IMRCRaceControl.Core;

namespace IMRCRaceControl.Services;

/// <summary>
/// Single ordered CPU/background worker. All SQLite and settings backup writes
/// run outside the WPF dispatcher; no live mutable AppSettings references cross
/// the thread boundary. Each job carries a fixed JSON snapshot.
/// </summary>
public sealed class BackgroundRaceStateWriter : IAsyncDisposable
{
    private sealed record Job(string SnapshotJson, TaskCompletionSource<bool> Completion);
    private readonly Channel<Job> _jobs = Channel.CreateUnbounded<Job>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = false,
            AllowSynchronousContinuations = false });
    private readonly RaceControlDatabaseService _database;
    private readonly SettingsService _settings;
    private readonly Task _worker;
    private bool _stopping;

    public BackgroundRaceStateWriter(RaceControlDatabaseService database, SettingsService settings)
    {
        _database = database;
        _settings = settings;
        _worker = Task.Run(ProcessAsync);
    }

    public Task WriteAsync(string frozenSnapshotJson)
    {
        if (_stopping) throw new ObjectDisposedException(nameof(BackgroundRaceStateWriter));
        var signal = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_jobs.Writer.TryWrite(new(frozenSnapshotJson, signal)))
            throw new InvalidOperationException("The race database worker stopped accepting changes.");
        return signal.Task;
    }

    private async Task ProcessAsync()
    {
        await foreach (var job in _jobs.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            try
            {
                var state = JsonSerializer.Deserialize<AppSettings>(job.SnapshotJson)
                    ?? throw new InvalidDataException("Race state snapshot could not be decoded.");
                // Historical PB calculations no longer occupy the UI thread.
                state.DriverPersonalBests = PersonalBests.FromEvents(state.Events, state.Classes);
                await _database.SaveApplicationStateAsync(state).ConfigureAwait(false);
                try { await _settings.SaveAsync(state).ConfigureAwait(false); }
                catch (Exception backupError)
                {
                    AppCrashLogger.Log("Settings JSON recovery backup", backupError);
                    // SQLite is authoritative and has already committed successfully.
                }
                job.Completion.TrySetResult(true);
            }
            catch (Exception ex)
            {
                job.Completion.TrySetException(ex);
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        _stopping = true;
        _jobs.Writer.TryComplete();
        await _worker.ConfigureAwait(false);
    }
}
