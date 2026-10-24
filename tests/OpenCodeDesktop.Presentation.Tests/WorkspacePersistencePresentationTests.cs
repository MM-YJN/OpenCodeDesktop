using OpenCodeDesktop.Core.Connections;
using OpenCodeDesktop.Core.Workspaces;
using OpenCodeDesktop.Presentation;

namespace OpenCodeDesktop.Presentation.Tests;

public sealed class WorkspacePersistencePresentationTests
{
    [Fact]
    public async Task ProgressEmptyDisconnectedAndUnreadableStorageHaveDistinctActions()
    {
        Store store = new();
        using ServerConnections connections = new(new Service());
        using LiveWorkspace workspace = new(connections);
        using WorkspacePersistence persistence = new(connections, workspace, store, new Credentials());
        Task start = persistence.StartAsync();
        var loading = WorkspacePersistencePresentation.From(persistence);
        Assert.True(loading.IsRestoring);
        Assert.Contains("Loading", loading.Message);
        Assert.False(loading.CanReset);
        store.Load.SetException(new IOException("private exception"));
        await start;
        var failed = WorkspacePersistencePresentation.From(persistence);
        Assert.True(failed.IsError);
        Assert.True(failed.CanReset);
        Assert.False(failed.CanSave);
        Assert.False(failed.CanRetry);
        Assert.DoesNotContain("private", failed.Message);
        await persistence.ResetAsync();
        Assert.Null(WorkspacePersistencePresentation.From(persistence).Message);
        Assert.Null(connections.Active);
    }

    [Fact]
    public async Task UnavailableCredentialsOfferReconnectAndExplicitRetryWithoutResettingProfiles()
    {
        Store store = new();
        var id = Guid.NewGuid();
        store.Load.SetResult(new(1, id, [new(id, "Saved", ServerAddress.DefaultOrigin, true, null, null, [])]));
        using ServerConnections connections = new(new Service());
        using LiveWorkspace workspace = new(connections);
        using WorkspacePersistence persistence = new(connections, workspace, store, new Credentials());
        await persistence.StartAsync();
        var state = WorkspacePersistencePresentation.From(persistence);
        Assert.True(state.IsError);
        Assert.True(state.CanRetry);
        Assert.False(state.CanReset);
        Assert.Contains("password", state.Message);
        Assert.Single(connections.Profiles);
    }

    private sealed class Store : IWorkspaceSettingsStore
    {
        public TaskCompletionSource<WorkspaceSettings?> Load { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<WorkspaceSettings?> LoadAsync(CancellationToken cancellationToken) => Load.Task;
        public Task SaveAsync(WorkspaceSettings settings, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task ResetAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
    private sealed class Credentials : ICredentialStore
    {
        public Task<string> ReadAsync(string reference, CancellationToken cancellationToken) => throw new IOException();
        public Task<string> SaveAsync(string password, CancellationToken cancellationToken) => throw new IOException();
        public Task DeleteAsync(string reference, CancellationToken cancellationToken) => Task.CompletedTask;
    }
    private sealed class Service : IServerConnectionService
    {
        public Task<ConnectionResult> ProbeAsync(ServerAddress address, string password, CancellationToken cancellationToken) => throw new InvalidOperationException("No password available.");
    }
}
