using OpenCodeDesktop.Core.Connections;

namespace OpenCodeDesktop.Core.Workspaces;

/// <summary>UI-independent workspace use case. State and events belong to the caller's synchronization context.</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2213:Disposable fields should be disposed", Justification = "Each RunAsync owns its CancellationTokenSource in a using scope until the pending operation exits; navigation only cancels it.")]
public sealed partial class LiveWorkspace : IDisposable
{
    private readonly ServerConnections _connections;
    private readonly IRecoveryScheduler? _recoveryScheduler;
    private readonly Dictionary<Guid, Backend> _backends = [];
    private CancellationTokenSource? _load;
    private long _generation;
    private long _connectionGeneration = -1;
    private Backend? _backend;
    private Project? _project;
    private bool _disposed;
    private Func<Task>? _retry;

    public LiveWorkspace(ServerConnections connections, IRecoveryScheduler? recoveryScheduler = null)
    {
        ArgumentNullException.ThrowIfNull(connections);
        _connections = connections;
        _recoveryScheduler = recoveryScheduler;
        connections.Changed += ConnectionChangedAsync;
        ConnectionChangedAsync(this, EventArgs.Empty);
    }

    public event EventHandler? Changed;
    public ProjectFilesWorkspace Files { get; } = new();
    public LiveTaskWorkflow? Tasks { get; private set; }
    public bool HasConnected { get; private set; }
    public bool IsConnected => _connections.Context is not null;
    public ActiveServer? Server => _connections.Active;
    public bool IsLoading { get; private set; }
    public string Status { get; private set; } = "Connect in Settings to open a server directory.";
    public string? Error { get; private set; }
    public bool CanRetry => _retry is not null && !IsLoading;
    public ProjectLocation? Location => IsConnected ? _project?.Location : null;
    public IReadOnlyList<WorkspaceProject> Projects => _backend?.Projects.Values.Where(p => p.Listed)
        .Select(p => new WorkspaceProject(
            p.Location,
            p == _project,
            p.OpenError,
            p.Sessions.ToArray(),
            p.SessionsLoaded,
            p.SessionsLoading,
            p.SessionsError,
            IsConnected && p.SessionCursor is not null && !p.SessionsLoading)).ToArray() ?? [];
    public IReadOnlyList<string> OpenedDirectories => Projects.Select(p => p.Location.Directory).ToArray();
    public IReadOnlyList<WorkspaceSession> Sessions => IsConnected ? _project?.Sessions.AsReadOnly() ?? (IReadOnlyList<WorkspaceSession>)[] : [];
    public WorkspaceSession? Selected => Sessions.FirstOrDefault(s => s.Id == _project?.SelectedId);
    public string? SelectedSessionId => _project?.SelectedId;
    public IReadOnlyList<HistoryMessage> History { get; private set; } = [];
    public bool HistoryLoaded { get; private set; }
    public bool CanLoadSessions => IsConnected && _project is { SessionCursor: not null, SessionsLoading: false };
    public bool CanLoadHistory => IsConnected && _project?.HistoryCursor is not null && HistoryLoaded && !IsLoading;
    public bool CanCreate => IsConnected && _project is { Creating: false, Uncertain: false } && !IsLoading;
    public bool CreationUncertain => _project?.Uncertain == true;
    public string Draft
    {
        get => _project?.Drafts.GetValueOrDefault(_project.SelectedId ?? "") ?? "";
        set
        {
            _project?.Drafts[_project.SelectedId ?? ""] = value;
        }
    }

    private void ConnectionChangedAsync(object? sender, EventArgs args)
    {
        if (!_disposed && _connectionGeneration != _connections.Generation)
        {
            ConnectionTransition = ApplyConnectionAsync();
        }
    }

    public Task ConnectionTransition { get; private set; } = Task.CompletedTask;

