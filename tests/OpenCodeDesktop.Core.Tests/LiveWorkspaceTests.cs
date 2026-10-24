using OpenCodeDesktop.Core.Connections;
using OpenCodeDesktop.Core.Workspaces;

namespace OpenCodeDesktop.Core.Tests;

public sealed class LiveWorkspaceTests
{
    [Fact]
    public async Task GlobalDirectoriesAndSessionDraftsAreIndependentAndFailedCandidateRetainsHistory()
    {
        using Harness h = await Harness.StartAsync();
        await h.Workspace.OpenProjectAsync("/one");
        h.Workspace.Draft = "new one";
        await h.Workspace.SelectSessionAsync("a");
        h.Workspace.Draft = "session a";
        await h.Workspace.SelectSessionAsync("b");
        h.Workspace.Draft = "session b";
        await h.Workspace.OpenProjectAsync("/two");
        Assert.Null(h.Workspace.Selected);
        Assert.Empty(h.Workspace.Draft);
        h.Workspace.Draft = "new two";
        await h.Workspace.OpenProjectAsync("/one");
        Assert.Equal("session b", h.Workspace.Draft);
        Assert.Equal("b", h.Workspace.Selected!.Id);
        await h.Workspace.SelectSessionAsync("a");
        Assert.Equal("session a", h.Workspace.Draft);
        h.Api.Validate = (_, _) => throw Failure();
        await h.Workspace.OpenProjectAsync("/file");
        Assert.Equal("/one", h.Workspace.Location!.Directory);
        Assert.Equal("session a", h.Workspace.Draft);
        Assert.Equal("a", Assert.Single(h.Workspace.History).Id);
        Assert.NotNull(h.Workspace.Error);
        await h.Workspace.SelectSessionAsync(null);
        Assert.Equal("new one", h.Workspace.Draft);
    }

    [Fact]
    public async Task SelectedHistoryIsHiddenWhileLoadingAndWrongDirectoryMetadataIsRejectedBeforeHistoryRequest()
    {
        using Harness h = await Harness.StartAsync();
        await h.Workspace.OpenProjectAsync("/one");
        await h.Workspace.SelectSessionAsync("a");
        TaskCompletionSource<WorkspaceSession> pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
        h.Api.Metadata = (_, _) => pending.Task;
        Task loading = h.Workspace.SelectSessionAsync("b");
        Assert.True(h.Workspace.IsLoading);
        Assert.Empty(h.Workspace.History);
        h.Workspace.Draft = "editable during loading";
        int historyCalls = h.Api.HistoryCalls;
        pending.SetResult(new("b", "/other", "global", "Other"));
        await loading;
        Assert.Equal(historyCalls, h.Api.HistoryCalls);
        Assert.Empty(h.Workspace.History);
        Assert.Equal("editable during loading", h.Workspace.Draft);
        Assert.True(h.Workspace.CanRetry);
        Assert.False(h.Workspace.IsLoading);
    }

    [Fact]
    public async Task OlderPagesReverseDeduplicateRetainDataOnFailureAndStopOnEmptyFinalPage()
    {
        using Harness h = await Harness.StartAsync();
        await h.Workspace.OpenProjectAsync("/one");
        bool fail = true;
        h.Api.History = (_, cursor, _) => Task.FromResult(cursor switch
        {
            null => new WorkspacePage<HistoryMessage>([new("5", "user", "five"), new("4", "user", "four")], "opaque/next"),
            "opaque/next" when fail => throw Failure(),
            "opaque/next" => new([new("4", "user", "duplicate"), new("3", "user", "three")], "last"),
            "last" => new([new("1", "user", "one")], "empty"),
            _ => new([], "ignored"),
        });
        await h.Workspace.SelectSessionAsync("a");
        h.Workspace.Draft = "keep";
        await h.Workspace.LoadOlderHistoryAsync();
        Assert.Equal(new[] { "4", "5" }, h.Workspace.History.Select(m => m.Id));
        Assert.NotNull(h.Workspace.Error);
        fail = false;
        await h.Workspace.RetryAsync();
        await h.Workspace.LoadOlderHistoryAsync();
        Assert.True(h.Workspace.CanLoadHistory);
        await h.Workspace.LoadOlderHistoryAsync();
        Assert.False(h.Workspace.CanLoadHistory);
        Assert.Equal(new[] { "1", "3", "4", "5" }, h.Workspace.History.Select(m => m.Id));
        Assert.Equal("keep", h.Workspace.Draft);
    }

