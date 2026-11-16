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
    private sealed record Job(string? SnapshotJson, string? EventId, string? ClassIdsJson, string? AssignmentsJson, string? EntrantPatchesJson, TaskCompletionSource<bool> Completion);
    private readonly Channel<Job> _jobs = Channel.CreateUnbounded<Job>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = false,
            AllowSynchronousContinuations = false });
    private readonly Func<AppSettings, Task> _saveDatabase;
    private readonly Func<AppSettings, Task> _saveRecovery;
    private readonly Func<string, string, Task>? _updateEventClasses;
    private readonly Func<string, string, string, string, Task>? _updateEventRegistration;
    private readonly Task _worker;
    private bool _stopping;

    public BackgroundRaceStateWriter(Func<AppSettings, Task> saveDatabase,
        Func<AppSettings, Task> saveRecovery,
        Func<string, string, Task>? updateEventClasses = null,
        Func<string, string, string, string, Task>? updateEventRegistration = null)
    {
        _saveDatabase = saveDatabase;
        _saveRecovery = saveRecovery;
        _updateEventClasses = updateEventClasses;
        _updateEventRegistration = updateEventRegistration;
        _worker = Task.Run(ProcessAsync);
    }

    public Task WriteAsync(string frozenSnapshotJson)
    {
        if (_stopping) throw new ObjectDisposedException(nameof(BackgroundRaceStateWriter));
        var signal = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_jobs.Writer.TryWrite(new(frozenSnapshotJson, null, null, null, null, signal)))
            throw new InvalidOperationException("The race database worker stopped accepting changes.");
        return signal.Task;
    }

    public Task UpdateEventClassesAsync(string eventId, string classIdsJson)
    {
        if (_stopping) throw new ObjectDisposedException(nameof(BackgroundRaceStateWriter));
        if (_updateEventClasses is null) throw new NotSupportedException("Incremental event updates unavailable.");
        var signal = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_jobs.Writer.TryWrite(new(null, eventId, classIdsJson, null, null, signal)))
            throw new InvalidOperationException("The race database worker stopped accepting changes.");
        return signal.Task;
    }

    public Task UpdateEventRegistrationAsync(string eventId, string classIdsJson,
        string assignmentsJson, string entrantPatchesJson)
    {
        if (_stopping) throw new ObjectDisposedException(nameof(BackgroundRaceStateWriter));
        if (_updateEventRegistration is null) throw new NotSupportedException("Incremental registrations unavailable.");
        var signal = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_jobs.Writer.TryWrite(new(null,eventId,classIdsJson,assignmentsJson,entrantPatchesJson,signal)))
            throw new InvalidOperationException("The race database worker stopped accepting changes.");
        return signal.Task;
    }

    private async Task ProcessAsync()
    {
        await foreach (var job in _jobs.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            try
            {
                if (job.AssignmentsJson is not null)
                {
                    await _updateEventRegistration!(job.EventId!,job.ClassIdsJson!,job.AssignmentsJson,job.EntrantPatchesJson!).ConfigureAwait(false);
                    job.Completion.TrySetResult(true);
                    continue;
                }
                if (job.EventId is not null)
                {
                    await _updateEventClasses!(job.EventId, job.ClassIdsJson!).ConfigureAwait(false);
                    job.Completion.TrySetResult(true);
                    continue;
                }
                var state = JsonSerializer.Deserialize<AppSettings>(job.SnapshotJson!)
                    ?? throw new InvalidDataException("Race state snapshot could not be decoded.");
                // Historical PB calculations no longer occupy the UI thread.
                state.DriverPersonalBests = PersonalBests.FromEvents(state.Events, state.Classes);
                await _saveDatabase(state).ConfigureAwait(false);
                try { await _saveRecovery(state).ConfigureAwait(false); }
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
