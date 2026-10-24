using OpenCodeDesktop.Core.Connections;

namespace OpenCodeDesktop.Core.Workspaces;

/// <summary>UI-independent workspace use case. State and events belong to the caller's synchronization context.</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2213:Disposable fields should be disposed", Justification = "Each RunAsync owns its CancellationTokenSource in a using scope until the pending operation exits; navigation only cancels it.")]
public sealed class LiveWorkspace : IDisposable
{
    private readonly ServerConnections _connections;
    private readonly Dictionary<Guid, Backend> _backends = [];
    private CancellationTokenSource? _load;
    private long _generation;
    private long _connectionGeneration = -1;
    private Backend? _backend;
    private Project? _project;
    private bool _disposed;
    private Func<Task>? _retry;

    public LiveWorkspace(ServerConnections connections)
    {
        ArgumentNullException.ThrowIfNull(connections);
        _connections = connections;
        connections.Changed += ConnectionChangedAsync;
        ConnectionChangedAsync(this, EventArgs.Empty);
    }

    public event EventHandler? Changed;
    public bool HasConnected { get; private set; }
    public bool IsConnected => _connections.Context is not null;
    public ActiveServer? Server => _connections.Active;
    public bool IsLoading { get; private set; }
    public string Status { get; private set; } = "Connect in Settings to open a server directory.";
    public string? Error { get; private set; }
    public bool CanRetry => _retry is not null && !IsLoading;
    public ProjectLocation? Location => IsConnected ? _project?.Location : null;
    public IReadOnlyList<string> OpenedDirectories => _backend?.Projects.Keys.ToArray() ?? [];
    public IReadOnlyList<WorkspaceSession> Sessions => IsConnected ? _project?.Sessions.AsReadOnly() ?? (IReadOnlyList<WorkspaceSession>)[] : [];
    public WorkspaceSession? Selected => Sessions.FirstOrDefault(s => s.Id == _project?.SelectedId);
    public string? SelectedSessionId => _project?.SelectedId;
    public IReadOnlyList<HistoryMessage> History { get; private set; } = [];
    public bool HistoryLoaded { get; private set; }
    public bool CanLoadSessions => IsConnected && _project?.SessionCursor is not null && !IsLoading;
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

    private async void ConnectionChangedAsync(object? sender, EventArgs args)
    {
        if (_disposed)
        {
            return;
        }

        foreach (Guid id in _backends.Keys.ToArray())
        {
            if (!_connections.Profiles.Any(p => p == _backends[id].Profile))
            {
                _backends.Remove(id);
            }
        }
        if (_connectionGeneration == _connections.Generation)
        {
            return;
        }

        _connectionGeneration = _connections.Generation;
        CancelLoad();
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
            Status = "Enter a server-side directory and choose Open.";
            string? remembered = _backend.Directory;
            Notify();
            if (remembered is not null)
            {
                await OpenProjectAsync(remembered).ConfigureAwait(true);
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
        if (string.IsNullOrWhiteSpace(directory) || !IsConnected)
        {
            return Task.CompletedTask;
        }

        return RunAsync("Opening server directory…", async (context, token, current) =>
        {
            ProjectLocation location = await context.ResolveAsync(directory, token).ConfigureAwait(true);
            await context.ValidateDirectoryAsync(location.Directory, token).ConfigureAwait(true);
            WorkspacePage<WorkspaceSession> page = await context.SessionsAsync(location.Directory, null, token).ConfigureAwait(true);
            ValidateSessions(location, page.Data);
            if (!current())
            {
                return;
            }

            if (!_backend!.Projects.TryGetValue(location.Directory, out Project? project))
            {
                project = new(location);
                _backend.Projects.Add(location.Directory, project);
            }
            project.Location = location;
            WorkspaceSession? rememberedSession = project.Sessions.FirstOrDefault(s => s.Id == project.SelectedId);
            SetSessions(project, page);
            if (rememberedSession is not null && !project.Sessions.Any(s => s.Id == rememberedSession.Id))
            {
                project.Sessions.Add(rememberedSession);
            }
            _project = project;
            _backend.Directory = location.Directory;
            History = [];
            HistoryLoaded = false;
            if (project.SelectedId is { } selected)
            {
                Notify();
                await ReadHistoryAsync(context, project, selected, current, token).ConfigureAwait(true);
            }
            else
            {
                project.SelectedId = null;
            }
        }, () => OpenProjectAsync(directory));
    }

    public Task SelectSessionAsync(string? id)
    {
        if (_project is null || !IsConnected || (id is not null && !Sessions.Any(s => s.Id == id)))
        {
            return Task.CompletedTask;
        }

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
            Notify();
            return Task.CompletedTask;
        }
        Project project = _project;
        return RunAsync("Loading conversation history…", (context, token, current) => ReadHistoryAsync(context, project, id, current, token), () => SelectSessionAsync(id));
    }

