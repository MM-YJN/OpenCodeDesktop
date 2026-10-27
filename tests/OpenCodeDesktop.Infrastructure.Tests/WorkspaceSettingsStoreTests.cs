using System.Text;
using System.Text.Json;

using OpenCodeDesktop.Core.Connections;
using OpenCodeDesktop.Core.Workspaces;
using OpenCodeDesktop.Infrastructure.Persistence;

namespace OpenCodeDesktop.Infrastructure.Tests;

public sealed class WorkspaceSettingsStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "OpenCodeDesktop-WI012-" + Guid.NewGuid().ToString("N"));
    private string SettingsPath => Path.Combine(_directory, "workspace.v1.json");

    [Fact]
    public async Task NotificationPreferencesMigrateAndDestinationsSurviveRestart()
    {
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(SettingsPath, "{\"version\":1,\"connections\":[]}", TestContext.Current.CancellationToken);
        JsonWorkspaceSettingsStore store = new(_directory);
        WorkspaceSettings original = (await store.LoadAsync(TestContext.Current.CancellationToken))!;
        Assert.Equal(new NotificationPreferences(), original.Notifications);
        TaskNotificationTarget target = new(Guid.NewGuid(), Guid.NewGuid(), "http://127.0.0.1:4096", "/project", "project", "session",
            TaskNotificationKind.Form, "request", DateTimeOffset.UtcNow);
        await store.SaveAsync(original with { Notifications = new(false, true), NotificationTargets = [target] }, TestContext.Current.CancellationToken);
        WorkspaceSettings restored = (await new JsonWorkspaceSettingsStore(_directory).LoadAsync(TestContext.Current.CancellationToken))!;
        Assert.Equal(new NotificationPreferences(false, true), restored.Notifications);
        Assert.Equal(target, Assert.Single(restored.NotificationTargets));
        await store.SaveAsync(restored with { Notifications = new(true, false) }, TestContext.Current.CancellationToken);
        Assert.Equal(new NotificationPreferences(true, false), (await store.LoadAsync(TestContext.Current.CancellationToken))!.Notifications);
    }

    [Fact]
    public async Task RoundTripRetainsVersionOwnershipAndReferencesOnly()
    {
        JsonWorkspaceSettingsStore store = new(_directory);
        Assert.Null(await store.LoadAsync(TestContext.Current.CancellationToken));
        WorkspaceSettings settings = Sample();
        await store.SaveAsync(settings, TestContext.Current.CancellationToken);
        WorkspaceSettings loaded = (await new JsonWorkspaceSettingsStore(_directory).LoadAsync(TestContext.Current.CancellationToken))!;
        Assert.Equal(1, loaded.Version);
        Assert.Equal(settings.ActiveConnectionId, loaded.ActiveConnectionId);
        Assert.Equal(settings.Connections[0], loaded.Connections[0] with { Projects = settings.Connections[0].Projects });
        Assert.Equal(settings.Connections[0].Projects, loaded.Connections[0].Projects);
        string json = await File.ReadAllTextAsync(SettingsPath, TestContext.Current.CancellationToken);
        Assert.DoesNotContain("password", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("credentialReference", json);
    }

    [Theory]
    [InlineData("{")]
    [InlineData("null")]
    [InlineData("{\"version\":999,\"connections\":[]}")]
    [InlineData("{\"version\":1,\"connections\":null}")]
    [InlineData("{\"version\":1,\"connections\":[],\"notifications\":null}")]
    [InlineData("{\"version\":1,\"connections\":[],\"notificationTargets\":[null]}")]
    [InlineData("{\"version\":1,\"connections\":[null]}")]
    public async Task CorruptOrUnsupportedDataCannotBeOverwrittenWithoutExplicitReset(string json)
    {
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(SettingsPath, json, TestContext.Current.CancellationToken);
        JsonWorkspaceSettingsStore store = new(_directory);
        await Assert.ThrowsAnyAsync<Exception>(() => store.LoadAsync(TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.SaveAsync(Sample(), TestContext.Current.CancellationToken));
        Assert.Equal(json, await File.ReadAllTextAsync(SettingsPath, TestContext.Current.CancellationToken));
        await store.ResetAsync(TestContext.Current.CancellationToken);
        Assert.Equal(json, await File.ReadAllTextAsync(Assert.Single(Directory.GetFiles(_directory, "*.recovery-*")), TestContext.Current.CancellationToken));
        await store.SaveAsync(Sample(), TestContext.Current.CancellationToken);
        Assert.NotNull(await store.LoadAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ConcurrentSavesAndInterruptedTemporaryFilesLeaveAValidSnapshot()
    {
        JsonWorkspaceSettingsStore store = new(_directory);
        await store.LoadAsync(TestContext.Current.CancellationToken);
        await store.SaveAsync(Sample(), TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(SettingsPath + ".interrupted.tmp", "{partial", TestContext.Current.CancellationToken);
        await Task.WhenAll(Enumerable.Range(0, 25).Select(_ => store.SaveAsync(Sample(), TestContext.Current.CancellationToken)));
        Assert.NotNull(await store.LoadAsync(TestContext.Current.CancellationToken));
        Assert.Single(Directory.GetFiles(_directory, "*.tmp"));
    }

    [Fact]
    public async Task FailedAndCancelledWritesKeepLastValidBytes()
    {
        JsonWorkspaceSettingsStore store = new(_directory);
        await store.LoadAsync(TestContext.Current.CancellationToken);
        await store.SaveAsync(Sample(), TestContext.Current.CancellationToken);
        byte[] before = await File.ReadAllBytesAsync(SettingsPath, TestContext.Current.CancellationToken);
        if (OperatingSystem.IsWindows())
        {
            using FileStream locked = File.Open(SettingsPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            await Assert.ThrowsAsync<IOException>(() => store.SaveAsync(Sample(), TestContext.Current.CancellationToken));
        }
        using CancellationTokenSource cancelled = new();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.SaveAsync(Sample(), cancelled.Token));
        Assert.Equal(before, await File.ReadAllBytesAsync(SettingsPath, TestContext.Current.CancellationToken));
        Assert.Empty(Directory.GetFiles(_directory, "*.tmp"));
    }

    [Fact]
    public async Task OversizedFilesAndInvalidReferencesPathsAndOwnershipAreRejected()
    {
        JsonWorkspaceSettingsStore store = new(_directory);
        await store.LoadAsync(TestContext.Current.CancellationToken);
        WorkspaceSettings good = Sample();
        foreach (SavedConnection invalid in new[]
        {
            good.Connections[0] with { CredentialReference = "../escape" },
            good.Connections[0] with { SelectedDirectory = "/missing" },
            good.Connections[0] with { Address = "https://example.com" },
            good.Connections[0] with { Projects = [new("/same", "global", null), new("/same", "global", null)] },
            good.Connections[0] with { Projects = [new("/p", "global", null) { TaskSelection = new(new string('x', 4097), null, null) }] },
        })
        {
            await Assert.ThrowsAsync<InvalidDataException>(() => store.SaveAsync(good with { Connections = [invalid] }, TestContext.Current.CancellationToken));
        }
        Directory.CreateDirectory(_directory);
        await File.WriteAllBytesAsync(SettingsPath, new byte[4 * 1024 * 1024 + 1], TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<InvalidDataException>(() => store.LoadAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public async Task DpapiRoundTripTamperRejectionReferenceValidationAndDeletion()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        DpapiCredentialStore store = new(_directory);
        const string password = "private-credential-测试";
        string reference = await store.SaveAsync(password, TestContext.Current.CancellationToken);
        Assert.Equal(password, await store.ReadAsync(reference, TestContext.Current.CancellationToken));
        byte[] bytes = await File.ReadAllBytesAsync(Path.Combine(_directory, reference + ".bin"), TestContext.Current.CancellationToken);
        Assert.False(Encoding.UTF8.GetString(bytes).Contains(password, StringComparison.Ordinal));
        Assert.False(Encoding.Unicode.GetString(bytes).Contains(password, StringComparison.Ordinal));
        await Assert.ThrowsAsync<InvalidDataException>(() => store.ReadAsync("../escape", TestContext.Current.CancellationToken));
        bytes[^1] ^= 255;
        await File.WriteAllBytesAsync(Path.Combine(_directory, reference + ".bin"), bytes, TestContext.Current.CancellationToken);
        await Assert.ThrowsAnyAsync<System.Security.Cryptography.CryptographicException>(() => store.ReadAsync(reference, TestContext.Current.CancellationToken));
        await store.DeleteAsync(reference, TestContext.Current.CancellationToken);
        Assert.Empty(Directory.GetFiles(_directory));
    }

    private static WorkspaceSettings Sample()
    {
        var id = Guid.NewGuid();
        return new(1, id, [new(id, "Local", ServerAddress.DefaultOrigin, true, Guid.NewGuid().ToString("N"), "/项目 one", [new("/项目 one", "global", "session-a") { TaskSelection = new("provider-a", "model-a", "build") }, new("/two", "global", null)])]);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, true);
        }
    }
}
