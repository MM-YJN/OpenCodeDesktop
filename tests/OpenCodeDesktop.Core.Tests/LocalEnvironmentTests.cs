using OpenCodeDesktop.Core.Connections;

namespace OpenCodeDesktop.Core.Tests;

public sealed class LocalEnvironmentTests
{
    [Fact]
    public async Task DetectionDoesNotAddOrConnectAndExplicitActionReusesProfileWithoutCredentialState()
    {
        Service service = new();
        Discovery discovery = new();
        using ServerConnections connections = new(service);
        using LocalEnvironment local = new(connections, discovery);
        TaskCompletionSource<LocalServerInfo?> pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
        discovery.Pending = pending;
        Task detect = local.DetectAsync();
        Assert.True(local.IsBusy);
        Assert.False(local.CanAdd);
        Assert.Empty(connections.Profiles);
        pending.SetResult(Info());
        await detect;
        Assert.Empty(connections.Profiles);
        Assert.Null(connections.Active);
        Assert.True(local.CanAdd);
        Assert.DoesNotContain("test-secret", local.Detected!.ToString(), StringComparison.Ordinal);
        ServerProfile existing = connections.Save(null, "Existing local", ServerAddress.DefaultOrigin)!;
        await local.AddAsync();
        Assert.Equal(existing.Id, connections.Active!.Profile.Id);
        Assert.Single(connections.Profiles);
        Assert.Equal(1, discovery.PasswordCalls);
        Assert.Equal(1, service.Calls);
        Assert.True(local.IsAlreadyConnected);
        Assert.False(local.CanAdd);
        await local.AddAsync();
        Assert.Equal(1, service.Calls);
        Assert.DoesNotContain("test-secret", local.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnavailableOrChangedEnvironmentNeverCreatesProfileOrReplacesActiveServer()
    {
        Service service = new();
        Discovery discovery = new();
        using ServerConnections connections = new(service);
        ServerProfile old = connections.Save(null, "Old", "http://127.0.0.1:4097")!;
        await connections.ConnectAsync(old.Id, "old-secret");
        using LocalEnvironment local = new(connections, discovery);
        discovery.Info = null;
        await local.DetectAsync();
        Assert.False(local.CanAdd);
        discovery.Info = Info();
        await local.DetectAsync();
        discovery.Password = null;
        await local.AddAsync();
        Assert.Equal(old, connections.Active!.Profile);
        Assert.Single(connections.Profiles);
        Assert.Null(local.Detected);
        Assert.Contains("changed", local.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NavigationInvalidatesLateDetectionAndLateCredentialRetrieval()
    {
        Service service = new();
        Discovery discovery = new() { Pending = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        using ServerConnections connections = new(service);
        using LocalEnvironment local = new(connections, discovery);
        Task detect = local.DetectAsync();
        local.Deactivate();
        discovery.Pending.SetResult(Info());
        await detect;
        Assert.Null(local.Detected);
        Assert.False(local.IsBusy);
        discovery.Pending = null;
        await local.DetectAsync();
        discovery.PasswordPending = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Task add = local.AddAsync();
        await local.AddAsync();
        Assert.Equal(1, discovery.PasswordCalls);
        local.Deactivate();
        discovery.PasswordPending.SetResult("test-secret");
        await add;
        Assert.Empty(connections.Profiles);
        Assert.Equal(0, service.Calls);
        Assert.Null(local.Detected);
    }

    [Fact]
    public async Task CancelledAndFailedSwitchKeepPreviousActiveServerAndRetryDoesNotDuplicateProfiles()
    {
        Service service = new();
        Discovery discovery = new();
        using ServerConnections connections = new(service);
        ServerProfile old = connections.Save(null, "Old", "http://127.0.0.1:4097")!;
        await connections.ConnectAsync(old.Id, "old-secret");
        using LocalEnvironment local = new(connections, discovery);
        await local.DetectAsync();
        service.Result = new(ConnectionOutcome.AuthenticationFailed, "Rejected");
        await local.AddAsync();
        Assert.Equal(old, connections.Active!.Profile);
        Assert.Equal(2, connections.Profiles.Count);
        service.Pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Task add = local.AddAsync();
        local.Deactivate();
        service.Pending.SetResult(Ready);
        await add;
        Assert.Equal(old, connections.Active.Profile);
        Assert.Equal(2, connections.Profiles.Count);
        Assert.False(local.IsBusy);
    }

    [Fact]
    public async Task SafeFailureAndShutdownReleaseStateAndIgnoreLateCompletion()
    {
        Discovery discovery = new() { Throw = true };
        using ServerConnections connections = new(new Service());
        LocalEnvironment local = new(connections, discovery);
        await local.DetectAsync();
        Assert.DoesNotContain("test-secret", local.Message, StringComparison.Ordinal);
        Assert.False(local.IsBusy);
        discovery.Throw = false;
        discovery.Pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Task detect = local.DetectAsync();
        local.Dispose();
        discovery.Pending.SetResult(Info());
        await detect;
        Assert.Null(local.Detected);
        Assert.False(local.CanAdd);
        Assert.Empty(connections.Profiles);
    }

    private static LocalServerInfo Info()
    {
        Assert.True(ServerAddress.TryParse(ServerAddress.DefaultOrigin, out ServerAddress? address));
        return new(address!, "2.1.4");
    }
    private static ConnectionResult Ready => new(ConnectionOutcome.Ready, "Ready", "2.1.4");
    private sealed class Discovery : ILocalServerDiscovery
    {
        public LocalServerInfo? Info { get; set; } = LocalEnvironmentTests.Info();
        public string? Password { get; set; } = "test-secret";
        public bool Throw { get; set; }
        public int PasswordCalls { get; private set; }
        public TaskCompletionSource<LocalServerInfo?>? Pending { get; set; }
        public TaskCompletionSource<string?>? PasswordPending { get; set; }
        public Task<LocalServerInfo?> DiscoverAsync(CancellationToken cancellationToken) => Throw ? throw new IOException("test-secret") : Pending?.Task ?? Task.FromResult(Info);
        public Task<string?> GetPasswordAsync(ServerAddress expectedAddress, CancellationToken cancellationToken)
        {
            PasswordCalls++;
            return PasswordPending?.Task ?? Task.FromResult(Password);
        }
    }
    private sealed class Service : IServerConnectionService
    {
        public int Calls { get; private set; }
        public ConnectionResult Result { get; set; } = Ready;
        public TaskCompletionSource<ConnectionResult>? Pending { get; set; }
        public Task<ConnectionResult> ProbeAsync(ServerAddress address, string password, CancellationToken cancellationToken)
        {
            Calls++;
            return Pending?.Task ?? Task.FromResult(Result);
        }
    }
}
