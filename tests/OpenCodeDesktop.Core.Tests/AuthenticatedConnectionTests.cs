using OpenCodeDesktop.Core.Connections;
using OpenCodeDesktop.Core.Workspaces;

namespace OpenCodeDesktop.Core.Tests;

public sealed class AuthenticatedConnectionTests
{
    [Fact]
    public async Task CancelledSwitchRetainsContextAndReconnectCreatesNewGeneration()
    {
        Service service = new();
        using ServerConnections connections = new(service);
        ServerProfile one = connections.Save(null, "One", "http://127.0.0.1:4096")!;
        ServerProfile two = connections.Save(null, "Two", "http://127.0.0.1:4097")!;
        await connections.ConnectAsync(one.Id, "first password");
        IWorkspaceConnection original = connections.Context!;
        long generation = connections.Generation;
        service.Pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Task attempt = connections.ConnectAsync(two.Id, "candidate password");
        connections.Cancel();
        service.Pending.SetResult(new(ConnectionOutcome.Ready, "Ready", "2.1.4"));
        await attempt;
        Assert.Same(original, connections.Context);
        Assert.Equal(generation, connections.Generation);
        Assert.Equal(1, service.Created);
        service.Pending = null;
        connections.Disconnect();
        Assert.True(service.Api!.Disposed);
        Assert.Null(connections.Context);
        await connections.ConnectAsync(one.Id, "reentered password");
        Assert.NotSame(original, connections.Context);
        Assert.True(connections.Generation > generation);
        connections.RejectAuthorization(generation);
        Assert.NotNull(connections.Context);
        connections.Dispose();
        Assert.True(service.Api!.Disposed);
    }

    private sealed class Service : IServerConnectionService, IWorkspaceConnectionFactory
    {
        public TaskCompletionSource<ConnectionResult>? Pending { get; set; }
        public int Created { get; private set; }
        public Api? Api { get; private set; }
        public Task<ConnectionResult> ProbeAsync(ServerAddress address, string password, CancellationToken cancellationToken)
            => Pending?.Task ?? Task.FromResult(new ConnectionResult(ConnectionOutcome.Ready, "Ready", "2.1.4"));
        public IWorkspaceConnection Create(ServerAddress address, string password)
        {
            Created++;
            return Api = new();
        }
    }
    private sealed class Api : IWorkspaceConnection
    {
        public bool Disposed { get; private set; }
        public void Dispose() => Disposed = true;
        public Task<ProjectLocation> ResolveAsync(string directory, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task ValidateDirectoryAsync(string directory, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<WorkspacePage<WorkspaceSession>> SessionsAsync(string directory, string? cursor, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<WorkspaceSession> CreateAsync(string directory, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<WorkspaceSession> SessionAsync(string id, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<WorkspacePage<HistoryMessage>> HistoryAsync(string id, string? cursor, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
