namespace OpenCodeDesktop.Core.Workspaces;

public sealed partial class LiveWorkspace
{
    public event EventHandler<WorkspaceSession>? SessionMetadataReconciled;

    private async void SessionMetadataChangedAsync(object? sender, TaskEvent args)
    {
        if (sender != Tasks || _backend is not { } backend) { return; }
        foreach (Project project in backend.Projects.Values.Where(p => p.Listed && p.SessionsLoaded && !p.OrganizationBusy).ToArray())
        {
            await RefreshProjectSessionsAsync(project.Location.Directory).ConfigureAwait(true);
            if (sender != Tasks) { return; }
        }
    }

    private static Task<WorkspacePage<WorkspaceSession>> QuerySessionsAsync(IWorkspaceConnection context, string directory,
        SessionHistoryFilter filter, string? cursor, CancellationToken token) => context is ISessionOrganizationConnection organization
        ? organization.SearchSessionsAsync(directory, filter, cursor, token)
        : context.SessionsAsync(directory, cursor, token);

    private static bool Matches(SessionHistoryFilter filter, WorkspaceSession session) => session.Archived == filter.Archived &&
        (filter.Search.Length == 0 || session.Title.Contains(filter.Search, StringComparison.OrdinalIgnoreCase));

    public Task FilterProjectSessionsAsync(string directory, SessionHistoryFilter filter)
    {
        ArgumentNullException.ThrowIfNull(filter);
        if (!IsConnected || _backend?.Projects.GetValueOrDefault(directory) is not { Listed: true } project) { return Task.CompletedTask; }
        if (_project == project) { CancelLoad(); }
        CancelProjectLoad(project);
        project.Filter = filter;
        project.Sessions.Clear();
        project.SessionsLoaded = false;
        project.SessionCursor = null;
        project.SessionCursors.Clear();
        return LoadProjectSessionsCoreAsync(project, false);
    }

    public bool CanOrganizeSession(string directory, string id) => IsConnected &&
        _connections.Context is ISessionOrganizationConnection &&
        _backend?.Projects.GetValueOrDefault(directory) is { Listed: true, OrganizationBusy: false } project &&
        !project.UnconfirmedUpdates.Contains(id) && project.Sessions.Any(s => s.Id == id);

    public bool CanArchiveSession(string directory, string id) => CanOrganizeSession(directory, id) &&
        _connections.Context is ITaskConnection && Tasks is { Recovery: WorkspaceRecovery.Current, AttentionError: null } && SafeToArchive(Tasks.Find(id));

    private static bool SafeToArchive(SessionTaskState? state) => state is null ||
        state is { Active: false, Busy: false, Loading: false, Refreshing: false, Stopping: false, Stale: false, DeliveryUncertain: false, Submission: null,
            Inbox.Count: 0, Permissions.Count: 0, Forms.Count: 0 };

    public Task RenameSessionAsync(string directory, string id, string title)
    {
        if (!SessionTitles.IsValid(title))
        {
            if (_backend?.Projects.GetValueOrDefault(directory) is { } project) { project.SessionsError = SessionTitles.Guidance; Notify(); }
            return Task.CompletedTask;
        }
        return UpdateSessionAsync(directory, id, title, null);
    }

    public Task ArchiveSessionAsync(string directory, string id, bool archived) => UpdateSessionAsync(directory, id, null, archived);

    private async Task UpdateSessionAsync(string directory, string id, string? title, bool? archived)
    {
        if (!CanOrganizeSession(directory, id) || archived == true && !CanArchiveSession(directory, id) ||
            _backend?.Projects.GetValueOrDefault(directory) is not { } project ||
            _connections.Context is not ISessionOrganizationConnection api || _connections.Context is not { } context) { return; }
        long connection = _connections.Generation;
        Backend backend = _backend;
        bool Current() => !_disposed && connection == _connections.Generation && _backend == backend && project.Listed;
        project.OrganizationBusy = true;
        project.SessionsError = null;
        CancelProjectLoad(project);
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(15));
        bool sent = false;
        Notify();
        try
        {
            WorkspaceSession before = await context.SessionAsync(id, timeout.Token).ConfigureAwait(true);
            ValidateSessionIdentity(project, id, before);
            if (archived == true)
            {
                TaskSnapshot snapshot = await ((ITaskConnection)context).TaskSnapshotAsync(id, timeout.Token).ConfigureAwait(true);
                ValidateSessionIdentity(project, id, snapshot.Session);
                if (snapshot.Active || snapshot.Inbox.Count + snapshot.Forms.Count + snapshot.Permissions.Count > 0 || !SafeToArchive(Tasks?.Find(id)))
                {
                    throw new WorkspaceException(WorkspaceFailure.Protocol, "Session is running, waiting, or has unresolved delivery. Refresh its live state before archiving.");
                }
            }
            if (!Current()) { return; }
            sent = true;
            project.UnconfirmedUpdates.Add(id);
            if (title is not null) { await api.RenameSessionAsync(id, title, timeout.Token).ConfigureAwait(true); }
            else { await api.ArchiveSessionAsync(id, archived!.Value, timeout.Token).ConfigureAwait(true); }
            WorkspaceSession after = await context.SessionAsync(id, timeout.Token).ConfigureAwait(true);
            ValidateSessionIdentity(project, id, after);
            if (!Current()) { return; }
            project.UnconfirmedUpdates.Remove(id);
            ApplySessionMetadata(project, after, archived == true);
            if (archived == true && context is ITaskConnection && Tasks is { } tasks)
            {
                // Archive is not an interrupt. Discover requests that raced the update even for hidden sessions.
                await tasks.RefreshAsync(tasks.Track(after), reconnect: false).ConfigureAwait(true);
            }
            if (Current()) { await RefreshProjectSessionsAsync(directory).ConfigureAwait(true); }
        }
        catch (Exception error)
        {
            if (Current())
            {
                if (error is WorkspaceException { Failure: WorkspaceFailure.Unauthorized }) { _connections.RejectAuthorization(connection); }
                else
                {
                    project.SessionsError = sent
                        ? "Update was not confirmed. Refresh sessions to reconcile before another change. No update was retried; drafts are retained."
                        : error is WorkspaceException failure ? failure.Message : "Session could not be checked. Refresh sessions and try again.";
                }
            }
        }
        finally
        {
            project.OrganizationBusy = false;
            if (Current()) { Notify(); }
        }
    }

    private static void ValidateSessionIdentity(Project project, string id, WorkspaceSession session)
    {
        ValidateSessions(project.Location, [session]);
        if (session.Id != id) { throw Ownership(); }
    }

    private void ApplySessionMetadata(Project project, WorkspaceSession session, bool clearArchivedSelection)
    {
        SessionMetadataReconciled?.Invoke(this, session);
        int index = project.Sessions.FindIndex(s => s.Id == session.Id);
        if (index >= 0)
        {
            if (Matches(project.Filter, session)) { project.Sessions[index] = session; }
            else { project.Sessions.RemoveAt(index); }
        }
        if (project.SelectedId != session.Id) { return; }
        project.Selection = session;
        if (session.Archived && clearArchivedSelection)
        {
            project.SelectedId = null;
            project.ConfirmedId = null;
            project.Selection = null;
            project.HistoryCursor = null;
            if (_project == project)
            {
                CancelLoad();
                History = [];
                HistoryLoaded = false;
                Error = "The selected session is archived. Open Archived history to restore or reopen it. Its draft is retained for this app session.";
            }
        }
        NavigationChanged?.Invoke(this, EventArgs.Empty);
    }
}
