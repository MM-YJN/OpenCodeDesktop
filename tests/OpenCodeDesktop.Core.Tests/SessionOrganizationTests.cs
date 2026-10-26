using System.Threading.Channels;

using OpenCodeDesktop.Core.Connections;
using OpenCodeDesktop.Core.Workspaces;

namespace OpenCodeDesktop.Core.Tests;

public sealed class SessionOrganizationTests
{
    [Fact]
    public async Task FilterChangeSupersedesAnOpeningProjectPage()
    {
        using Harness h = await Harness.StartAsync();
        await h.Workspace.OpenProjectAsync("/two");
        await h.Workspace.OpenProjectAsync("/one");
        TaskCompletionSource<WorkspacePage<WorkspaceSession>> old = new();
        h.Api.Query = (directory, filter, _) => directory == "/two" && filter.Search.Length == 0 ? old.Task :
            Task.FromResult(new WorkspacePage<WorkspaceSession>([], null));
        Task opening = h.Workspace.OpenProjectAsync("/two");
        await h.Workspace.FilterProjectSessionsAsync("/two", new("new filter"));
        old.SetResult(new([], "old cursor"));
        await opening;
        Assert.Equal("/one", h.Workspace.Location!.Directory);
        WorkspaceProject two = h.Workspace.Projects.Single(p => p.Location.Directory == "/two");
        Assert.Equal("new filter", two.Filter.Search);
        Assert.False(two.CanLoadMoreSessions);
        Assert.Contains("filter changed", h.Workspace.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LateMutationAfterConnectionChangeCannotReplaceCurrentWorkspace()
    {
        using Harness h = await Harness.StartAsync();
        await h.Workspace.SelectSessionAsync("a");
        TaskCompletionSource delivered = new();
        h.Api.UpdateAsync = () => delivered.Task;
        Task rename = h.Workspace.RenameSessionAsync("/one", "a", "late");
        h.Connections.Disconnect();
        await h.Connections.ConnectAsync(h.Profile.Id, "secret");
        await h.Workspace.ConnectionTransition;
        await h.Workspace.OpenProjectAsync("/two");
        h.Workspace.Draft = "current connection";
        delivered.SetResult();
        await rename;
        Assert.Equal("/two", h.Workspace.Location!.Directory);
        Assert.Equal("current connection", h.Workspace.Draft);
    }

    [Fact]
    public async Task PermissionRacingArchiveRemainsReachableOutsideActiveHistory()
    {
        using Harness h = await Harness.StartAsync();
        await h.Workspace.SelectSessionAsync("a");
        h.Api.Update = (id, _, archived) =>
        {
            h.Api.Data[id] = h.Api.Data[id] with { Archived = archived!.Value };
            h.Api.Pending = "permission";
        };
        await h.Workspace.ArchiveSessionAsync("/one", "a", true);
        Assert.Single(h.Workspace.Tasks!.Find("a")!.Permissions);
        await h.Workspace.SelectAttentionSessionAsync("a");
        Assert.Equal("a", h.Workspace.SelectedSessionId);
        Assert.True(h.Workspace.Selected!.Archived);
        Assert.DoesNotContain(h.Workspace.Sessions, session => session.Id == "a");
    }

    [Fact]
    public async Task ExternalRenameEventReconcilesSelectedMetadataWithoutMutation()
    {
        using Harness h = await Harness.StartAsync();
        await h.Workspace.SelectSessionAsync("a");
        h.Api.Data["a"] = h.Api.Data["a"] with { Title = "External title" };
        TaskCompletionSource observed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        h.Workspace.Changed += (_, _) => { if (h.Workspace.Selected?.Title == "External title") { observed.TrySetResult(); } };
        await h.Api.Events.Writer.WriteAsync(new("rename", "session.renamed", "a", null, 0, null), TestContext.Current.CancellationToken);
        await observed.Task.WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
        Assert.Equal(0, h.Api.Mutations);
    }

    [Fact]
    public async Task FiltersOwnPaginationAndSupersededResultsPreserveSelectionAndDraft()
    {
        using Harness h = await Harness.StartAsync();
        await h.Workspace.SelectSessionAsync("a");
        h.Workspace.Draft = "retain";
        TaskCompletionSource<WorkspacePage<WorkspaceSession>> slow = new();
        h.Api.Query = (_, filter, cursor) => filter.Search == "slow" ? slow.Task : Task.FromResult(
            new WorkspacePage<WorkspaceSession>(filter.Search == "empty" ? [] : [h.Api.Data[cursor is null ? "b" : "a"]], cursor is null && filter.Search == "paged" ? "filtered/#%" : null));
        Task previous = h.Workspace.FilterProjectSessionsAsync("/one", new("slow"));
        await h.Workspace.FilterProjectSessionsAsync("/one", new("empty", true));
        slow.SetResult(new([h.Api.Data["b"]], "stale"));
        await previous;
        Assert.Empty(h.Workspace.Sessions);
        Assert.Equal("a", h.Workspace.SelectedSessionId);
        Assert.Equal("retain", h.Workspace.Draft);
        Assert.False(h.Workspace.CanLoadSessions);
        await h.Workspace.FilterProjectSessionsAsync("/one", new("paged"));
        await h.Workspace.LoadMoreSessionsAsync();
        Assert.Equal(("/one", new SessionHistoryFilter("paged"), "filtered/#%"), h.Api.Queries[^1]);
        Assert.Equal(2, h.Workspace.Sessions.Count);
    }

    [Fact]
    public async Task RenameIsAuthoritativeAndUncertainDeliveryRequiresReadReconciliation()
    {
        using Harness h = await Harness.StartAsync();
        await h.Workspace.SelectSessionAsync("a");
        await h.Workspace.RenameSessionAsync("/one", "a", "  ");
        Assert.Equal(0, h.Api.Mutations);
        h.Api.Update = (id, title, _) => h.Api.Data[id] = h.Api.Data[id] with { Title = title + " authoritative" };
        await h.Workspace.RenameSessionAsync("/one", "a", "New");
        Assert.Equal("New authoritative", h.Workspace.Selected!.Title);
        h.Api.Update = (id, title, _) =>
        {
            h.Api.Data[id] = h.Api.Data[id] with { Title = title! };
            throw new WorkspaceException(WorkspaceFailure.Transport, "lost acknowledgement");
        };
        h.Workspace.Draft = "retain";
        await h.Workspace.RenameSessionAsync("/one", "a", "Delivered once");
        Assert.False(h.Workspace.CanOrganizeSession("/one", "a"));
        Assert.False(h.Workspace.CanArchiveSession("/one", "a"));
        Assert.Contains("not confirmed", h.Workspace.Projects[0].SessionsError, StringComparison.Ordinal);
        await h.Workspace.RenameSessionAsync("/one", "a", "Do not send");
        Assert.Equal(2, h.Api.Mutations);
        await h.Workspace.RefreshSessionsAsync();
        Assert.Equal("Delivered once", h.Workspace.Selected.Title);
        Assert.Equal("retain", h.Workspace.Draft);
        Assert.True(h.Workspace.CanOrganizeSession("/one", "a"));
    }

    [Fact]
    public async Task ArchiveAndRestorePreserveHistoryAndBothDrafts()
    {
        using Harness h = await Harness.StartAsync();
        h.Workspace.Draft = "project draft";
        await h.Workspace.SelectSessionAsync("a");
        h.Workspace.Draft = "session draft";
        await h.Workspace.ArchiveSessionAsync("/one", "a", true);
        Assert.Null(h.Workspace.SelectedSessionId);
        Assert.Equal("project draft", h.Workspace.Draft);
        Assert.DoesNotContain(h.Workspace.Sessions, s => s.Id == "a");
        await h.Workspace.FilterProjectSessionsAsync("/one", new("", true));
        Assert.Equal("a", Assert.Single(h.Workspace.Sessions).Id);
        await h.Workspace.ArchiveSessionAsync("/one", "a", false);
        Assert.Empty(h.Workspace.Sessions);
        await h.Workspace.FilterProjectSessionsAsync("/one", new());
        await h.Workspace.SelectSessionAsync("a");
        Assert.Equal("session draft", h.Workspace.Draft);
        Assert.Equal("preserved history", Assert.Single(h.Workspace.History).Text);
    }

    [Theory]
    [InlineData("active")]
    [InlineData("inbox")]
    [InlineData("permission")]
    [InlineData("form")]
    public async Task ArchiveRevalidatesUntrackedSessionAndRejectsPendingWork(string pending)
    {
        using Harness h = await Harness.StartAsync();
        h.Api.Pending = pending;
        await h.Workspace.ArchiveSessionAsync("/one", "b", true);
        Assert.Equal(0, h.Api.Mutations);
        Assert.False(h.Api.Data["b"].Archived);
        Assert.Contains("running, waiting", h.Workspace.Projects[0].SessionsError, StringComparison.Ordinal);
    }

    [Fact]
    public async Task KnownRunningSessionCannotArchiveAndAnotherProjectKeepsItsSelection()
    {
        using Harness h = await Harness.StartAsync();
        h.Api.Pending = "active";
        await h.Workspace.SelectSessionAsync("a");
        Assert.False(h.Workspace.CanArchiveSession("/one", "a"));
        await h.Workspace.ArchiveSessionAsync("/one", "a", true);
        Assert.Equal(0, h.Api.Mutations);
        h.Api.Pending = null;
        await h.Workspace.OpenProjectAsync("/two");
        h.Workspace.Draft = "other project";
        await h.Workspace.ArchiveSessionAsync("/one", "b", true);
        Assert.Equal("/two", h.Workspace.Location!.Directory);
        Assert.Equal("other project", h.Workspace.Draft);
        Assert.True(h.Api.Data["b"].Archived);
    }

    [Fact]
    public async Task ReconnectAndRefreshReconcileArchivedAndMissingSelectionsWithGuidance()
    {
        using Harness h = await Harness.StartAsync();
        await h.Workspace.SelectSessionAsync("a");
        h.Workspace.Draft = "retained";
        h.Api.Data["a"] = h.Api.Data["a"] with { Archived = true };
        h.Connections.Disconnect();
        await h.Connections.ConnectAsync(h.Profile.Id, "secret");
        await h.Workspace.ConnectionTransition;
        Assert.Null(h.Workspace.SelectedSessionId);
        Assert.Contains("Archived history", h.Workspace.Error, StringComparison.Ordinal);
        await h.Workspace.FilterProjectSessionsAsync("/one", new("", true));
        await h.Workspace.SelectSessionAsync("a");
        Assert.Equal("retained", h.Workspace.Draft);
        h.Api.Data.Remove("a");
        await h.Workspace.RefreshSessionsAsync();
        Assert.Null(h.Workspace.SelectedSessionId);
        Assert.Empty(h.Workspace.History);
        Assert.Contains("unavailable", h.Workspace.Error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("two\nlines")]
    [InlineData("control\0")]
    public void TitlesRejectEmptyAndControlCharacters(string title) => Assert.False(SessionTitles.IsValid(title));

    private sealed class Harness : IDisposable
    {
        public Api Api { get; } = new();
        public ServerConnections Connections { get; }
        public LiveWorkspace Workspace { get; }
        public ServerProfile Profile { get; }
        private Harness()
        {
            Connections = new(new Service(Api));
            Workspace = new(Connections);
            Profile = Connections.Save(null, "Organization", "http://127.0.0.1:4096")!;
        }
        public static async Task<Harness> StartAsync()
        {
            Harness h = new();
            await h.Connections.ConnectAsync(h.Profile.Id, "secret");
            await h.Workspace.OpenProjectAsync("/one");
            return h;
        }
        public void Dispose() { Workspace.Dispose(); Connections.Dispose(); }
    }
    private sealed class Service(Api api) : IServerConnectionService, IWorkspaceConnectionFactory
    {
        public Task<ConnectionResult> ProbeAsync(ServerAddress address, string password, CancellationToken cancellationToken) => Task.FromResult(new ConnectionResult(ConnectionOutcome.Ready, "Ready", "2.1.4"));
        public IWorkspaceConnection Create(ServerAddress address, string password) => api;
    }
    private sealed class Api : IWorkspaceConnection, ISessionOrganizationConnection, ITaskConnection
    {
        public Dictionary<string, WorkspaceSession> Data { get; } = new() { ["a"] = new("a", "/one", "global", "Alpha"), ["b"] = new("b", "/one", "global", "Beta") };
        public List<(string Directory, SessionHistoryFilter Filter, string? Cursor)> Queries { get; } = [];
        public Func<string, SessionHistoryFilter, string?, Task<WorkspacePage<WorkspaceSession>>>? Query { get; set; }
        public Action<string, string?, bool?>? Update { get; set; }
        public Func<Task>? UpdateAsync { get; set; }
        public string? Pending { get; set; }
        public int Mutations { get; private set; }
        public Channel<TaskEvent> Events { get; } = Channel.CreateUnbounded<TaskEvent>();
        public Task<ProjectLocation> ResolveAsync(string directory, CancellationToken cancellationToken) => Task.FromResult(new ProjectLocation(directory, "global"));
        public Task ValidateDirectoryAsync(string directory, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<WorkspacePage<WorkspaceSession>> SessionsAsync(string directory, string? cursor, CancellationToken cancellationToken) => SearchSessionsAsync(directory, new(), cursor, cancellationToken);
        public Task<WorkspacePage<WorkspaceSession>> SearchSessionsAsync(string directory, SessionHistoryFilter filter, string? cursor, CancellationToken cancellationToken)
        {
            Queries.Add((directory, filter, cursor));
            return Query?.Invoke(directory, filter, cursor) ?? Task.FromResult(new WorkspacePage<WorkspaceSession>(Data.Values.Where(s => s.Directory == directory && s.Archived == filter.Archived && s.Title.Contains(filter.Search, StringComparison.OrdinalIgnoreCase)).ToArray(), null));
        }
        public Task<WorkspaceSession> SessionAsync(string id, CancellationToken cancellationToken) => Data.TryGetValue(id, out WorkspaceSession? session) ? Task.FromResult(session) : throw new WorkspaceException(WorkspaceFailure.NotFound, "Missing");
        public Task<WorkspaceSession> CreateAsync(string directory, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<WorkspacePage<HistoryMessage>> HistoryAsync(string id, string? cursor, CancellationToken cancellationToken) => Task.FromResult(new WorkspacePage<HistoryMessage>([new("message", "user", "preserved history")], null));
        public Task RenameSessionAsync(string id, string title, CancellationToken cancellationToken) => Change(id, title, null);
        public Task ArchiveSessionAsync(string id, bool archived, CancellationToken cancellationToken) => Change(id, null, archived);
        private Task Change(string id, string? title, bool? archived)
        {
            Mutations++;
            if (UpdateAsync is not null) { return UpdateAsync(); }
            if (Update is not null) { Update(id, title, archived); }
            else { Data[id] = Data[id] with { Title = title ?? Data[id].Title, Archived = archived ?? Data[id].Archived }; }
            return Task.CompletedTask;
        }
        public Task<TaskCatalog> CatalogAsync(string directory, CancellationToken cancellationToken) => Task.FromResult(new TaskCatalog([], []));
        public Task<IReadOnlyList<string>> AttentionSessionsAsync(string directory, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<string>>([]);
        public Task<TaskSnapshot> TaskSnapshotAsync(string id, CancellationToken cancellationToken) => Task.FromResult(new TaskSnapshot(Data[id], Pending == "active", null, null, null, null,
            Pending == "inbox" ? ["input"] : [], Pending == "permission" ? [new("p", id, "read", [], [])] : [], Pending == "form" ? [new("f", id, "Question", [])] : []));
        public Task SelectModelAsync(string id, TaskModel model, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task SelectAgentAsync(string id, string agent, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task SubmitAsync(string id, TaskSubmission submission, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task InterruptAsync(string id, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task ReplyPermissionAsync(string id, string requestId, string decision, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<TaskForm> FormAsync(string id, string formId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task ReplyFormAsync(string id, string formId, IReadOnlyDictionary<string, object> answer, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task CancelFormAsync(string id, string formId, CancellationToken cancellationToken) => Task.CompletedTask;
        public async Task WatchAsync(Func<TaskEvent, Task> receive, CancellationToken cancellationToken)
        {
            await receive(new("connected", "server.connected", null, null, 0, null));
            await foreach (TaskEvent update in Events.Reader.ReadAllAsync(cancellationToken)) { await receive(update); }
        }
        public void Dispose() { }
    }
}
