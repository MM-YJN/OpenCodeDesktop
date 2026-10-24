using System.Diagnostics;

using OpenCodeDesktop.Infrastructure.Connections;

namespace OpenCodeDesktop.Infrastructure.Tests;

public sealed class LocalOpenCodeCommandsTests
{
    [Theory]
    [InlineData("normal", "opencode v2.1.4")]
    [InlineData("fail", null)]
    public async Task CompletedCommandsReturnOnlySuccessfulOutputAndExitCleanly(string mode, string? expected)
    {
        using Fixture fixture = new(mode);
        string? result = await new LocalOpenCodeCommands().ExecuteAsync(fixture.Executable, ["--version"], TestContext.Current.CancellationToken);
        Assert.Equal(expected, result);
        fixture.AssertExited();
    }

    [Theory]
    [InlineData("stall")]
    [InlineData("stdout")]
    [InlineData("stderr")]
    public async Task DeadlineAndBothOutputBoundsTerminateAndReapOwnedProcess(string mode)
    {
        using Fixture fixture = new(mode);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new LocalOpenCodeCommands(TimeSpan.FromSeconds(2)).ExecuteAsync(fixture.Executable, ["--version"], TestContext.Current.CancellationToken));
        fixture.AssertExited();
    }

    [Fact]
    public async Task ExternalCancellationReapsProcessAndMutatingCommandsAreRejected()
    {
        using Fixture fixture = new("stall");
        using CancellationTokenSource cancel = new();
        Task<string?> running = new LocalOpenCodeCommands().ExecuteAsync(fixture.Executable, ["--version"], cancel.Token);
        for (int attempt = 0; attempt < 100 && !File.Exists(fixture.Executable + ".pid"); attempt++)
        {
            await Task.Delay(20, TestContext.Current.CancellationToken);
        }

        Assert.True(File.Exists(fixture.Executable + ".pid"));
        await cancel.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running);
        fixture.AssertExited();
        await Assert.ThrowsAsync<ArgumentException>(() => new LocalOpenCodeCommands().ExecuteAsync(fixture.Executable, ["service", "start"], TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ArgumentException>(() => new LocalOpenCodeCommands().ExecuteAsync(fixture.Executable, ["service", "get"], TestContext.Current.CancellationToken));
    }

    private sealed class Fixture : IDisposable
    {
        public string Executable { get; }
        public Fixture(string mode)
        {
            string directory = Path.Combine(AppContext.BaseDirectory, "CliFixture");
            Executable = Path.Combine(directory, "fixture-" + mode + "-" + Guid.NewGuid().ToString("N") + ".exe");
            File.Copy(Path.Combine(directory, "OpenCodeDesktop.CliFixture.exe"), Executable);
        }
        public void AssertExited()
        {
            string pidText = File.ReadAllText(Executable + ".pid");
            int pid = int.Parse(pidText, System.Globalization.CultureInfo.InvariantCulture);
            try
            {
                using var process = Process.GetProcessById(pid);
                Assert.True(process.HasExited);
            }
            catch (ArgumentException) { }
        }
        public void Dispose()
        {
            File.Delete(Executable);
            File.Delete(Executable + ".pid");
        }
    }
}