    [Fact]
    public async Task RepeatedCursorDoesNotReplaceLoadedHistory()
    {
        using Harness h = await Harness.StartAsync();
        await h.Workspace.OpenProjectAsync("/one");
        h.Api.History = (_, cursor, _) => Task.FromResult(new WorkspacePage<HistoryMessage>([new(cursor is null ? "2" : "1", "user", "text")], "same"));
        await h.Workspace.SelectSessionAsync("a");
        await h.Workspace.LoadOlderHistoryAsync();
        Assert.NotNull(h.Workspace.Error);
        Assert.Equal("2", Assert.Single(h.Workspace.History).Id);
    }

    [Fact]
    public async Task SessionPaginationPreservesDirectoryDeduplicatesAndStopsAfterEmptyPage()
    {
        using Harness h = await Harness.StartAsync();
        h.Api.List = (directory, cursor, _) =>
        {
            Assert.Equal("/one", directory);
            return Task.FromResult(cursor switch
            {
                null => new WorkspacePage<WorkspaceSession>([new("a", directory, "global", "A")], "next"),
                "next" => new([new("a", directory, "global", "A"), new("b", directory, "global", "B")], "empty"),
                _ => new([], "ignored"),
            });
        };
        await h.Workspace.OpenProjectAsync("/one");
        await h.Workspace.LoadMoreSessionsAsync();
        Assert.Equal(2, h.Workspace.Sessions.Count);
        await h.Workspace.LoadMoreSessionsAsync();
        Assert.False(h.Workspace.CanLoadSessions);
    }

    [Fact]
    public async Task CreationGatesDuplicatesTransfersOnlyConfirmedDraftAndReconcilesUncertainDelivery()
    {
        using Harness h = await Harness.StartAsync();
        await h.Workspace.OpenProjectAsync("/one");
        h.Workspace.Draft = "unsent";
        TaskCompletionSource<WorkspaceSession> pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
        h.Api.Create = (_, _) => pending.Task;
        Task create = h.Workspace.CreateSessionAsync();
        await h.Workspace.CreateSessionAsync();
        Assert.Equal(1, h.Api.CreateCalls);
        Assert.Equal("unsent", h.Workspace.Draft);
        pending.SetException(Failure());
        await create;
        Assert.True(h.Workspace.CreationUncertain);
        Assert.False(h.Workspace.CanCreate);
        Assert.Equal("unsent", h.Workspace.Draft);
        await h.Workspace.RetryAsync();
        Assert.True(h.Workspace.CanCreate);
        Assert.Equal(1, h.Api.CreateCalls);
        h.Api.Create = (dir, _) => Task.FromResult(new WorkspaceSession("created", dir, "global", "Created"));
        await h.Workspace.CreateSessionAsync();
        Assert.Equal("created", h.Workspace.Selected!.Id);
        Assert.Equal("unsent", h.Workspace.Draft);
        await h.Workspace.SelectSessionAsync(null);
        Assert.Empty(h.Workspace.Draft);
    }

