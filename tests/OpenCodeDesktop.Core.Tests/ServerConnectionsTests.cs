using OpenCodeDesktop.Core.Connections;

namespace OpenCodeDesktop.Core.Tests;

public sealed class ServerConnectionsTests
{
    [Theory]
    [InlineData(" http://127.0.0.1:4096/ ", "http://127.0.0.1:4096")]
    [InlineData("HTTP://127.0.0.1:00080", "http://127.0.0.1:80")]
    [InlineData("http://127.0.0.1:1", "http://127.0.0.1:1")]
    [InlineData("http://127.0.0.1:65535", "http://127.0.0.1:65535")]
    public void ValidAddressesNormalize(string input, string expected)
    {
        Assert.True(ServerAddress.TryParse(input, out ServerAddress? address));
        Assert.Equal(expected, address!.Display);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("http://127.0.0.1")]
    [InlineData("http://127.0.0.1:0")]
    [InlineData("http://127.0.0.1:65536")]
    [InlineData("http://127.0.0.1:-1")]
    [InlineData("http://127.0.0.1:80x")]
    [InlineData("http://127.0.0.1:4096/api")]
    [InlineData("http://127.0.0.1:4096/?q=1")]
    [InlineData("http://127.0.0.1:4096/#test")]
    [InlineData("http://user:password@127.0.0.1:4096")]
    [InlineData("http://localhost:4096")]
    [InlineData("http://[::1]:4096")]
    [InlineData("https://127.0.0.1:4096")]
    [InlineData("http://127.1:4096")]
    [InlineData("http://2130706433:4096")]
    [InlineData("http://127.0.0.1:4096/../")]
    [InlineData("http://127.0.0.1:4096?")]
    [InlineData("http://127.0.0.1:4096#")]
    [InlineData("http://127.0.0.1:4096\\")]
    [InlineData("http://127.0.0.1:4096\n/x")]
    public void UnsupportedAddressesAreRejected(string? input) => Assert.False(ServerAddress.TryParse(input, out _));

    [Theory]
    [InlineData("2.1.4", true)]
    [InlineData("2.1.5", false)]
    [InlineData("2.1.4-preview", false)]
    [InlineData("unknown", false)]
    [InlineData("0.0.1", false)]
    public void VersionPolicyIsExact(string version, bool supported) => Assert.Equal(supported, ServerVersionPolicy.IsSupported(version));

    [Fact]
    public void ProfilesValidateAndKeepStableIdentityWithoutNetwork()
    {
        FakeService service = new();
        using ServerConnections connections = new(service);
        Assert.Empty(connections.Profiles);
        Assert.Null(connections.Save(null, "", "http://127.0.0.1:4096"));
        Assert.Equal(ConnectionOutcome.ValidationFailed, connections.LastResult!.Outcome);
        Assert.Null(connections.Save(null, "Invalid", "http://localhost:4096"));
        ServerProfile profile = connections.Save(null, " Local ", "http://127.0.0.1:4096/")!;
        Assert.Equal("Local", profile.Name);
        Assert.Null(connections.Save(null, "Duplicate", "http://127.0.0.1:04096"));
        ServerProfile edited = connections.Save(profile.Id, "Renamed", "http://127.0.0.1:4097")!;
        Assert.Equal(profile.Id, edited.Id);
        Assert.Equal("Renamed", Assert.Single(connections.Profiles).Name);
        Assert.True(connections.Remove(profile.Id));
        Assert.Empty(connections.Profiles);
        Assert.Equal(0, service.Calls);
    }

