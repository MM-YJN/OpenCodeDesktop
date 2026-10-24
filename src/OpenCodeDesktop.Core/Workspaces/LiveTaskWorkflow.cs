using System.Threading.Channels;

namespace OpenCodeDesktop.Core.Workspaces;

public sealed class SessionTaskState(string id, string directory, string projectId)
{
    public string Id { get; } = id;
    public string Directory { get; } = directory;
    public string ProjectId { get; } = projectId;
    public TaskCatalog Catalog { get; internal set; } = new([], []);
    public TaskModel? Model { get; set; }
    public string? Agent { get; set; }
    public bool Loading { get; internal set; }
    public bool Busy { get; internal set; }
    public bool Stopping { get; internal set; }
    public bool Stale { get; internal set; } = true;
    public bool Active { get; internal set; }
    public bool Hydrated { get; internal set; }
    public bool DeliveryUncertain { get; internal set; }
    public TaskSubmission? Submission { get; internal set; }
    public string? Error { get; internal set; }
    public string Outcome { get; internal set; } = "Ready";
    public IReadOnlyList<string> Inbox { get; internal set; } = [];
    public IReadOnlyList<HistoryMessage> History { get; internal set; } = [];
    public IReadOnlyList<TaskPermission> Permissions { get; internal set; } = [];
    public IReadOnlyList<TaskForm> Forms { get; internal set; } = [];
    public Dictionary<string, Dictionary<string, object>> Answers { get; } = new(StringComparer.Ordinal);
    internal Dictionary<(string Message, int Ordinal), string> Text { get; } = [];
    internal Dictionary<(string Message, string Tool), string> Tools { get; } = [];
    internal long Revision { get; set; }
    internal double? Idle { get; set; }
    internal double? SubmissionIdle { get; set; }
    internal bool Accepted { get; set; }
    internal SessionTaskState Retain()
    {
        SessionTaskState copy = new(Id, Directory, ProjectId)
        {
            Catalog = Catalog,
            Model = Model,
            Agent = Agent,
            Stale = true,
            Active = Active,
            Hydrated = Hydrated,
            DeliveryUncertain = DeliveryUncertain || Busy && Submission is not null,
            Submission = Submission,
            Outcome = Outcome,
            Inbox = Inbox,
            History = Conversation,
            Permissions = Permissions,
            Forms = Forms,
            Idle = Idle,
            SubmissionIdle = SubmissionIdle,
            Accepted = Accepted,
        };
        foreach (KeyValuePair<string, Dictionary<string, object>> answer in Answers)
        {
            copy.Answers.Add(answer.Key, new(answer.Value, StringComparer.Ordinal));
        }

        return copy;
    }
    public bool CanSend => Hydrated && !Stale && !Loading && !Busy && !Active && Inbox.Count == 0 && Submission is null &&
        Permissions.Count == 0 && Forms.Count == 0 && Model is not null && Agent is not null &&
        Catalog.Models.Any(m => m.Id == Model.Id && m.ProviderId == Model.ProviderId) && Catalog.Agents.Any(a => a.Id == Agent);
    public string Status => Stopping ? "Stopping… confirming the result" : Busy ? "Sending decision or request…" :
        Stale ? "Live state unavailable — refresh explicitly" : Permissions.Count + Forms.Count > 0 ? (Active ? "Waiting for you · task active" : "Waiting for you · session inactive") :
        Active ? "Running" : Loading ? "Checking live state…" : Submission is not null ? (DeliveryUncertain ? "Delivery uncertain — do not resend; refresh" : "Accepted / queued") :
        Inbox.Count > 0 ? (Outcome == "Ready" ? "Accepted / queued" : Outcome + " · queued input remains") : Outcome;
    public IReadOnlyList<HistoryMessage> Conversation => History
        .Concat(Submission is { } submitted && !History.Any(m => m.Id == submitted.Id) ? [new HistoryMessage(submitted.Id, DeliveryUncertain ? "user · delivery uncertain" : "user · accepted / queued", submitted.Text)] : [])
        .Concat(Text.GroupBy(p => p.Key.Message)
        .Where(g => !History.Any(h => h.Id == g.Key)).Select(g => new HistoryMessage(g.Key, "assistant", string.Join("\n", g.OrderBy(p => p.Key.Ordinal).Select(p => p.Value)))))
        .Concat(Tools.Where(t => !History.Any(m => m.Id == t.Key.Message)).Select(t => new HistoryMessage(t.Key.Message + ":" + t.Key.Tool, "tool activity", t.Value))).ToArray();
}

