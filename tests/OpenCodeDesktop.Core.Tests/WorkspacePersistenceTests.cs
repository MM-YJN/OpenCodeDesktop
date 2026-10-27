using OpenCodeDesktop.Core.Connections;
using OpenCodeDesktop.Core.Workspaces;

namespace OpenCodeDesktop.Core.Tests;

public sealed class WorkspacePersistenceTests
{
    [Fact]
    public async Task IndependentNotificationPreferencesAreCapturedAndRestored()
    {
        MemorySettings store = new();
        MemoryCredentials credentials = new();
        using (Harness h = new(store, credentials))
        {
            await h.Persistence.StartAsync();
            h.Workspace.SetNotificationPreferences(new(false, true));
            await h.Persistence.FlushAsync();
            Assert.Equal(new NotificationPreferences(false, true), store.Value!.Notifications);
        }
        using Harness restarted = new(store, credentials);
        await restarted.Persistence.StartAsync();
        Assert.Equal(new NotificationPreferences(false, true), restarted.Workspace.NotificationPreferences);
        restarted.Workspace.SetNotificationPreferences(new(true, false));
        await restarted.Persistence.FlushAsync();
        Assert.Equal(new NotificationPreferences(true, false), store.Value!.Notifications);
    }

    [Fact]
    public async Task LastUsedTaskSelectionIsCapturedPerProjectAndRestored()
    {
        MemorySettings store = new();
        MemoryCredentials credentials = new();
        using (Harness h = new(store, credentials))
        {
            await h.Persistence.StartAsync();
            Guid id = h.Connections.Save(null, "First", "http://127.0.0.1:4096")!.Id;
            await h.Connections.ConnectAsync(id, "secret");
            await h.Workspace.OpenProjectAsync("/one");
            h.Workspace.RememberModelPreference(new("model-a", "provider-a", "Model A"));
            h.Workspace.RememberAgentPreference("plan");
            await h.Workspace.OpenProjectAsync("/two");
            h.Workspace.RememberModelPreference(new("model-b", "provider-b", "Model B"));
            h.Workspace.RememberAgentPreference("build");
            await h.Persistence.FlushAsync();
        }
        using Harness restarted = new(store, credentials);
        await restarted.Persistence.StartAsync();
        Assert.Equal("/two", restarted.Workspace.Location!.Directory);
        Assert.Equal(new TaskModel("model-b", "provider-b", "model-b"), restarted.Workspace.PreferredModel);
        Assert.Equal("build", restarted.Workspace.PreferredAgent);
        await restarted.Workspace.OpenProjectAsync("/one");
        Assert.Equal(new TaskModel("model-a", "provider-a", "model-a"), restarted.Workspace.PreferredModel);
        Assert.Equal("plan", restarted.Workspace.PreferredAgent);
    }

    [Fact]
    public async Task TwoConnectionsRestoreIndependentProjectsSelectionsAndSavedPassword()
    {
        MemorySettings store = new();
        MemoryCredentials credentials = new();
        Guid first;
        Guid second;
        using (Harness h = new(store, credentials))
        {
            await h.Persistence.StartAsync();
            first = h.Connections.Save(null, "First", "http://127.0.0.1:4096")!.Id;
            await h.Connections.ConnectAsync(first, "private-one");
            await h.Workspace.OpenProjectAsync("/one");
            await h.Workspace.SelectSessionAsync("a");
            h.Workspace.Draft = "never persisted";
            await h.Workspace.OpenProjectAsync("/two");
            second = h.Connections.Save(null, "Second", "http://127.0.0.1:4097")!.Id;
            await h.Connections.ConnectAsync(second, "private-two");
            await h.Workspace.OpenProjectAsync("/other");
            await h.Workspace.SelectSessionAsync("b");
            await h.Persistence.FlushAsync();
        }
        using Harness restarted = new(store, credentials);
        restarted.Api.SessionList = []; // The remembered session is outside the first page.
        await restarted.Persistence.StartAsync();
        Assert.Equal(second, restarted.Connections.Active!.Profile.Id);
        Assert.Equal("private-two", restarted.Service.LastPassword);
        Assert.Equal("/other", restarted.Workspace.Location!.Directory);
        Assert.Equal("b", restarted.Workspace.Selected!.Id);
        Assert.True(restarted.Workspace.HistoryLoaded);
        Assert.Equal("server b", Assert.Single(restarted.Workspace.History).Text);
        Assert.Empty(restarted.Workspace.Draft);
        await restarted.Connections.ConnectAsync(first, "");
        await restarted.Workspace.ConnectionTransition;
        Assert.Equal(new[] { "/one", "/two" }, restarted.Workspace.Projects.Select(p => p.Location.Directory));
        Assert.Null(restarted.Workspace.Selected);
        await restarted.Workspace.OpenProjectAsync("/one");
        Assert.Equal("a", restarted.Workspace.Selected!.Id);
        Assert.Equal(0, restarted.Api.CreateCalls);
    }