    private async Task ApplyConnectionAsync()
    {
        if (_disposed)
        {
            return;
        }

        foreach (Guid id in _backends.Keys.ToArray())
        {
            if (!_connections.Profiles.Any(p => p.Id == id && p.Address.Display == _backends[id].Profile.Address.Display))
            {
                _backends.Remove(id);
                Files.ForgetConnection(id);
            }
        }
        if (_connectionGeneration == _connections.Generation)
        {
            return;
        }

        _connectionGeneration = _connections.Generation;
        Files.SetConnection(IsConnected ? Server?.Profile.Id : null, _connections.Context as IProjectFilesConnection);
        if (_backend is { } previous && Tasks is { } previousTasks)
        {
            previous.TaskStates = previousTasks.Sessions.Select(s => s.Retain()).ToArray();
        }

        Tasks?.Dispose();
        Tasks = _connections.Context is ITaskConnection tasks ? new LiveTaskWorkflow(_connections.Context, tasks, _recoveryScheduler) : null;
        Tasks?.Changed += TaskChanged;

        CancelLoad();
        CancelProjectSessionLoads();
        _project = null;
        _backend = null;
        History = [];
        HistoryLoaded = false;
        Error = null;
        _retry = null;
        if (Server is { } server && IsConnected)
        {
            HasConnected = true;
            if (!_backends.TryGetValue(server.Profile.Id, out _backend))
            {
                _backend = new(server.Profile);
                _backends.Add(server.Profile.Id, _backend);
            }
            foreach (Project project in _backend.Projects.Values)
            {
                project.Sessions.Clear();
                project.SessionsLoaded = false;
                project.SessionsLoading = false;
                project.SessionsError = null;
                project.SessionCursor = null;
                project.SessionCursors.Clear();
            }
            Tasks?.Restore(_backend.TaskStates);
            foreach (Project project in _backend.Projects.Values)
            {
                Tasks?.RememberDirectory(project.Location);
            }
            Status = "Add a server-side directory or select a listed project.";
            string? remembered = _backend.Directory;
            Notify();
            if (remembered is not null)
            {
                await OpenProjectCoreAsync(remembered, null, false).ConfigureAwait(true);
            }
            else if (Tasks is { } attention && _backend.Projects.Values.FirstOrDefault() is { } retained)
            {
                // Removed entries still own server work and may acquire new requests while disconnected.
                await attention.OpenDirectoryAsync(retained.Location).ConfigureAwait(true);
            }
        }
        else
        {
            Status = "Disconnected. Reconnect in Settings. Drafts are retained for this app session.";
            Notify();
        }
    }

    public Task OpenProjectAsync(string directory)
    {
        UserNavigation?.Invoke(this, EventArgs.Empty);
        return OpenProjectCoreAsync(directory, null, false);
    }

    public Task OpenProjectSessionAsync(string directory, string sessionId)
    {
        UserNavigation?.Invoke(this, EventArgs.Empty);
        return OpenProjectCoreAsync(directory, sessionId, false);
    }