/// <summary>Session-owned workflow, called on its owning synchronization context. Never retries mutations.</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2213:Disposable fields should be disposed", Justification = "The stream consumer disposes the cancellation source when all producers exit.")]
public sealed class LiveTaskWorkflow : IDisposable
{
    private readonly IWorkspaceConnection _workspace;
    private readonly ITaskConnection _api;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Dictionary<string, SessionTaskState> _sessions = new(StringComparer.Ordinal);
    private readonly HashSet<string> _eventIds = new(StringComparer.Ordinal);
    private readonly Queue<string> _eventOrder = new();
    private bool _disposed;
    private bool _streamAvailable;
    private Task? _watch;
    private readonly Dictionary<string, string> _directories = new(StringComparer.Ordinal);
    private bool _discovering;
    private TaskCompletionSource _connected = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public LiveTaskWorkflow(IWorkspaceConnection workspace, ITaskConnection api)
    {
        _workspace = workspace;
        _api = api;
    }
    public event EventHandler? Changed;
    public IReadOnlyList<SessionTaskState> Sessions => _sessions.Values.ToArray();
    public string? AttentionError { get; private set; }
    public void Restore(IEnumerable<SessionTaskState> sessions)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        foreach (SessionTaskState state in sessions)
        {
            _sessions.TryAdd(state.Id, state.Retain());
        }
    }
    public SessionTaskState? Find(string? id) => id is not null ? _sessions.GetValueOrDefault(id) : null;
    public async Task OpenDirectoryAsync(ProjectLocation location)
    {
        ArgumentNullException.ThrowIfNull(location);
        _directories[location.Directory] = location.ProjectId;
        try
        {
            await EnsureStreamAsync().ConfigureAwait(true);
            await DiscoverAsync().ConfigureAwait(true);
        }
        catch (Exception)
        {
            AttentionError = "Pending requests could not be loaded. Reopen the directory to refresh.";
            Notify();
        }
    }
    private async Task EnsureStreamAsync()
    {
        if (_watch is null || _watch.IsCompleted)
        {
            _connected = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _watch = ConsumeAsync();
        }
        await _connected.Task.WaitAsync(TimeSpan.FromSeconds(15), _lifetime.Token).ConfigureAwait(true);
    }
    private async Task DiscoverAsync()
    {
        if (_discovering || _disposed)
        {
            return;
        }

        _discovering = true;
        try
        {
            AttentionError = null;
            foreach (KeyValuePair<string, string> directory in _directories.ToArray())
            {
                IReadOnlyList<string> ids = await _api.AttentionSessionsAsync(directory.Key, _lifetime.Token).ConfigureAwait(true);
                foreach (string id in ids)
                {
                    if (_disposed)
                    {
                        return;
                    }

                    WorkspaceSession session = await _workspace.SessionAsync(id, _lifetime.Token).ConfigureAwait(true);
                    if (session.Directory != directory.Key || session.ProjectId != directory.Value || session.Id != id)
                    {
                        continue;
                    }

                    await RefreshAsync(Track(session), reconnect: false).ConfigureAwait(true);
                }
            }
        }
        catch (Exception)
        {
            AttentionError = "Pending requests could not be confirmed. Reopen the directory to refresh.";
            foreach (SessionTaskState state in _sessions.Values)
            {
                state.Stale = true;
            }
        }
        finally
        {
            _discovering = false;
            Notify();
        }
    }
    public SessionTaskState Track(WorkspaceSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (!_sessions.TryGetValue(session.Id, out SessionTaskState? state))
        {
            state = new(session.Id, session.Directory, session.ProjectId);
            _sessions.Add(session.Id, state);
        }
        return state;
    }

    public async Task OpenAsync(WorkspaceSession session)
    {
        SessionTaskState state = Track(session);
        await RefreshAsync(state).ConfigureAwait(true);
    }

    public async Task RefreshAsync(SessionTaskState state, bool reconnect = true)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (_disposed || state.Loading)
        {
            return;
        }
        if (!reconnect && !_streamAvailable)
        {
            state.Stale = true;
            Notify();
            return;
        }

        state.Loading = true;
        state.Error = null;
        Notify();
        try
        {
            await EnsureStreamAsync().ConfigureAwait(true);
            state.Catalog = await _api.CatalogAsync(state.Directory, _lifetime.Token).ConfigureAwait(true);
            // Events invalidate the read set. Re-read until a quiet snapshot is obtained; never claim an atomic snapshot.
            for (int attempt = 0; attempt < 8; attempt++)
            {
                long revision = state.Revision;
                TaskSnapshot snapshot = await _api.TaskSnapshotAsync(state.Id, _lifetime.Token).ConfigureAwait(true);
                if (snapshot.Session.Id != state.Id || snapshot.Session.Directory != state.Directory || snapshot.Session.ProjectId != state.ProjectId ||
                    snapshot.Permissions.Any(p => p.SessionId != state.Id) || snapshot.Forms.Any(f => f.SessionId != state.Id))
                {
                    throw new WorkspaceException(WorkspaceFailure.Protocol, "Task data belongs to another session. Refresh the owning directory.");
                }

                List<HistoryMessage> messages = await ReadDeliveryHistoryAsync(state, snapshot).ConfigureAwait(true);
                if (_disposed)
                {
                    return;
                }

                if (revision != state.Revision)
                {
                    continue;
                }

                ApplySnapshot(state, snapshot, messages);

                if (revision != state.Revision)
                {
                    continue;
                }

                state.Stale = !_streamAvailable;
                state.Hydrated = true;
                return;
            }
            state.Stale = true;
            state.Error = "The session is changing while checking. Refresh to confirm its latest state.";
        }
        catch (Exception)
        {
            state.Stale = true;
            state.Error = "Could not confirm live state. Check the connection and refresh. No prompt or decision was retried.";
        }
        finally
        {
            state.Loading = false;
            Notify();
        }
    }

    private async Task<List<HistoryMessage>> ReadDeliveryHistoryAsync(SessionTaskState state, TaskSnapshot snapshot)
    {
        List<HistoryMessage> messages = [];
        HashSet<string> cursors = new(StringComparer.Ordinal);
        string? cursor = null;
        do
        {
            WorkspacePage<HistoryMessage> page = await _workspace.HistoryAsync(state.Id, cursor, _lifetime.Token).ConfigureAwait(true);
            messages.AddRange(page.Data);
            cursor = page.Data.Count == 0 ? null : page.Next;
            if (cursor is not null && !cursors.Add(cursor))
            {
                throw new WorkspaceException(WorkspaceFailure.Protocol, "History pagination repeated. Refresh before sending.");
            }

            if (messages.Count > 100000)
            {
                throw new WorkspaceException(WorkspaceFailure.Size, "History is too large to reconcile safely. Delivery remains unresolved.");
            }

            if (state.Submission is null || messages.Any(m => m.Id == state.Submission.Id) || snapshot.Inbox.Contains(state.Submission.Id))
            {
                break;
            }
        } while (cursor is not null);
        return messages;
    }
    private static void ApplySnapshot(SessionTaskState state, TaskSnapshot snapshot, List<HistoryMessage> messages)
    {
        state.History = state.History.Where(h => !messages.Any(m => m.Id == h.Id)).Concat(messages.DistinctBy(m => m.Id).Reverse()).ToArray();
        state.Active = snapshot.Active;
        state.Idle = snapshot.Idle;
        state.Permissions = snapshot.Permissions;
        state.Forms = snapshot.Forms.Where(f => f.State == "pending").ToArray();
        foreach (TaskForm form in state.Forms)
        {
            if (!state.Answers.TryGetValue(form.Id, out Dictionary<string, object>? answers))
            {
                state.Answers[form.Id] = answers = new(StringComparer.Ordinal);
            }

            foreach (TaskField field in form.Fields)
            {
                if (field.Default is not null && field.Type != "external")
                {
                    answers.TryAdd(field.Key, field.Default);
                }
            }
        }
        state.Inbox = snapshot.Inbox;
        state.Model ??= state.Catalog.Models.FirstOrDefault(m => m.Id == snapshot.Model?.Id && m.ProviderId == snapshot.Model.ProviderId);
        state.Agent ??= state.Catalog.Agents.FirstOrDefault(a => a.Id == snapshot.Agent)?.Id;
        bool found = state.Submission is { } submitted && (messages.Any(m => m.Id == submitted.Id) || snapshot.Inbox.Contains(submitted.Id));
        if (found)
        {
            state.Accepted = true;
            state.DeliveryUncertain = false;
        }
        bool terminal = !snapshot.Active && snapshot.Idle.HasValue && messages.Any(m => m.Kind == "idle") &&
            (state.Submission is null || state.Accepted && snapshot.Idle != state.SubmissionIdle && found && !snapshot.Inbox.Contains(state.Submission.Id));
        state.Outcome = terminal ? snapshot.Outcome switch { "succeeded" => "Completed", "failed" => "Failed — review errors and model access", "interrupted" => "Interrupted", _ => "Ready" } : "Ready";
        if (terminal && snapshot.Outcome is "succeeded" or "failed" or "interrupted")
        {
            state.Submission = null;
        }

        foreach ((string Message, int Ordinal) key in state.Text.Keys.Where(k => messages.Any(m => m.Id == k.Message) && !snapshot.Active).ToArray())
        {
            state.Text.Remove(key);
        }
    }

    public async Task SendAsync(SessionTaskState state, string text)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (_disposed || !state.CanSend || string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        state.Busy = true;
        state.Error = null;
        state.Accepted = false;
        TaskModel model = state.Model!;
        string agent = state.Agent!;
        TaskSubmission submission = new("msg_" + Guid.NewGuid().ToString("N"), text);
        state.SubmissionIdle = state.Idle;
        bool sending = false;
        string? failure = null;
        Notify();
        try
        {
            TaskCatalog catalog = await _api.CatalogAsync(state.Directory, _lifetime.Token).ConfigureAwait(true);
            if (!catalog.Models.Any(m => m.Id == model.Id && m.ProviderId == model.ProviderId) || !catalog.Agents.Any(a => a.Id == agent))
            {
                throw new WorkspaceException(WorkspaceFailure.Protocol, "Selection unavailable.");
            }

            state.Catalog = catalog;
            await _api.SelectModelAsync(state.Id, model, _lifetime.Token).ConfigureAwait(true);
            await _api.SelectAgentAsync(state.Id, agent, _lifetime.Token).ConfigureAwait(true);
            TaskSnapshot selected = await _api.TaskSnapshotAsync(state.Id, _lifetime.Token).ConfigureAwait(true);
            if (selected.Session.Id != state.Id || selected.Session.Directory != state.Directory || selected.Active || selected.Inbox.Count > 0 ||
                selected.Model?.Id != model.Id || selected.Model.ProviderId != model.ProviderId || selected.Agent != agent || selected.Permissions.Count + selected.Forms.Count > 0)
            {
                throw new WorkspaceException(WorkspaceFailure.Protocol, "Selection or execution changed. Refresh before sending.");
            }

            state.Submission = submission;
            state.Accepted = false;
            state.DeliveryUncertain = true;
            sending = true;
            await _api.SubmitAsync(state.Id, submission, _lifetime.Token).ConfigureAwait(true);
            if (_disposed)
            {
                return;
            }

            state.Accepted = true;
            state.DeliveryUncertain = false;
        }
        catch (Exception)
        {
            state.Stale = true;
            failure = sending ? "Delivery is uncertain. The immutable input ID and original text are retained. Refresh; do not resend." : "Could not apply the selected model and agent. Your draft is retained. Refresh and check model access.";
            state.Error = failure;
        }
        finally
        {
            state.Busy = false;
            Notify();
        }
        if (!_disposed)
        {
            await RefreshAsync(state, reconnect: false).ConfigureAwait(true);
            if (failure is not null && (!sending || state.DeliveryUncertain))
            {
                state.Error = failure;
                Notify();
            }
        }
    }

    public Task StopAsync(SessionTaskState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return MutateAsync(state, async () =>
        {
            state.Stopping = true;
            Notify();
            try
            {
                await _api.InterruptAsync(state.Id, _lifetime.Token).ConfigureAwait(true);
            }
            finally
            {
                state.Stopping = false;
            }
        });
    }

    public Task PermissionAsync(SessionTaskState state, TaskPermission permission, string decision)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(permission);
        TaskPermission? current = state.Permissions.FirstOrDefault(p => p.Id == permission.Id && p.SessionId == permission.SessionId);
        if (current is null || current.Action != permission.Action || !current.Resources.SequenceEqual(permission.Resources) || !current.Save.SequenceEqual(permission.Save) ||
            decision is not ("once" or "always" or "reject") || decision == "always" && permission.Save.Count == 0)
        {
            return Task.CompletedTask;
        }

        return MutateAsync(state, () => _api.ReplyPermissionAsync(state.Id, permission.Id, decision, _lifetime.Token));
    }
    public Task AnswerAsync(SessionTaskState state, TaskForm form, bool cancel)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(form);
        if (!state.Forms.Any(f => f.Id == form.Id && f.SessionId == form.SessionId))
        {
            return Task.CompletedTask;
        }

        Dictionary<string, object> answers = state.Answers[form.Id];
        // Freeze the payload; edits made while awaiting do not alter this decision.
        var payload = answers.Where(a => form.Fields.Any(f => f.Key == a.Key && TaskFormValidation.Visible(f, answers))).ToDictionary();
        return MutateAsync(state, async () =>
        {
            string? error = cancel ? null : await Task.Run(() => TaskFormValidation.Validate(form, payload)).ConfigureAwait(true);
            if (error is not null)
            {
                throw new WorkspaceException(WorkspaceFailure.Protocol, error);
            }

            TaskForm current = await _api.FormAsync(state.Id, form.Id, _lifetime.Token).ConfigureAwait(true);
            if (current.SessionId != state.Id || current.State != "pending")
            {
                throw new WorkspaceException(WorkspaceFailure.Protocol, "The request is no longer pending.");
            }

            if (cancel)
            {
                await _api.CancelFormAsync(state.Id, form.Id, _lifetime.Token).ConfigureAwait(true);
            }
            else
            {
                await _api.ReplyFormAsync(state.Id, form.Id, payload, _lifetime.Token).ConfigureAwait(true);
            }
        });
    }
    private async Task MutateAsync(SessionTaskState state, Func<Task> action)
    {
        if (_disposed || state.Busy || state.Stale)
        {
            return;
        }

        state.Busy = true;
        state.Revision++;
        state.Error = null;
        Notify();
        string? error = null;
        try
        {
            await action().ConfigureAwait(true);
        }
        catch (WorkspaceException failure)
        {
            error = failure.Message + " Answers are retained. Refresh to check whether the request is still available; no decision was retried.";
            state.Stale = true;
        }
        catch (Exception)
        {
            error = "The operation was not confirmed. Refresh to check whether the request is still available; answers are retained. For an invalid answer, review the declared constraints. No decision was retried.";
            state.Stale = true;
        }
        finally
        {
            state.Busy = false;
            state.Revision++;
            Notify();
        }
        if (!_disposed)
        {
            await RefreshAsync(state, reconnect: false).ConfigureAwait(true);
            if (error is not null)
            {
                state.Error = error;
                Notify();
            }
        }
    }

    private async Task ConsumeAsync()
    {
        using var streamLifetime = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        var channel = Channel.CreateBounded<TaskEvent>(new BoundedChannelOptions(256) { SingleReader = true, SingleWriter = true });
        Task ProduceAsync() => _api.WatchAsync(e => channel.Writer.WriteAsync(e, streamLifetime.Token).AsTask(), streamLifetime.Token);
        async Task ProducerAsync()
        {
            try
            {
                await ProduceAsync().ConfigureAwait(false);
                channel.Writer.TryComplete();
            }
            catch (Exception e)
            {
                channel.Writer.TryComplete(e);
            }
        }
        Task producer = ProducerAsync();
        try
        {
            await foreach (TaskEvent e in channel.Reader.ReadAllAsync(_lifetime.Token).ConfigureAwait(true))
            {
                if (_disposed)
                {
                    break;
                }

                if (e.Type == "server.connected")
                {
                    _streamAvailable = true;
                    _connected.TrySetResult();
                    continue;
                }
                if (e.Type.StartsWith("permission.", StringComparison.Ordinal) || e.Type.StartsWith("form.", StringComparison.Ordinal))
                {
                    _ = DiscoverAsync();
                }

                if (e.Id.Length > 0)
                {
                    if (!_eventIds.Add(e.Id))
                    {
                        continue;
                    }

                    _eventOrder.Enqueue(e.Id);
                    if (_eventOrder.Count > 4096)
                    {
                        _eventIds.Remove(_eventOrder.Dequeue());
                    }
                }
                IEnumerable<SessionTaskState> affected = e.SessionId is { } id && _sessions.TryGetValue(id, out SessionTaskState? owned) ? [owned] : _sessions.Values;
                foreach (SessionTaskState state in affected)
                {
                    state.Revision++;
                    ApplyEvent(state, e);
                    if (!state.Loading && !state.Busy)
                    {
                        _ = RefreshAfterEventAsync(state);
                    }
                }
                Notify();
            }
            if (!_disposed)
            {
                throw new WorkspaceException(WorkspaceFailure.Transport, "Live stream ended.");
            }
        }
        catch (Exception)
        {
            _streamAvailable = false;
            _connected.TrySetException(new WorkspaceException(WorkspaceFailure.Transport, "Live stream unavailable."));
            foreach (SessionTaskState state in _sessions.Values)
            {
                state.Stale = true;
                state.Revision++;
            }
            Notify();
        }
        finally
        {
            await streamLifetime.CancelAsync().ConfigureAwait(true);
            try
            {
                await producer.ConfigureAwait(true);
            }
            catch (Exception) { /* Reported through the channel. */ }
            if (_disposed)
            {
                _lifetime.Dispose();
            }
        }
    }
    private static void ApplyEvent(SessionTaskState state, TaskEvent e)
    {
        if (e.SessionId == state.Id && e.Type == "session.execution.started")
        {
            state.Active = true;
        }

        if (e.SessionId == state.Id && e.MessageId is { } message)
        {
            if (e.Type.StartsWith("session.tool.", StringComparison.Ordinal) && e.ToolId is { } tool)
            {
                state.Tools[(message, tool)] = (e.ToolName ?? tool) + " · " + e.Type["session.tool.".Length..];
            }

            (string message, int Ordinal) key = (message, e.Ordinal);
            if (e.Type == "session.text.started")
            {
                state.Text.TryAdd(key, "");
            }

            if (e.Type == "session.text.delta" && state.Text.TryGetValue(key, out string? text))
            {
                if (text.Length + e.Delta?.Length > 8388608)
                {
                    throw new WorkspaceException(WorkspaceFailure.Size, "Streaming text exceeded the size limit.");
                }

                state.Text[key] = text + e.Delta;
            }
        }
    }

    private async Task RefreshAfterEventAsync(SessionTaskState state)
    {
        // Coalesce bursts without blocking the UI or creating one REST hydration per delta.
        state.Loading = true;
        try
        {
            await Task.Delay(100, _lifetime.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        finally
        {
            state.Loading = false;
        }
        if (!_disposed)
        {
            await RefreshAsync(state, reconnect: false).ConfigureAwait(true);
        }
    }
    private void Notify()
    {
        if (!_disposed)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _lifetime.Cancel();
        if (_watch is null || _watch.IsCompleted)
        {
            _lifetime.Dispose();
        }
    }
}