    [Fact]
    public async Task ExplicitDisconnectRetainsCredentialAndListsButClearsAutomaticTarget()
    {
        MemorySettings store = new();
        MemoryCredentials credentials = new();
        using (Harness h = new(store, credentials))
        {
            await h.Persistence.StartAsync();
            Guid id = h.Connections.Save(null, "First", null)!.Id;
            await h.Connections.ConnectAsync(id, "secret");
            await h.Workspace.OpenProjectAsync("/one");
            h.Connections.Disconnect();
            await h.Persistence.FlushAsync();
        }
        Assert.Null(store.Value!.ActiveConnectionId);
        Assert.Single(credentials.Values);
        using Harness restarted = new(store, credentials);
        await restarted.Persistence.StartAsync();
        Assert.Null(restarted.Connections.Active);
        Assert.Equal(0, restarted.Service.Probes);
        await restarted.Connections.ConnectAsync(restarted.Connections.Profiles[0].Id, "");
        await restarted.Workspace.ConnectionTransition;
        Assert.Equal("/one", restarted.Workspace.Location!.Directory);
    }

    [Fact]
    public async Task OptOutDoesNotSavePasswordAndStartupRequiresExplicitAuthentication()
    {
        MemorySettings store = new();
        MemoryCredentials credentials = new();
        using (Harness h = new(store, credentials))
        {
            await h.Persistence.StartAsync();
            Guid id = h.Connections.Save(null, "First", null, false)!.Id;
            await h.Connections.ConnectAsync(id, "private");
            await h.Persistence.FlushAsync();
            Assert.Null(h.Connections.Profiles[0].CredentialReference);
        }
        Assert.Empty(credentials.Values);
        using Harness restarted = new(store, credentials);
        await restarted.Persistence.StartAsync();
        Assert.Null(restarted.Connections.Active);
        Assert.NotNull(restarted.Persistence.Error);
        Assert.Equal(0, restarted.Service.Probes);
        Assert.False(restarted.Persistence.NeedsReset);
    }

    [Theory]
    [InlineData(ConnectionOutcome.AuthenticationFailed)]
    [InlineData(ConnectionOutcome.Incompatible)]
    [InlineData(ConnectionOutcome.TransportFailure)]
    public async Task FailedStartupPreservesSelectionsAndDoesNotLoop(ConnectionOutcome outcome)
    {
        (MemorySettings store, MemoryCredentials credentials) = Seed();
        using Harness h = new(store, credentials);
        h.Service.Result = new(outcome, "Action required.");
        await h.Persistence.StartAsync();
        Assert.Null(h.Connections.Active);
        Assert.Single(h.Connections.Profiles);
        Assert.Equal("a", store.Value!.Connections[0].Projects[0].SessionId);
        Assert.Equal(1, h.Service.Probes);
        Assert.True(h.Persistence.CanRetry);
        Assert.False(h.Persistence.NeedsReset);
    }