    [Fact]
    public async Task FailedSwitchKeepsWorkspaceSuccessfulSwitchDisposesAuthAndReturningRevalidatesDraft()
    {
        using Harness h = await Harness.StartAsync();
        await h.Workspace.OpenProjectAsync("/one");
        h.Workspace.Draft = "first backend";
        ServerProfile second = h.Connections.Save(null, "Second", "http://127.0.0.1:4097")!;
        h.Service.Result = new(ConnectionOutcome.AuthenticationFailed, "Rejected");
        await h.Connections.ConnectAsync(second.Id, "wrong");
        Assert.Equal("/one", h.Workspace.Location!.Directory);
        Assert.False(h.Api.Disposed);
        h.Service.Result = Ready;
        await h.Connections.ConnectAsync(second.Id, "new password");
        Assert.True(h.Api.Disposed);
        Assert.Null(h.Workspace.Location);
        Assert.Empty(h.Workspace.History);
        await h.Connections.ConnectAsync(h.First.Id, "entered again");
        await WaitIdleAsync(h.Workspace);
        Assert.Equal("first backend", h.Workspace.Draft);
        Assert.Equal("/one", h.Workspace.Location!.Directory);
        Assert.Equal(3, h.Service.Created.Count);
    }

    [Fact]
    public async Task LaterUnauthorizedDisposesConnectionHidesContentAndPreservesDraftForExplicitReconnect()
    {
        using Harness h = await Harness.StartAsync();
        await h.Workspace.OpenProjectAsync("/one");
        h.Workspace.Draft = "retained";
        h.Api.List = (_, _, _) => throw new WorkspaceException(WorkspaceFailure.Unauthorized, "Reconnect");
        await h.Workspace.RefreshSessionsAsync();
        Assert.Null(h.Connections.Active);
        Assert.True(h.Api.Disposed);
        Assert.Empty(h.Workspace.Sessions);
        Assert.Empty(h.Workspace.History);
        await h.Connections.ConnectAsync(h.First.Id, "explicit password");
        await WaitIdleAsync(h.Workspace);
        Assert.Equal("retained", h.Workspace.Draft);
    }

    [Fact]
    public async Task LateResultCannotReplaceNewSelectionOrReconnectionToSameProfile()
    {
        using Harness h = await Harness.StartAsync();
        await h.Workspace.OpenProjectAsync("/one");
        TaskCompletionSource<WorkspacePage<HistoryMessage>> pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
        h.Api.History = (_, _, _) => pending.Task;
        Task old = h.Workspace.SelectSessionAsync("a");
        h.Connections.Disconnect();
        await h.Connections.ConnectAsync(h.First.Id, "again");
        await WaitIdleAsync(h.Workspace);
        await h.Workspace.SelectSessionAsync("b");
        pending.SetResult(new([new("old", "user", "stale")], null));
        await old;
        Assert.Equal("b", h.Workspace.Selected!.Id);
        Assert.Equal("b", Assert.Single(h.Workspace.History).Id);
    }

    [Fact]
    public async Task ReopeningRevalidatesRememberedSessionOutsideFirstPage()
    {
        using Harness h = await Harness.StartAsync();
        await h.Workspace.OpenProjectAsync("/one");
        await h.Workspace.SelectSessionAsync("b");
        h.Workspace.Draft = "older session draft";
        h.Api.List = (dir, _, _) => Task.FromResult(new WorkspacePage<WorkspaceSession>([new("a", dir, "global", "A")], "next"));
        await h.Workspace.OpenProjectAsync("/two");
        await h.Workspace.OpenProjectAsync("/one");
        Assert.Equal("b", h.Workspace.Selected!.Id);
        Assert.Equal("older session draft", h.Workspace.Draft);
        Assert.Equal("b", Assert.Single(h.Workspace.History).Id);
    }

    [Fact]
    public async Task EditingDisconnectedProfileInvalidatesRememberedWorkspace()
    {
        using Harness h = await Harness.StartAsync();
        await h.Workspace.OpenProjectAsync("/one");
        h.Workspace.Draft = "old origin";
        h.Connections.Disconnect();
        h.Connections.Save(h.First.Id, "Changed", "http://127.0.0.1:4098");
        await h.Connections.ConnectAsync(h.First.Id, "new password");
        Assert.Null(h.Workspace.Location);
        Assert.Empty(h.Workspace.OpenedDirectories);
        Assert.Empty(h.Workspace.Draft);
    }

