namespace OpenCodeDesktop.Core.Workspaces;

public enum WorkspaceRecovery { Current, Recovering, RetryRequired, ReconnectRequired, ActionRequired }

/// <summary>Cancellation-aware backoff scheduling, replaceable in controlled tests.</summary>
public interface IRecoveryScheduler
{
    Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken);
}

internal sealed class RecoveryScheduler : IRecoveryScheduler
{
    public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken) => Task.Delay(delay, cancellationToken);
}

public sealed partial class LiveTaskWorkflow
{
    private readonly IRecoveryScheduler _scheduler;
    private Task? _recovery;
    private long _directoryRevision;
    private static readonly int[] s_recoveryDelays = [1, 2, 4, 8, 15];

    public WorkspaceRecovery Recovery { get; private set; }
    public string RecoveryStatus { get; private set; } = "Live updates connected.";
    public bool CanRetryRecovery => Recovery == WorkspaceRecovery.RetryRequired;

    internal void ReportReadFailure(WorkspaceException error) => RequestRecovery(error);

    public Task RetryRecoveryAsync()
    {
        if (_disposed || Recovery is WorkspaceRecovery.ReconnectRequired or WorkspaceRecovery.ActionRequired)
        {
            return Task.CompletedTask;
        }
        if (_recovery is { IsCompleted: false })
        {
            return _recovery;
        }
        MarkStale();
        Recovery = WorkspaceRecovery.Recovering;
        _recovery = RecoverAsync(immediate: true);
        return _recovery;
    }

    private void MarkStale()
    {
        foreach (SessionTaskState state in _sessions.Values)
        {
            state.Stale = true;
            state.Revision++;
        }
    }

    private void RequestRecovery(Exception error)
    {
        if (_disposed)
        {
            return;
        }
        MarkStale();
        if (error is WorkspaceException { Failure: WorkspaceFailure.Unauthorized })
        {
            Recovery = WorkspaceRecovery.ReconnectRequired;
            RecoveryStatus = "Authorization rejected. Reconnect in Settings > Connections with current credentials.";
        }
        else if (error is WorkspaceException { Failure: WorkspaceFailure.Protocol or WorkspaceFailure.Size } or System.Text.Json.JsonException)
        {
            Recovery = WorkspaceRecovery.ActionRequired;
            RecoveryStatus = "Live state unavailable. Check server compatibility and response limits, then reconnect in Settings > Connections.";
        }
        else if (Recovery == WorkspaceRecovery.Current)
        {
            Recovery = WorkspaceRecovery.Recovering;
            RecoveryStatus = "Live updates interrupted. Recovering; task state is the last observation.";
            _recovery = RecoverAsync(immediate: false);
        }
        Notify();
    }

    private async Task RecoverAsync(bool immediate)
    {
        // Yield before starting so even synchronous transports cannot reenter before ownership is recorded.
        await Task.Yield();
        try
        {
            for (int attempt = immediate ? -1 : 0; attempt < s_recoveryDelays.Length; attempt++)
            {
                if (_disposed || Recovery != WorkspaceRecovery.Recovering)
                {
                    return;
                }
                try
                {
                    if (attempt >= 0)
                    {
                        RecoveryStatus = $"Live updates interrupted. Recovery attempt {attempt + 1}/5 in {s_recoveryDelays[attempt]} seconds; task state is the last observation.";
                        Notify();
                        await _scheduler.DelayAsync(TimeSpan.FromSeconds(s_recoveryDelays[attempt]), _lifetime.Token).ConfigureAwait(true);
                    }
                    if (_disposed || Recovery != WorkspaceRecovery.Recovering)
                    {
                        return;
                    }
                    RecoveryStatus = "Recovering conversation, execution state and pending requests…";
                    Notify();
                    await ReconcileAsync().ConfigureAwait(true);
                    if (_disposed || Recovery != WorkspaceRecovery.Recovering)
                    {
                        return;
                    }
                    Recovery = WorkspaceRecovery.Current;
                    RecoveryStatus = "Live updates connected. Server state refreshed.";
                    AttentionError = null;
                    foreach (SessionTaskState state in _sessions.Values)
                    {
                        state.Stale = false;
                    }
                    Notify();
                    return;
                }
                catch (Exception error) when (!_disposed)
                {
                    RequestRecovery(error);
                    if (Recovery != WorkspaceRecovery.Recovering)
                    {
                        return;
                    }
                }
            }
            if (!_disposed)
            {
                Recovery = WorkspaceRecovery.RetryRequired;
                RecoveryStatus = "Recovery stopped after five attempts. Check the server and network, then choose Retry recovery. Drafts and unresolved delivery are retained.";
                Notify();
            }
        }
        catch (OperationCanceledException) when (_disposed) { }
        catch (ObjectDisposedException) when (_disposed) { }
    }

    private async Task ReconcileAsync()
    {
        await EnsureStreamAsync().ConfigureAwait(true);
        // The event channel is bounded and backpressured. Revisions replace a second unbounded event buffer.
        // Unknown/locationless events invalidate every session; directory revisions also cover attention discovery.
        for (int pass = 0; pass < 8; pass++)
        {
            long revision = _directoryRevision;
            foreach (KeyValuePair<string, string> directory in _directories.ToArray())
            {
                IReadOnlyList<string> pending = await _api.AttentionSessionsAsync(directory.Key, _lifetime.Token).ConfigureAwait(true);
                foreach (string id in pending)
                {
                    WorkspaceSession session = await _workspace.SessionAsync(id, _lifetime.Token).ConfigureAwait(true);
                    if (session.Id != id || session.Directory != directory.Key || session.ProjectId != directory.Value)
                    {
                        throw new WorkspaceException(WorkspaceFailure.Protocol, "Pending attention belongs to another directory.");
                    }
                    if (_disposed)
                    {
                        return;
                    }
                    Track(session);
                }
            }
            foreach (SessionTaskState state in _sessions.Values.ToArray())
            {
                if (state.Refresh is { } previous)
                {
                    try
                    {
                        await previous.ConfigureAwait(true);
                    }
                    catch (Exception) { /* Replaced by the authoritative read below. */ }
                }
                if (_disposed)
                {
                    return;
                }
                state.Refresh = HydrateAsync(state);
                await state.Refresh.ConfigureAwait(true);
            }
            if (_streamAvailable && Volatile.Read(ref _pendingEvents) == 0 && revision == _directoryRevision && _sessions.Values.All(s => s.Hydrated))
            {
                return;
            }
        }
        throw new WorkspaceException(WorkspaceFailure.Transport, "Live state kept changing during recovery.");
    }
}