    [Fact]
    public async Task FailedAndCancelledSwitchPreserveActiveServerAndSuccessfulSwitchPromotesTarget()
    {
        FakeService service = new();
        using ServerConnections connections = new(service);
        ServerProfile first = connections.Save(null, "Local", "http://127.0.0.1:4096")!;
        ServerProfile second = connections.Save(null, "Sandbox", "http://127.0.0.1:4097")!;
        await connections.ConnectAsync(first.Id, "secret");
        Assert.Equal(first, connections.Active!.Profile);
        Assert.False(connections.Remove(first.Id));
        Assert.Null(connections.Save(first.Id, "Changed", "http://127.0.0.1:4098"));
        service.Next = new(ConnectionOutcome.AuthenticationFailed, "Wrong password.");
        await connections.ConnectAsync(second.Id, "wrong");
        Assert.Equal(first, connections.Active.Profile);
        Assert.Equal(ConnectionOutcome.AuthenticationFailed, connections.LastResult!.Outcome);
        service.Completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Task switchTask = connections.ConnectAsync(second.Id, "secret");
        Assert.True(connections.IsConnecting);
        Assert.Equal(first, connections.Active.Profile);
        Assert.Null(connections.Save(null, "Third", "http://127.0.0.1:4098"));
        Assert.False(connections.Remove(second.Id));
        connections.Disconnect();
        Assert.NotNull(connections.Active);
        int calls = service.Calls;
        await connections.ConnectAsync(second.Id, "another");
        Assert.Equal(calls, service.Calls);
        connections.Cancel();
        // Even a service that ignores cancellation cannot promote its late success.
        service.Completion.SetResult(Ready);
        await switchTask;
        Assert.Equal(first, connections.Active.Profile);
        Assert.Equal(ConnectionOutcome.Cancelled, connections.LastResult!.Outcome);
        Assert.False(connections.IsConnecting);
        service.Completion = null;
        service.Next = Ready;
        await connections.ConnectAsync(second.Id, "secret");
        Assert.Equal(second, connections.Active.Profile);
        Assert.DoesNotContain("secret", connections.Message, StringComparison.Ordinal);
        connections.Disconnect();
        Assert.Null(connections.Active);
        Assert.True(connections.Remove(second.Id));
    }

    [Fact]
    public async Task MissingPasswordAndUnknownProfileDoNotProbeAndDisposalInvalidatesLateSuccess()
    {
        FakeService service = new();
        ServerConnections connections = new(service);
        ServerProfile profile = connections.Save(null, "Local", "http://127.0.0.1:4096")!;
        await connections.ConnectAsync(profile.Id, "");
        await connections.ConnectAsync(Guid.NewGuid(), "secret");
        Assert.Equal(0, service.Calls);
        service.Completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Task attempt = connections.ConnectAsync(profile.Id, "secret");
        connections.Dispose();
        service.Completion.SetResult(Ready);
        await attempt;
        Assert.Null(connections.Active);
        Assert.Empty(connections.Profiles);
        Assert.False(connections.IsConnecting);
        connections.Dispose();
        using ServerConnections fresh = new(service);
        Assert.Empty(fresh.Profiles);
    }

    [Fact]
    public async Task ReadyResultWithUnsupportedVersionCannotBecomeActive()
    {
        FakeService service = new() { Next = new(ConnectionOutcome.Ready, "Unexpected", "2.1.5") };
        using ServerConnections connections = new(service);
        ServerProfile profile = connections.Save(null, "Local", "http://127.0.0.1:4096")!;
        await connections.ConnectAsync(profile.Id, "secret");
        Assert.Null(connections.Active);
        Assert.Equal(ConnectionOutcome.Incompatible, connections.LastResult!.Outcome);
    }

    [Fact]
    public async Task ExternalFailureCannotLeakExceptionOrStickInConnecting()
    {
        using ServerConnections connections = new(new ThrowingService());
        ServerProfile profile = connections.Save(null, "Local", "http://127.0.0.1:4096")!;
        await connections.ConnectAsync(profile.Id, "secret");
        Assert.False(connections.IsConnecting);
        Assert.Equal(ConnectionOutcome.TransportFailure, connections.LastResult!.Outcome);
        Assert.DoesNotContain("secret", connections.Message, StringComparison.Ordinal);
    }

    private static ConnectionResult Ready => new(ConnectionOutcome.Ready, "Ready", "2.1.4");

    private sealed class FakeService : IServerConnectionService
    {
        public int Calls { get; private set; }
        public ConnectionResult Next { get; set; } = Ready;
        public TaskCompletionSource<ConnectionResult>? Completion { get; set; }
        public Task<ConnectionResult> ProbeAsync(ServerAddress address, string password, CancellationToken cancellationToken)
        {
            Calls++;
            return Completion?.Task ?? Task.FromResult(Next);
        }
    }

    private sealed class ThrowingService : IServerConnectionService
    {
        public Task<ConnectionResult> ProbeAsync(ServerAddress address, string password, CancellationToken cancellationToken)
            => throw new InvalidOperationException("secret");
    }
}