    private static async Task WaitIdleAsync(LiveWorkspace workspace)
    {
        for (int i = 0; i < 100 && workspace.IsLoading; i++)
        {
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }

        Assert.False(workspace.IsLoading);
    }

    private static ConnectionResult Ready => new(ConnectionOutcome.Ready, "Ready", "2.1.4");
    private static WorkspaceException Failure() => new(WorkspaceFailure.Transport, "Interrupted. Retry.");

    private sealed class Harness : IDisposable
    {
        public FakeService Service { get; } = new();
        public ServerConnections Connections { get; }
        public LiveWorkspace Workspace { get; }
        public ServerProfile First { get; }
        public FakeConnection Api => Service.Created[0];
        private Harness()
        {
            Connections = new(Service);
            Workspace = new(Connections);
            First = Connections.Save(null, "First", "http://127.0.0.1:4096")!;
        }
        public static async Task<Harness> StartAsync()
        {
            Harness h = new();
            await h.Connections.ConnectAsync(h.First.Id, "secret");
            return h;
        }
        public void Dispose()
        {
            Workspace.Dispose();
            Connections.Dispose();
        }
    }

    private sealed class FakeService : IServerConnectionService, IWorkspaceConnectionFactory
    {
        public ConnectionResult Result { get; set; } = Ready;
        public List<FakeConnection> Created { get; } = [];
        public Task<ConnectionResult> ProbeAsync(ServerAddress address, string password, CancellationToken cancellationToken) => Task.FromResult(Result);
        public IWorkspaceConnection Create(ServerAddress address, string password)
        {
            FakeConnection connection = new();
            Created.Add(connection);
            return connection;
        }
    }

    private sealed class FakeConnection : IWorkspaceConnection
    {
        private string _directory = "";
        public bool Disposed { get; private set; }
        public int HistoryCalls { get; private set; }
        public int CreateCalls { get; private set; }
        public Func<string, CancellationToken, Task>? Validate { get; set; }
        public Func<string, string?, CancellationToken, Task<WorkspacePage<WorkspaceSession>>>? List { get; set; }
        public Func<string, CancellationToken, Task<WorkspaceSession>>? Metadata { get; set; }
        public Func<string, CancellationToken, Task<WorkspaceSession>>? Create { get; set; }
        public Func<string, string?, CancellationToken, Task<WorkspacePage<HistoryMessage>>>? History { get; set; }
        public Task<ProjectLocation> ResolveAsync(string directory, CancellationToken cancellationToken)
        {
            _directory = directory;
            return Task.FromResult(new ProjectLocation(directory, "global"));
        }
        public Task ValidateDirectoryAsync(string directory, CancellationToken cancellationToken) => Validate?.Invoke(directory, cancellationToken) ?? Task.CompletedTask;
        public Task<WorkspacePage<WorkspaceSession>> SessionsAsync(string directory, string? cursor, CancellationToken cancellationToken) => List?.Invoke(directory, cursor, cancellationToken)
            ?? Task.FromResult(new WorkspacePage<WorkspaceSession>([new("a", directory, "global", "A"), new("b", directory, "global", "B")], null));
        public Task<WorkspaceSession> SessionAsync(string id, CancellationToken cancellationToken) => Metadata?.Invoke(id, cancellationToken)
            ?? Task.FromResult(new WorkspaceSession(id, _directory, "global", "Title " + id));
        public Task<WorkspaceSession> CreateAsync(string directory, CancellationToken cancellationToken)
        {
            CreateCalls++;
            return Create?.Invoke(directory, cancellationToken) ?? Task.FromResult(new WorkspaceSession("created", directory, "global", "Created"));
        }
        public Task<WorkspacePage<HistoryMessage>> HistoryAsync(string id, string? cursor, CancellationToken cancellationToken)
        {
            HistoryCalls++;
            return History?.Invoke(id, cursor, cancellationToken) ?? Task.FromResult(new WorkspacePage<HistoryMessage>([new(id, "user", "history " + id)], null));
        }
        public void Dispose() => Disposed = true;
    }
}