    public Task OpenProjectDraftAsync(string directory)
    {
        UserNavigation?.Invoke(this, EventArgs.Empty);
        return OpenProjectCoreAsync(directory, null, true);
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Maintainability", "CA1502:Avoid excessive complexity", Justification = "Directory, target-session and history validation remain one staged navigation transaction so a failed or superseded switch cannot partially replace the workspace.")]
    private Task OpenProjectCoreAsync(string directory, string? targetSessionId, bool selectDraft)
    {
        if (string.IsNullOrWhiteSpace(directory) || !IsConnected)
        {
            return Task.CompletedTask;
        }

        bool useRememberedSession = targetSessionId is null && !selectDraft;
        Func<Task> retry = selectDraft
            ? () => OpenProjectDraftAsync(directory)
            : targetSessionId is { } target ? () => OpenProjectSessionAsync(directory, target) : () => OpenProjectAsync(directory);
        return RunAsync("Opening server directory…", async (context, token, current) =>
        {
            Backend backend = _backend!;
            ProjectLocation location;
            WorkspacePage<WorkspaceSession> page;
            WorkspaceSession? selected = null;
            WorkspacePage<HistoryMessage>? history = null;
            bool missingSession = false;
            try
            {
                location = await context.ResolveAsync(directory, token).ConfigureAwait(true);
                await context.ValidateDirectoryAsync(location.Directory, token).ConfigureAwait(true);
                if (backend.Projects.GetValueOrDefault(directory) is { } saved &&
                    (saved.Location.ProjectId != location.ProjectId || saved.Location.Directory != location.Directory))
                {
                    throw Ownership();
                }

                if (backend.Projects.GetValueOrDefault(location.Directory) is { } loaded)
                {
                    CancelProjectLoad(loaded);
                }

                page = await context.SessionsAsync(location.Directory, null, token).ConfigureAwait(true);
                ValidateSessions(location, page.Data);
                string? selectedId = selectDraft
                    ? null
                    : targetSessionId ?? backend.Projects.GetValueOrDefault(location.Directory)?.ConfirmedId;
                if (selectedId is { } id)
                {
                    try
                    {
                        selected = await context.SessionAsync(id, token).ConfigureAwait(true);
                    }
                    catch (WorkspaceException error) when (error.Failure == WorkspaceFailure.NotFound && useRememberedSession)
                    {
                        missingSession = true;
                    }
                    if (selected is not null)
                    {
                        ValidateSessions(location, [selected]);
                        if (selected.Id != id)
                        {
                            throw Ownership();
                        }

                        history = await context.HistoryAsync(id, null, token).ConfigureAwait(true);
                    }
                }

                token.ThrowIfCancellationRequested();
            }
            catch
            {
                if (current() && backend.Projects.GetValueOrDefault(directory) is { Listed: true } failed)
                {
                    failed.OpenError = "Unavailable. Select to retry, or remove from list.";
                }

                throw;
            }

            if (!current())
            {
                return;
            }

            if (!backend.Projects.TryGetValue(location.Directory, out Project? project))
            {
                project = new(location);
                backend.Projects.Add(location.Directory, project);
            }
            project.Location = location;
            project.Listed = true;
            project.OpenError = null;
            SetSessions(project, page);
            if (selected is not null && !project.Sessions.Any(s => s.Id == selected.Id))
            {
                project.Sessions.Add(selected);
            }
            project.SelectedId = selected?.Id;
            project.ConfirmedId = selectDraft ? null : selected?.Id ?? project.ConfirmedId;
            _project = project;
            backend.Directory = location.Directory;
            History = history?.Data.DistinctBy(m => m.Id).Reverse().ToArray() ?? [];
            HistoryLoaded = history is not null;
            project.HistoryCursors.Clear();
            project.HistoryCursor = history is { Data.Count: > 0 } ? history.Next : null;
            if (missingSession)
            {
                Error = "The remembered session no longer exists. Select another session, or Retry to check it again.";
                _retry = retry;
            }
            NavigationChanged?.Invoke(this, EventArgs.Empty);
            Notify();
            if (Tasks is { } tasks)
            {
                await tasks.OpenDirectoryAsync(location).ConfigureAwait(true);
                if (current() && selected is not null)
                {
                    await tasks.OpenAsync(selected).ConfigureAwait(true);
                }
            }
        }, retry);
    }

    /// <summary>Forget navigation only; retain drafts and task attention for this app lifetime.</summary>
    public void RemoveProject(string directory)
    {
        if (_backend?.Projects.GetValueOrDefault(directory) is not { Listed: true } project)
        {
            return;
        }
        UserNavigation?.Invoke(this, EventArgs.Empty);
        CancelProjectLoad(project);
        CancelLoad();
        project.Listed = false;
        project.OpenError = null;
        if (_backend.Directory == directory)
        {
            _backend.Directory = null;
        }
        if (_project == project)
        {
            _project = null;
            _backend.Directory = null;
            History = [];
            HistoryLoaded = false;
        }
        Error = null;
        _retry = null;
        Status = _project is null ? "Add a server-side directory or select a listed project." : "Project removed from list. Server work is retained.";
        NavigationChanged?.Invoke(this, EventArgs.Empty);
        Notify();
    }

    public async Task SelectAttentionSessionAsync(string id)
    {
        if (_project is not { } project || Tasks?.Find(id) is not { } state || state.Directory != project.Location.Directory)
        {
            return;
        }

        if (!project.Sessions.Any(s => s.Id == id))
        {
            project.Sessions.Add(new(id, state.Directory, state.ProjectId, "Session requiring attention"));
        }

        await SelectSessionAsync(id).ConfigureAwait(true);
    }

    public Task SelectSessionAsync(string? id)
    {
        UserNavigation?.Invoke(this, EventArgs.Empty);
        if (_project is null || !IsConnected || (id is not null && !Sessions.Any(s => s.Id == id)))
        {
            return Task.CompletedTask;
        }

        CancelProjectLoad(_project);
        CancelLoad();
        _project.SelectedId = id;
        _project.HistoryCursor = null;
        _project.HistoryCursors.Clear();
        History = [];
        HistoryLoaded = false;
        Error = null;
        _retry = null;
        if (id is null)
        {
            Status = "New-session draft. Create a session to keep this conversation on the server.";
            _project.ConfirmedId = null;
            NavigationChanged?.Invoke(this, EventArgs.Empty);
            Notify();
            return Task.CompletedTask;
        }
        Project project = _project;
        return RunAsync("Loading conversation history…", (context, token, current) => ReadHistoryAsync(context, project, id, current, token), () => SelectSessionAsync(id));
    }

    private async Task ReadHistoryAsync(IWorkspaceConnection context, Project project, string id, Func<bool> current, CancellationToken token)
    {
        if (project.Sessions.FirstOrDefault(s => s.Id == id) is { } known)
        {
            Tasks?.Track(known);
        }
        WorkspaceSession session = await context.SessionAsync(id, token).ConfigureAwait(true);
        ValidateSessions(project.Location, [session]);
        if (session.Id != id)
        {
            throw Ownership();
        }
        if (!current())
        {
            return;
        }
        int index = project.Sessions.FindIndex(s => s.Id == id);
        if (index < 0)
        {
            project.Sessions.Add(session);
        }
        else
        {
            project.Sessions[index] = session;
        }

        WorkspacePage<HistoryMessage> page = await context.HistoryAsync(id, null, token).ConfigureAwait(true);
        if (!current())
        {
            return;
        }

        History = page.Data.DistinctBy(m => m.Id).Reverse().ToArray();
        project.HistoryCursors.Clear();
        project.HistoryCursor = page.Data.Count == 0 ? null : page.Next;
        HistoryLoaded = true;
        project.ConfirmedId = id;
        NavigationChanged?.Invoke(this, EventArgs.Empty);
        if (Tasks is { } tasks)
        {
            await tasks.OpenAsync(session).ConfigureAwait(true);
        }
    }

    public Task RefreshSessionsAsync() => _project is { } project ? RefreshProjectSessionsAsync(project.Location.Directory) : Task.CompletedTask;
    public Task LoadMoreSessionsAsync() => _project is { } project ? LoadProjectSessionsAsync(project.Location.Directory, true) : Task.CompletedTask;

    /// <summary>Load a listed project's first session page without changing the active workspace.</summary>
    public Task LoadProjectSessionsAsync(string directory, bool more = false)
    {
        if (!IsConnected || _backend?.Projects.GetValueOrDefault(directory) is not { Listed: true } project)
        {
            return Task.CompletedTask;
        }

        if (project.SessionsLoading || (more ? project.SessionCursor is null : project.SessionsLoaded))
        {
            return Task.CompletedTask;
        }

        return LoadProjectSessionsCoreAsync(project, more);
    }

    /// <summary>Reload a listed project's first session page, preserving its selection and drafts.</summary>
    public Task RefreshProjectSessionsAsync(string directory)
    {
        if (!IsConnected || _backend?.Projects.GetValueOrDefault(directory) is not { Listed: true } project)
        {
            return Task.CompletedTask;
        }

        CancelProjectLoad(project);
        return LoadProjectSessionsCoreAsync(project, false);
    }

    private async Task LoadProjectSessionsCoreAsync(Project project, bool more)
    {
        if (_connections.Context is not { } context)
        {
            return;
        }

        string? cursor = more ? project.SessionCursor : null;
        long connection = _connections.Generation;
        using CancellationTokenSource load = new(TimeSpan.FromSeconds(15));
        project.SessionLoad = load;
        project.SessionsLoading = true;
        project.SessionsError = null;
        Notify();
        bool OwnsLoad() => ReferenceEquals(project.SessionLoad, load);
        bool Current()
        {
            return !_disposed
                && connection == _connections.Generation
                && _backend is { } backend
                && backend.Projects.TryGetValue(project.Location.Directory, out Project? currentProject)
                && ReferenceEquals(currentProject, project)
                && project.Listed;
        }

        try
        {
            WorkspacePage<WorkspaceSession> page = await context.SessionsAsync(project.Location.Directory, cursor, load.Token).ConfigureAwait(true);
            ValidateSessions(project.Location, page.Data);
            ValidateCursor(cursor, page.Next, more ? project.SessionCursors : [], page.Data.Count, page.Data.Any(s => !project.Sessions.Any(old => old.Id == s.Id)));
            if (!Current() || !OwnsLoad())
            {
                return;
            }

            if (more)
            {
                if (cursor is not null)
                {
                    project.SessionCursors.Add(cursor);
                }

                project.Sessions.AddRange(page.Data.Where(s => !project.Sessions.Any(old => old.Id == s.Id)).DistinctBy(s => s.Id));
                project.SessionCursor = page.Data.Count == 0 ? null : page.Next;
            }
            else
            {
                // Keep selected metadata until its own GET determines whether it is unavailable.
                WorkspaceSession? selected = project.Sessions.FirstOrDefault(s => s.Id == project.SelectedId);
                SetSessions(project, page);
                if (selected is not null && !project.Sessions.Any(s => s.Id == selected.Id))
                {
                    project.Sessions.Add(selected);
                }

                project.Uncertain = false;
            }

            project.SessionsLoaded = true;
            project.SessionsError = null;
        }
        catch (WorkspaceException error)
        {
            if (OwnsLoad() && Current())
            {
                if (error.Failure == WorkspaceFailure.Unauthorized)
                {
                    _connections.RejectAuthorization(connection);
                }
                else
                {
                    project.SessionsError = error.Failure == WorkspaceFailure.Cancelled && load.IsCancellationRequested
                        ? "Loading timed out or was cancelled. Retry when ready."
                        : error.Message;
                }
            }
        }
        catch (Exception)
        {
            if (OwnsLoad() && Current())
            {
                project.SessionsError = "Sessions unavailable. Check the server and retry.";
            }
        }
        finally
        {
            if (OwnsLoad())
            {
                project.SessionLoad = null;
                project.SessionsLoading = false;
                if (Current())
                {
                    Notify();
                }
            }
        }
    }

    public Task LoadOlderHistoryAsync()
    {
        if (!CanLoadHistory || _project is not { SelectedId: { } id } project)
        {
            return Task.CompletedTask;
        }

        string cursor = project.HistoryCursor!;
        return RunAsync("Loading older messages…", async (context, token, current) =>
        {
            WorkspaceSession metadata = await context.SessionAsync(id, token).ConfigureAwait(true);
            ValidateSessions(project.Location, [metadata]);
            if (metadata.Id != id)
            {
                throw Ownership();
            }

            WorkspacePage<HistoryMessage> page = await context.HistoryAsync(id, cursor, token).ConfigureAwait(true);
            ValidateCursor(cursor, page.Next, project.HistoryCursors, page.Data.Count, page.Data.Any(m => !History.Any(old => old.Id == m.Id)));
            if (!current())
            {
                return;
            }

            project.HistoryCursors.Add(cursor);
            History = page.Data.DistinctBy(m => m.Id).Where(m => !History.Any(old => old.Id == m.Id)).Reverse().Concat(History).ToArray();
            project.HistoryCursor = page.Data.Count == 0 ? null : page.Next;
        }, LoadOlderHistoryAsync);
    }

    public Task CreateSessionAsync()
    {
        if (!CanCreate || _project is not { } project)
        {
            return Task.CompletedTask;
        }

        UserNavigation?.Invoke(this, EventArgs.Empty);
        CancelProjectLoad(project);
        project.Creating = true;
        return RunAsync("Creating session…", async (context, token, current) =>
        {
            WorkspaceSession created;
            try
            {
                created = await context.CreateAsync(project.Location.Directory, token).ConfigureAwait(true);
                ValidateSessions(project.Location, [created]);
            }
            catch
            {
                project.Uncertain = true;
                throw;
            }
            finally
            {
                project.Creating = false;
            }
            // The creation belongs to this project even when navigation has moved away.
            if (_disposed || !_backends.Values.Any(b => b.Projects.ContainsValue(project)))
            {
                return;
            }

            project.Drafts[created.Id] = project.Drafts.GetValueOrDefault("") ?? "";
            project.Drafts[""] = "";
            if (!current())
            {
                return;
            }

            project.Sessions.Insert(0, created);
            project.SelectedId = created.Id;
            project.ConfirmedId = created.Id;
            NavigationChanged?.Invoke(this, EventArgs.Empty);
            History = [];
            HistoryLoaded = false;
            Notify();
            WorkspacePage<WorkspaceSession> page = await context.SessionsAsync(project.Location.Directory, null, token).ConfigureAwait(true);
            ValidateSessions(project.Location, page.Data);
            if (!current())
            {
                return;
            }

            SetSessions(project, page);
            if (!project.Sessions.Any(s => s.Id == created.Id))
            {
                project.Sessions.Insert(0, created);
            }

            await ReadHistoryAsync(context, project, created.Id, current, token).ConfigureAwait(true);
        }, async () =>
        {
            await RefreshSessionsAsync().ConfigureAwait(true);
            if (Error is null && project.SelectedId is { } id)
            {
                await SelectSessionAsync(id).ConfigureAwait(true);
            }
        });
    }

    public Task RetryAsync() => CanRetry ? _retry!() : Task.CompletedTask;

    public async Task SendTaskAsync()
    {
        if (_project is not { SelectedId: { } id } project || Tasks is not { } workflow || workflow.Find(id) is not { CanSend: true } state)
        {
            return;
        }

        string text = project.Drafts.GetValueOrDefault(id) ?? "";
        await workflow.SendAsync(state, text).ConfigureAwait(true);
        // Only the admitted, unchanged draft is consumed. Navigation and later typing keep their own drafts.
        if (state.Accepted && project.Drafts.GetValueOrDefault(id) == text)
        {
            project.Drafts[id] = "";
        }

        Notify();
    }

    private async Task RunAsync(string status, Func<IWorkspaceConnection, CancellationToken, Func<bool>, Task> action, Func<Task> retry)
    {
        if (_disposed || _connections.Context is not { } context)
        {
            return;
        }

        CancelLoad();
        long generation = _generation;
        long connection = _connections.Generation;
        using CancellationTokenSource load = new(TimeSpan.FromSeconds(15));
        _load = load;
        bool Current() => !_disposed && generation == _generation && connection == _connections.Generation;
        IsLoading = true;
        Error = null;
        _retry = null;
        Status = status;
        Notify();
        try
        {
            await action(context, load.Token, Current).ConfigureAwait(true);
            if (Current())
            {
                Status = Error is not null ? "Selection unavailable. Retry or select another session." : Tasks is null ? "Ready. Live tasks are unavailable on this connection." : "Ready. Select a model and agent to run a task.";
            }
        }
        catch (WorkspaceException error)
        {
            if (Current() && error.Failure == WorkspaceFailure.Unauthorized)
            {
                _connections.RejectAuthorization(connection);
            }
            else if (Current())
            {
                Status = "Loading failed. Loaded data and drafts have been retained.";
                Error = error.Failure == WorkspaceFailure.Cancelled && load.IsCancellationRequested ? "Loading timed out or was cancelled. Retry when ready." : error.Message;
                if (_project?.Uncertain == true)
                {
                    Error += " Creation may have reached the server. Refresh sessions to reconcile before creating again.";
                }

                _retry = retry;
                if (_project is { SelectedId: not null, Uncertain: false } && error.Failure is WorkspaceFailure.Transport or WorkspaceFailure.Timeout)
                {
                    Tasks?.ReportReadFailure(error);
                }
            }
        }
        catch (Exception)
        {
            if (Current())
            {
                Status = "Loading failed. Loaded data and drafts have been retained.";
                Error = "Loading failed. Check the server and retry.";
                _retry = retry;
            }
        }
        finally
        {
            if (Current())
            {
                _load = null;
                IsLoading = false;
                Notify();
            }
        }
    }

    private static void ValidateSessions(ProjectLocation location, IReadOnlyList<WorkspaceSession> sessions)
    {
        if (sessions.Any(s => s.Directory != location.Directory || s.ProjectId != location.ProjectId))
        {
            throw Ownership();
        }
    }

    private static WorkspaceException Ownership() => new(WorkspaceFailure.Protocol, "The session belongs to a different server directory. Refresh the project and select its session again.");
    private static void ValidateCursor(string? cursor, string? next, HashSet<string> seen, int count, bool progress)
    {
        if (count > 0 && cursor is not null && (!progress || next == cursor || (next is not null && seen.Contains(next))))
        {
            throw new WorkspaceException(WorkspaceFailure.Protocol, "The server repeated a pagination cursor or returned no new entries. Loaded data was retained; retry or refresh.");
        }
    }

    private static void SetSessions(Project project, WorkspacePage<WorkspaceSession> page)
    {
        project.Sessions.Clear();
        project.Sessions.AddRange(page.Data.DistinctBy(s => s.Id));
        project.SessionCursors.Clear();
        project.SessionCursor = page.Data.Count == 0 ? null : page.Next;
        project.SessionsLoaded = true;
        project.SessionsLoading = false;
        project.SessionsError = null;
    }

    private static void CancelProjectLoad(Project project)
    {
        CancellationTokenSource? load = project.SessionLoad;
        project.SessionLoad = null;
        project.SessionsLoading = false;
        project.SessionsError = null;
        load?.Cancel();
    }

    private void CancelProjectSessionLoads()
    {
        foreach (Backend backend in _backends.Values)
        {
            foreach (Project project in backend.Projects.Values)
            {
                CancelProjectLoad(project);
            }
        }
    }

    private void CancelLoad()
    {
        _generation++;
        _load?.Cancel();
        _load = null;
        IsLoading = false;
    }
    private void TaskChanged(object? sender, EventArgs args)
    {
        if (!IsLoading && !HistoryLoaded && Error is not null && _project is { SelectedId: { } id, Uncertain: false } project &&
            Tasks is { Recovery: WorkspaceRecovery.Current } tasks && tasks.Find(id) is { Hydrated: true, Stale: false } state)
        {
            History = state.History;
            HistoryLoaded = true;
            project.HistoryCursor = state.HistoryCursor;
            project.HistoryCursors.Clear();
            project.ConfirmedId = id;
            NavigationChanged?.Invoke(this, EventArgs.Empty);
            Error = null;
            _retry = null;
            Status = "Ready. Server state refreshed.";
        }
        Notify();
    }
    private void Notify() => Changed?.Invoke(this, EventArgs.Empty);
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Files.Dispose();
        Tasks?.Dispose();
        Tasks = null;
        _connections.Changed -= ConnectionChangedAsync;
        CancelProjectSessionLoads();
        CancelLoad();
        foreach (Backend backend in _backends.Values)
        {
            foreach (Project project in backend.Projects.Values)
            {
                project.Drafts.Clear();
            }
        }
        _backends.Clear();
        _project = null;
        _backend = null;
        History = [];
        _retry = null;
    }