    private async Task ReadHistoryAsync(IWorkspaceConnection context, Project project, string id, Func<bool> current, CancellationToken token)
    {
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
    }

    public Task RefreshSessionsAsync() => SessionsPageAsync(false);
    public Task LoadMoreSessionsAsync() => CanLoadSessions ? SessionsPageAsync(true) : Task.CompletedTask;

    private Task SessionsPageAsync(bool more)
    {
        if (_project is not { } project || !IsConnected)
        {
            return Task.CompletedTask;
        }

        string? cursor = more ? project.SessionCursor : null;
        return RunAsync(more ? "Loading more sessions…" : "Refreshing sessions…", async (context, token, current) =>
        {
            WorkspacePage<WorkspaceSession> page = await context.SessionsAsync(project.Location.Directory, cursor, token).ConfigureAwait(true);
            ValidateSessions(project.Location, page.Data);
            ValidateCursor(cursor, page.Next, more ? project.SessionCursors : [], page.Data.Count, page.Data.Any(s => !project.Sessions.Any(old => old.Id == s.Id)));
            if (!current())
            {
                return;
            }

            if (!more)
            {
                // Keep selected metadata until its own GET determines whether it is unavailable.
                WorkspaceSession? selected = Selected;
                SetSessions(project, page);
                if (selected is not null && !project.Sessions.Any(s => s.Id == selected.Id))
                {
                    project.Sessions.Add(selected);
                }

                project.Uncertain = false;
            }
            else
            {
                if (cursor is not null)
                {
                    project.SessionCursors.Add(cursor);
                }

                project.Sessions.AddRange(page.Data.Where(s => !project.Sessions.Any(old => old.Id == s.Id)).DistinctBy(s => s.Id));
                project.SessionCursor = page.Data.Count == 0 ? null : page.Next;
            }
        }, () => SessionsPageAsync(more));
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
                Status = "Ready. History is recorded activity; live execution and sending are unavailable.";
            }
        }
        catch (WorkspaceException error)
        {
            if (error.Failure == WorkspaceFailure.Unauthorized)
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
    }

    private void CancelLoad()
    {
        _generation++;
        _load?.Cancel();
        _load = null;
        IsLoading = false;
    }
    private void Notify() => Changed?.Invoke(this, EventArgs.Empty);
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _connections.Changed -= ConnectionChangedAsync;
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
    }
    private sealed class Project(ProjectLocation location)
    {
        public ProjectLocation Location { get; set; } = location;
        public List<WorkspaceSession> Sessions { get; } = [];
        public Dictionary<string, string> Drafts { get; } = new(StringComparer.Ordinal);
        public string? SelectedId { get; set; }
        public string? SessionCursor { get; set; }
        public string? HistoryCursor { get; set; }
        public HashSet<string> SessionCursors { get; } = new(StringComparer.Ordinal);
        public HashSet<string> HistoryCursors { get; } = new(StringComparer.Ordinal);
        public bool Creating { get; set; }
        public bool Uncertain { get; set; }
    }
}
