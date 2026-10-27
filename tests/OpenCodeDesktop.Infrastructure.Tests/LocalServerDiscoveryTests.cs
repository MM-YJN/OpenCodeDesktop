using OpenCodeDesktop.Core.Connections;
using OpenCodeDesktop.Infrastructure.Connections;

namespace OpenCodeDesktop.Infrastructure.Tests;

public sealed class LocalServerDiscoveryTests
{
    [Fact]
    public async Task DiscoveryAndPasswordRefreshVerifyVersionOriginAndAuthorizationUsingOnlyReadOnlyCommands()
    {
        Commands commands = new();
        Probe probe = new();
        LocalServerDiscovery discovery = new(probe, commands);
        LocalServerInfo? info = await discovery.DiscoverAsync(TestContext.Current.CancellationToken);
        Assert.NotNull(info);
        Assert.Equal(ServerAddress.DefaultOrigin, info.Address.Display);
        Assert.Equal("2.1.4", info.Version);
        Assert.DoesNotContain("private-password", info.ToString(), StringComparison.Ordinal);
        Assert.Equal(new[] { "--version", "service status", "service get password" }, commands.Calls);
        Assert.Equal("private-password", await discovery.GetPasswordAsync(info.Address, TestContext.Current.CancellationToken));
        Assert.Equal(2, probe.Calls);
        commands.Status = "http://127.0.0.1:4097\n";
        Assert.Null(await discovery.GetPasswordAsync(info.Address, TestContext.Current.CancellationToken));
        Assert.Equal(2, probe.Calls);
    }

    [Theory]
    [InlineData("opencode v1.2.3", "http://127.0.0.1:28894", "private-password")]
    [InlineData("opencode v3.0.0", "http://127.0.0.1:28894", "private-password")]
    [InlineData("opencode v2.1.4", "http://evil.invalid:28894", "private-password")]
    [InlineData("opencode v2.1.4", "http://127.0.0.1:28894/path", "private-password")]
    [InlineData("opencode v2.1.4", "not running", "private-password")]
    [InlineData("opencode v2.1.4", "http://127.0.0.1:28894", "")]
    [InlineData("opencode v2.1.4", "http://127.0.0.1:28894", null)]
    public async Task UnsupportedStoppedOrUnusableServicesNeverBecomeCandidates(string version, string status, string? password)
    {
        Probe probe = new();
        Commands commands = new() { Version = version, Status = status, Password = password };
        Assert.Null(await new LocalServerDiscovery(probe, commands).DiscoverAsync(TestContext.Current.CancellationToken));
        Assert.Equal(0, probe.Calls);
    }

    [Fact]
    public async Task RejectedCredentialDeadlineCancellationAndBrokenCliRemainSafe()
    {
        Commands commands = new();
        Probe probe = new() { Accept = false };
        Assert.Null(await new LocalServerDiscovery(probe, commands).DiscoverAsync(TestContext.Current.CancellationToken));
        commands.Throw = true;
        Assert.Null(await new LocalServerDiscovery(probe, commands).DiscoverAsync(TestContext.Current.CancellationToken));
        commands.Throw = false;
        commands.Stall = true;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new LocalServerDiscovery(probe, commands, TimeSpan.FromMilliseconds(50)).DiscoverAsync(TestContext.Current.CancellationToken));
        using CancellationTokenSource cancel = new();
        await cancel.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new LocalServerDiscovery(probe, commands).DiscoverAsync(cancel.Token));
    }

    [Fact]
    public async Task InstalledEnvironmentDiscoveryIsReadOnlyAndContainsNoCredentialMetadata()
    {
        Assert.SkipUnless(Environment.GetEnvironmentVariable("OPENCODE_LOCAL_DISCOVERY_TEST") == "1", "Opt-in: read-only discovery of this PC's installed local service.");
        using HttpClient client = OpenCodeConnectionService.CreateHttpClient();
        LocalServerDiscovery discovery = new(new OpenCodeConnectionService(client), new LocalOpenCodeCommands());
        LocalServerInfo? found = await discovery.DiscoverAsync(TestContext.Current.CancellationToken);
        Assert.NotNull(found);
        Assert.Equal("2.1.4", found.Version);
        string? password = await discovery.GetPasswordAsync(found.Address, TestContext.Current.CancellationToken);
        Assert.False(string.IsNullOrEmpty(password));
        Assert.False(found.ToString().Contains(password!, StringComparison.Ordinal));
        password = null;
    }

    private sealed class Commands : ILocalOpenCodeCommands
    {
        public string Version { get; set; } = "opencode v2.1.4\n";
        public string Status { get; set; } = ServerAddress.DefaultOrigin + "\n";
        public string? Password { get; set; } = "private-password\r\n";
        public bool Throw { get; set; }
        public bool Stall { get; set; }
        public List<string> Calls { get; } = [];
        public IEnumerable<string> FindExecutables() => ["fixture-cli"];
        public async Task<string?> ExecuteAsync(string executable, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
        {
            if (Throw)
            {
                throw new IOException("private-password");
            }

            if (Stall)
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }

            string command = string.Join(" ", arguments);
            Calls.Add(command);
            return command switch { "--version" => Version, "service status" => Status, "service get password" => Password, _ => throw new InvalidOperationException("Unexpected command") };
        }
    }
    private sealed class Probe : IServerConnectionService
    {
        public int Calls { get; private set; }
        public bool Accept { get; set; } = true;
        public Task<ConnectionResult> ProbeAsync(ServerAddress address, string password, CancellationToken cancellationToken)
        {
            Calls++;
            Assert.Equal("private-password", password);
            return Task.FromResult(Accept ? new ConnectionResult(ConnectionOutcome.Ready, "Ready", "2.1.4") : new(ConnectionOutcome.AuthenticationFailed, "Rejected"));
        }
    }
}