    [Fact]
    public async Task MissingSessionReturnsToListKeepsRememberedIdAndCanRetry()
    {
        (MemorySettings store, MemoryCredentials credentials) = Seed();
        using Harness h = new(store, credentials);
        h.Api.Metadata = (_, _) => throw new WorkspaceException(WorkspaceFailure.NotFound, "Missing.");
        await h.Persistence.StartAsync();
        await h.Persistence.FlushAsync();
        Assert.Equal("/one", h.Workspace.Location!.Directory);
        Assert.Null(h.Workspace.Selected);
        Assert.False(h.Workspace.HistoryLoaded);
        Assert.Contains("no longer exists", h.Workspace.Error);
        Assert.Equal("a", store.Value!.Connections[0].Projects[0].SessionId);
        h.Api.Metadata = null;
        await h.Workspace.RetryAsync();
        Assert.Equal("a", h.Workspace.Selected!.Id);
    }

    [Fact]
    public async Task MissingDirectoryAndWrongSessionOwnershipDoNotChangeSavedNavigation()
    {
        (MemorySettings store, MemoryCredentials credentials) = Seed();
        using Harness h = new(store, credentials);
        h.Api.Validate = _ => throw new WorkspaceException(WorkspaceFailure.NotFound, "Directory missing.");
        await h.Persistence.StartAsync();
        Assert.Null(h.Workspace.Location);
        Assert.NotNull(Assert.Single(h.Workspace.Projects).Error);
        h.Api.Validate = null;
        h.Api.Metadata = (id, _) => Task.FromResult(new WorkspaceSession(id, "/wrong", "global", "Wrong"));
        await h.Workspace.RetryAsync();
        Assert.Null(h.Workspace.Location);
        Assert.Equal(0, h.Api.HistoryCalls);
        Assert.Equal("/one", store.Value!.Connections[0].SelectedDirectory);
    }