    private sealed class Backend(ServerProfile profile)
    {
        public ServerProfile Profile { get; } = profile;
        public string? Directory { get; set; }
        public Dictionary<string, Project> Projects { get; } = new(StringComparer.Ordinal);
        public IReadOnlyList<SessionTaskState> TaskStates { get; set; } = [];
    }
    private sealed class Project(ProjectLocation location)
    {
        public ProjectLocation Location { get; set; } = location;
        public bool Listed { get; set; } = true;
        public string? OpenError { get; set; }
        public List<WorkspaceSession> Sessions { get; } = [];
        public bool SessionsLoaded { get; set; }
        public bool SessionsLoading { get; set; }
        public string? SessionsError { get; set; }
        public CancellationTokenSource? SessionLoad { get; set; }
        public Dictionary<string, string> Drafts { get; } = new(StringComparer.Ordinal);
        public string? SelectedId { get; set; }
        public string? ConfirmedId { get; set; }
        public string? SessionCursor { get; set; }
        public string? HistoryCursor { get; set; }
        public HashSet<string> SessionCursors { get; } = new(StringComparer.Ordinal);
        public HashSet<string> HistoryCursors { get; } = new(StringComparer.Ordinal);
        public bool Creating { get; set; }
        public bool Uncertain { get; set; }
    }
}
