using OpenCodeDesktop.Core.Connections;
using OpenCodeDesktop.Core.Workspaces;
using OpenCodeDesktop.Presentation;

namespace OpenCodeDesktop.Presentation.Tests;

public sealed class LiveWorkspacePresentationTests
{
    [Fact]
    public async Task LiveStatesShowOwnershipLoadingFailureEmptyHistoryAndDisconnectedWithoutSampleContent()
    {
        Service service = new();
        using ServerConnections connections = new(service);
        using LiveWorkspace workspace = new(connections);
        var disconnected = LiveWorkspacePresentation.From(workspace);
        Assert.Contains("Settings > Connections", disconnected.ConversationNotice, StringComparison.Ordinal);
        Assert.Equal("Disconnected · No server connected", disconnected.Path);
        Assert.Empty(workspace.Sessions);
        Assert.Empty(workspace.History);
        ServerProfile profile = connections.Save(null, "Test server", "http://127.0.0.1:4096")!;
        await connections.ConnectAsync(profile.Id, "secret");
        var start = LiveWorkspacePresentation.From(workspace);
        Assert.Contains("Open a server-side directory", start.ConversationNotice, StringComparison.Ordinal);
        Assert.Contains("Test server", start.Path, StringComparison.Ordinal);
        await workspace.OpenProjectAsync("/real");
        Task loading = workspace.SelectSessionAsync("a");
        Assert.Contains("Loading", LiveWorkspacePresentation.From(workspace).ConversationNotice, StringComparison.Ordinal);
        Assert.DoesNotContain("Sample", LiveWorkspacePresentation.From(workspace).ToString(), StringComparison.OrdinalIgnoreCase);
        service.Api.Pending.SetException(new WorkspaceException(WorkspaceFailure.Transport, "Retry"));
        await loading.ConfigureAwait(true);
        Assert.Contains("unavailable", LiveWorkspacePresentation.From(workspace).ConversationNotice, StringComparison.Ordinal);
        Assert.True(workspace.CanRetry);
        service.Api.Pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
        service.Api.Pending.SetResult(new([], null));
        await workspace.RetryAsync();
        Assert.Contains("no recorded messages", LiveWorkspacePresentation.From(workspace).ConversationNotice, StringComparison.Ordinal);
        Assert.Contains("/real", LiveWorkspacePresentation.From(workspace).Path, StringComparison.Ordinal);
        Assert.Contains("unavailable", LiveWorkspacePresentation.UnavailableDetails, StringComparison.Ordinal);
        connections.Disconnect();
        Assert.Contains("Disconnected", LiveWorkspacePresentation.From(workspace).ConversationNotice, StringComparison.Ordinal);
        Assert.True(workspace.HasConnected);
    }

    private sealed class Service : IServerConnectionService, IWorkspaceConnectionFactory
    {
        public Api Api { get; } = new();
        public IWorkspaceConnection Create(ServerAddress address, string password) => Api;
        public Task<ConnectionResult> ProbeAsync(ServerAddress address, string password, CancellationToken cancellationToken) => Task.FromResult(new ConnectionResult(ConnectionOutcome.Ready, "Ready", "2.1.4"));
    }
    private sealed class Api : IWorkspaceConnection
    {
        public TaskCompletionSource<WorkspacePage<HistoryMessage>> Pending { get; set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<ProjectLocation> ResolveAsync(string directory, CancellationToken cancellationToken) => Task.FromResult(new ProjectLocation(directory, "global"));
        public Task ValidateDirectoryAsync(string directory, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<WorkspacePage<WorkspaceSession>> SessionsAsync(string directory, string? cursor, CancellationToken cancellationToken) => Task.FromResult(new WorkspacePage<WorkspaceSession>([new("a", directory, "global", "Real session")], null));
        public Task<WorkspaceSession> CreateAsync(string directory, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<WorkspaceSession> SessionAsync(string id, CancellationToken cancellationToken) => Task.FromResult(new WorkspaceSession(id, "/real", "global", "Real session"));
        public Task<WorkspacePage<HistoryMessage>> HistoryAsync(string id, string? cursor, CancellationToken cancellationToken) => Pending.Task;
        public void Dispose() { }
    }
}