    [Fact]
    public async Task NavigationDuringHeldStartupCannotOverwriteSelectionOrPersistFailedTarget()
    {
        (MemorySettings store, MemoryCredentials credentials) = Seed();
        using Harness h = new(store, credentials);
        TaskCompletionSource<WorkspaceSession> held = new(TaskCreationOptions.RunContinuationsAsynchronously);
        h.Api.Metadata = (_, _) => held.Task;
        Task startup = h.Persistence.StartAsync();
        Assert.True(h.Workspace.IsLoading);
        h.Api.Metadata = null;
        await h.Workspace.OpenProjectAsync("/two");
        held.SetResult(new("a", "/one", "global", "Old"));
        await startup;
        await h.Persistence.FlushAsync();
        Assert.Equal("/two", h.Workspace.Location!.Directory);
        Assert.Equal("/two", store.Value!.Connections[0].SelectedDirectory);
        Assert.Null(h.Workspace.Selected);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DisconnectOrShutdownDuringHeldProbeRejectsLateSuccess(bool shutdown)
    {
        (MemorySettings store, MemoryCredentials credentials) = Seed();
        using Harness h = new(store, credentials);
        TaskCompletionSource<ConnectionResult> held = new(TaskCreationOptions.RunContinuationsAsynchronously);
        h.Service.Probe = _ => held.Task;
        Task startup = h.Persistence.StartAsync();
        if (shutdown)
        {
            h.Dispose();
        }
        else
        {
            h.Connections.Disconnect();
        }

        held.SetResult(Ready);
        await startup;
        await h.Persistence.FlushAsync();
        Assert.Null(h.Connections.Active);
        if (!shutdown)
        {
            Assert.Null(store.Value!.ActiveConnectionId);
        }
    }

    [Fact]
    public async Task FailedWritesKeepOldCredentialUntilReplacementIsCommittedAndRemovalCleansUp()
    {
        (MemorySettings store, MemoryCredentials credentials) = Seed();
        string original = store.Value!.Connections[0].CredentialReference!;
        using Harness h = new(store, credentials);
        store.FailSave = true;
        await h.Persistence.StartAsync();
        await h.Persistence.FlushAsync();
        Assert.True(credentials.Values.ContainsKey(original));
        Assert.Equal(original, store.Value.Connections[0].CredentialReference);
        Assert.NotNull(h.Persistence.Error);
        store.FailSave = false;
        h.Persistence.RetrySave();
        await h.Persistence.FlushAsync();
        Assert.False(credentials.Values.ContainsKey(original));
        Guid id = h.Connections.Profiles[0].Id;
        h.Connections.Disconnect();
        h.Connections.Remove(id);
        await h.Persistence.FlushAsync();
        Assert.Empty(credentials.Values);
        Assert.Empty(store.Value.Connections);
    }

    [Fact]
    public async Task CorruptSettingsArePreservedUntilExplicitResetAndExceptionsNeverLeak()
    {
        MemorySettings store = new() { FailLoad = true };
        using Harness h = new(store, new());
        await h.Persistence.StartAsync();
        Assert.True(h.Persistence.NeedsReset);
        h.Connections.Save(null, "In memory", null);
        await h.Persistence.FlushAsync();
        Assert.Equal(0, store.Saves);
        Assert.DoesNotContain("sensitive", h.Persistence.Error);
        await h.Persistence.ResetAsync();
        Assert.Equal(1, store.Resets);
        Assert.Single(store.Value!.Connections);
        Assert.False(h.Persistence.NeedsReset);
    }

    [Fact]
    public async Task EndpointEditInvalidatesProjectsAndCredentialWhileRenamePreservesThem()
    {
        (MemorySettings store, MemoryCredentials credentials) = Seed();
        using Harness h = new(store, credentials);
        await h.Persistence.StartAsync();
        h.Connections.Disconnect();
        Guid id = h.Connections.Profiles[0].Id;
        h.Connections.Save(id, "Renamed", null);
        await h.Persistence.FlushAsync();
        Assert.Single(store.Value!.Connections[0].Projects);
        Assert.NotNull(store.Value.Connections[0].CredentialReference);
        h.Connections.Save(id, "Changed", "http://127.0.0.1:4097");
        await h.Persistence.FlushAsync();
        Assert.Empty(store.Value.Connections[0].Projects);
        Assert.Null(store.Value.Connections[0].CredentialReference);
        Assert.Empty(credentials.Values);
    }

    [Fact]
    public async Task CredentialFailuresKeepProfilesAndDoNotExposeExceptions()
    {
        (MemorySettings store, MemoryCredentials credentials) = Seed();
        credentials.FailRead = true;
        using Harness h = new(store, credentials);
        await h.Persistence.StartAsync();
        Assert.Null(h.Connections.Active);
        Assert.DoesNotContain("sensitive", h.Persistence.Error);
        credentials.FailRead = false;
        credentials.FailSave = true;
        await h.Connections.ConnectAsync(h.Connections.Profiles[0].Id, "replacement");
        Assert.NotNull(h.Connections.Active);
        Assert.NotNull(h.Connections.CredentialError);
        Assert.DoesNotContain("sensitive", h.Connections.CredentialError);
    }

    [Fact]
    public async Task HeldCredentialProtectionAfterDisconnectCannotPromoteOrLeaveAnEntry()
    {
        (MemorySettings store, MemoryCredentials credentials) = Seed();
        string reference = Guid.NewGuid().ToString("N");
        TaskCompletionSource<string> held = new(TaskCreationOptions.RunContinuationsAsynchronously);
        credentials.SaveOperation = _ => held.Task;
        using Harness h = new(store, credentials);
        Task startup = h.Persistence.StartAsync();
        Assert.True(h.Connections.IsConnecting);
        h.Connections.Disconnect();
        held.SetResult(reference);
        await startup;
        Assert.Null(h.Connections.Active);
        Assert.False(h.Connections.IsConnecting);
        Assert.Contains(reference, credentials.Deleted);
    }

    [Fact]
    public async Task SaveQueueKeepsLatestSnapshotAndOnlyConfirmedSessionNavigation()
    {
        MemorySettings store = new();
        using Harness h = new(store, new());
        await h.Persistence.StartAsync();
        TaskCompletionSource barrier = new(TaskCreationOptions.RunContinuationsAsynchronously);
        store.SaveBarrier = barrier.Task;
        Guid id = h.Connections.Save(null, "First", null)!.Id;
        h.Connections.Save(id, "Latest", null);
        Task flush = h.Persistence.FlushAsync();
        Assert.False(flush.IsCompleted);
        Assert.Equal(1, store.StartedSaves);
        barrier.SetResult();
        await flush;
        Assert.Equal(2, store.Saves);
        Assert.Equal("Latest", store.Value!.Connections[0].Name);
        store.SaveBarrier = Task.CompletedTask;
        await h.Connections.ConnectAsync(id, "secret");
        await h.Workspace.OpenProjectAsync("/one");
        await h.Workspace.SelectSessionAsync("a");
        h.Api.Metadata = (_, _) => throw new WorkspaceException(WorkspaceFailure.Transport, "Failed selection");
        await h.Workspace.SelectSessionAsync("b");
        h.Connections.Save(null, "Another", "http://127.0.0.1:4097");
        await h.Persistence.FlushAsync();
        Assert.Equal("a", store.Value.Connections[0].Projects[0].SessionId);
    }

    [Fact]
    public async Task NavigationBeforeSettingsLoadPreservesExistingFileUntilExplicitReset()
    {
        (MemorySettings store, MemoryCredentials credentials) = Seed();
        TaskCompletionSource<WorkspaceSettings?> held = new(TaskCreationOptions.RunContinuationsAsynchronously);
        store.LoadOperation = _ => held.Task;
        using Harness h = new(store, credentials);
        Task startup = h.Persistence.StartAsync();
        h.Connections.Save(null, "Manual choice", "http://127.0.0.1:4097");
        held.SetResult(store.Value);
        await startup;
        Assert.True(h.Persistence.NeedsReset);
        Assert.Equal("Manual choice", Assert.Single(h.Connections.Profiles).Name);
        Assert.Equal("First", store.Value!.Connections[0].Name);
        Assert.Equal(0, store.Saves);
    }

    [Fact]
    public async Task RemovingRememberedProjectDuringStartupClearsSavedSelectionBeforeLateResponse()
    {
        (MemorySettings store, MemoryCredentials credentials) = Seed();
        using Harness h = new(store, credentials);
        TaskCompletionSource<WorkspaceSession> held = new(TaskCreationOptions.RunContinuationsAsynchronously);
        h.Api.Metadata = (_, _) => held.Task;
        Task startup = h.Persistence.StartAsync();
        Assert.True(h.Workspace.IsLoading);
        h.Workspace.RemoveProject("/one");
        Assert.False(h.Persistence.IsRestoring);
        held.SetResult(new("a", "/one", "global", "Late"));
        await startup;
        await h.Persistence.FlushAsync();
        Assert.Empty(h.Workspace.Projects);
        Assert.Null(h.Workspace.Location);
        Assert.Empty(store.Value!.Connections[0].Projects);
        Assert.Null(store.Value.Connections[0].SelectedDirectory);
    }

    private static ConnectionResult Ready => new(ConnectionOutcome.Ready, "Ready", "2.1.4");
    private static (MemorySettings, MemoryCredentials) Seed()
    {
        MemoryCredentials credentials = new();
        string reference = Guid.NewGuid().ToString("N");
        credentials.Values.Add(reference, "secret");
        var id = Guid.NewGuid();
        return (new() { Value = new(1, id, [new(id, "First", ServerAddress.DefaultOrigin, true, reference, "/one", [new("/one", "global", "a")])]) }, credentials);
    }

    private sealed class Harness : IDisposable
    {
        public Api Api { get; } = new();
        public Service Service { get; }
        public ServerConnections Connections { get; }
        public LiveWorkspace Workspace { get; }
        public WorkspacePersistence Persistence { get; }
        public Harness(MemorySettings store, MemoryCredentials credentials)
        {
            Service = new(Api);
            Connections = new(Service, credentials);
            Workspace = new(Connections);
            Persistence = new(Connections, Workspace, store, credentials);
        }
        public void Dispose()
        {
            Persistence.Dispose();
            Workspace.Dispose();
            Connections.Dispose();
        }
    }

    private sealed class MemorySettings : IWorkspaceSettingsStore
    {
        public WorkspaceSettings? Value { get; set; }
        public bool FailLoad { get; set; }
        public bool FailSave { get; set; }
        public int Saves { get; private set; }
        public int Resets { get; private set; }
        public int StartedSaves { get; private set; }
        public Task SaveBarrier { get; set; } = Task.CompletedTask;
        public Func<CancellationToken, Task<WorkspaceSettings?>>? LoadOperation { get; set; }
        public Task<WorkspaceSettings?> LoadAsync(CancellationToken cancellationToken) => LoadOperation?.Invoke(cancellationToken) ?? (FailLoad ? throw new IOException("sensitive") : Task.FromResult(Value));
        public async Task SaveAsync(WorkspaceSettings settings, CancellationToken cancellationToken)
        {
            StartedSaves++;
            await SaveBarrier;
            if (FailSave)
            {
                throw new IOException("sensitive");
            }

            Saves++;
            Value = settings;
        }
        public Task ResetAsync(CancellationToken cancellationToken)
        {
            Resets++;
            FailLoad = false;
            return Task.CompletedTask;
        }
    }

    private sealed class MemoryCredentials : ICredentialStore
    {
        public Dictionary<string, string> Values { get; } = [];
        public bool FailRead { get; set; }
        public bool FailSave { get; set; }
        public Func<CancellationToken, Task<string>>? SaveOperation { get; set; }
        public List<string> Deleted { get; } = [];
        public Task<string> SaveAsync(string password, CancellationToken cancellationToken)
        {
            if (SaveOperation is not null)
            {
                return SaveOperation(cancellationToken);
            }

            if (FailSave)
            {
                throw new IOException("sensitive");
            }

            string reference = Guid.NewGuid().ToString("N");
            Values.Add(reference, password);
            return Task.FromResult(reference);
        }
        public Task<string> ReadAsync(string reference, CancellationToken cancellationToken) => FailRead ? throw new IOException("sensitive") : Task.FromResult(Values[reference]);
        public Task DeleteAsync(string reference, CancellationToken cancellationToken)
        {
            Deleted.Add(reference);
            Values.Remove(reference);
            return Task.CompletedTask;
        }
    }

    private sealed class Service(Api api) : IServerConnectionService, IWorkspaceConnectionFactory
    {
        public int Probes { get; private set; }
        public string? LastPassword { get; private set; }
        public ConnectionResult Result { get; set; } = Ready;
        public Func<CancellationToken, Task<ConnectionResult>>? Probe { get; set; }
        public Task<ConnectionResult> ProbeAsync(ServerAddress address, string password, CancellationToken cancellationToken)
        {
            Probes++;
            LastPassword = password;
            return Probe?.Invoke(cancellationToken) ?? Task.FromResult(Result);
        }
        public IWorkspaceConnection Create(ServerAddress address, string password) => api;
    }

    private sealed class Api : IWorkspaceConnection
    {
        private string _directory = "";
        public string[] SessionList { get; set; } = ["a", "b"];
        public Func<string, CancellationToken, Task<WorkspaceSession>>? Metadata { get; set; }
        public Action<string>? Validate { get; set; }
        public int CreateCalls { get; private set; }
        public int HistoryCalls { get; private set; }
        public Task<ProjectLocation> ResolveAsync(string directory, CancellationToken cancellationToken)
        {
            _directory = directory;
            return Task.FromResult(new ProjectLocation(directory, "global"));
        }
        public Task ValidateDirectoryAsync(string directory, CancellationToken cancellationToken)
        {
            Validate?.Invoke(directory);
            return Task.CompletedTask;
        }
        public Task<WorkspacePage<WorkspaceSession>> SessionsAsync(string directory, string? cursor, CancellationToken cancellationToken) => Task.FromResult(new WorkspacePage<WorkspaceSession>(SessionList.Select(id => new WorkspaceSession(id, directory, "global", id)).ToArray(), null));
        public Task<WorkspaceSession> SessionAsync(string id, CancellationToken cancellationToken) => Metadata?.Invoke(id, cancellationToken) ?? Task.FromResult(new WorkspaceSession(id, _directory, "global", id));
        public Task<WorkspacePage<HistoryMessage>> HistoryAsync(string id, string? cursor, CancellationToken cancellationToken)
        {
            HistoryCalls++;
            return Task.FromResult(new WorkspacePage<HistoryMessage>([new(id, "user", "server " + id)], null));
        }
        public Task<WorkspaceSession> CreateAsync(string directory, CancellationToken cancellationToken)
        {
            CreateCalls++;
            throw new InvalidOperationException("Restoration must never create sessions.");
        }
        public void Dispose() { }
    }
}
