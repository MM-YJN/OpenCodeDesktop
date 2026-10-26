using OpenCodeDesktop.Core.Connections;
using OpenCodeDesktop.Core.Workspaces;

namespace OpenCodeDesktop.Core.Tests;

public sealed class TaskNotificationPolicyTests
{
    private readonly Guid _connection = Guid.NewGuid();
    private const string Address = "http://127.0.0.1:4096";
    private static NotificationObservation Snapshot(double? boundary = 1, bool completed = true) =>
        new("session", "/project", "project", true, completed, boundary, [], []);

    [Fact]
    public void RestartHydrationAndDuplicateSnapshotsNeverReplayHistory()
    {
        TaskNotificationPolicy policy = new();
        var snapshot = Snapshot() with { Permissions = ["old-permission"], Forms = ["old-form"] };
        Assert.Empty(Observe(policy, snapshot));
        Assert.Empty(Observe(policy, snapshot));
        Assert.Empty(Observe(new(), snapshot));
        Assert.Single(Observe(policy, snapshot with { CompletionBoundary = 2 }));
        Assert.Empty(Observe(policy, snapshot with { CompletionBoundary = 2 }));
        Assert.Empty(Observe(policy, snapshot)); // A delayed old boundary cannot rewind the watermark.
        Assert.Empty(Observe(policy, snapshot with { CompletionBoundary = 2 }));
    }

    [Fact]
    public void ReconnectSkipsStaleDataAndNotifiesMissedCompletionAndRequestsOnce()
    {
        TaskNotificationPolicy policy = new();
        Assert.Empty(Observe(policy, Snapshot(completed: false)));
        var recovered = Snapshot(2) with { Permissions = ["permission"], Forms = ["form"] };
        Assert.Empty(Observe(policy, recovered with { Confirmed = false }));
        Assert.Equal(3, Observe(policy, recovered).Count);
        Assert.Empty(Observe(policy, recovered));
        Assert.Empty(Observe(policy, Snapshot(2)));
        Assert.Empty(Observe(policy, recovered)); // Settled IDs never become new requests again.
    }

    [Theory]
    [InlineData(true, true, true, 0)]
    [InlineData(false, true, false, 2)]
    [InlineData(true, false, false, 1)]
    [InlineData(false, false, false, 0)]
    public void PreferencesAndForegroundSuppressionConsumeIdentities(bool completion, bool attention, bool visible, int count)
    {
        TaskNotificationPolicy policy = new();
        Assert.Empty(Observe(policy, Snapshot()));
        var next = Snapshot(2) with { Permissions = ["permission"], Forms = ["form"] };
        Assert.Equal(count, policy.Observe(_connection, Address, next, new(completion, attention), visible).Count);
        Assert.Empty(Observe(policy, next));
    }

    [Fact]
    public void FailuresAndUnconfirmedCompletionDoNotNotify()
    {
        TaskNotificationPolicy policy = new();
        Assert.Empty(Observe(policy, Snapshot(null, false)));
        Assert.Empty(Observe(policy, Snapshot(1, false)));
        Assert.Empty(Observe(policy, Snapshot(1, true))); // Same failed boundary cannot be reinterpreted later.
        Assert.Single(Observe(policy, Snapshot(2)));
    }

    [Fact]
    public void NewlyDiscoveredLiveAttentionUsesDirectoryBaselineButNeverAnnouncesHistoricalCompletion()
    {
        TaskNotificationPolicy policy = new();
        var first = Snapshot() with { Permissions = ["new"], AttentionBaselineKnown = true };
        Assert.Equal(TaskNotificationKind.Permission, Assert.Single(Observe(policy, first)).Kind);
        Assert.Empty(Observe(policy, first));
        Assert.Empty(policy.Observe(Guid.NewGuid(), Address, first with { AttentionBaselineKnown = false }, new(), false));
        Assert.Empty(policy.Observe(_connection, "http://127.0.0.1:4097", first with { AttentionBaselineKnown = false }, new(), false));
    }

    private IReadOnlyList<NotificationCandidate> Observe(TaskNotificationPolicy policy, NotificationObservation snapshot) =>
        policy.Observe(_connection, Address, snapshot, new(), false);
}

public sealed partial class LiveTaskWorkflowTests
{
    [Fact]
    public async Task AuthoritativeWorkflowObservationsRequireSuccessfulNewIdleBoundary()
    {
        using Api api = new();
        api.Snapshot = api.Snapshot with { Idle = 1, Outcome = "succeeded" };
        api.History = [new("old-idle", "idle", "old")];
        using LiveTaskWorkflow workflow = new(api, api);
        TaskNotificationPolicy policy = new();
        Guid connection = Guid.NewGuid();
        List<NotificationCandidate> notifications = [];
        workflow.Changed += (_, _) =>
        {
            if (workflow.Find("s") is { } observed)
            {
                notifications.AddRange(policy.Observe(connection, "http://127.0.0.1:4096", observed.NotificationObservation, new(), false));
            }
        };
        await workflow.OpenAsync(api.Session);
        Assert.Empty(notifications);
        api.Snapshot = api.Snapshot with { Active = true };
        await workflow.RefreshAsync(workflow.Find("s")!);
        api.Snapshot = api.Snapshot with { Active = false, Idle = 2, Outcome = "succeeded" };
        api.History = [new("new-idle", "idle", "new")];
        await workflow.RefreshAsync(workflow.Find("s")!);
        Assert.Equal(TaskNotificationKind.Completion, Assert.Single(notifications).Kind);
        await workflow.RefreshAsync(workflow.Find("s")!);
        Assert.Single(notifications);
        api.Snapshot = api.Snapshot with { Idle = 3, Outcome = "interrupted" };
        await workflow.RefreshAsync(workflow.Find("s")!);
        Assert.Single(notifications);
    }

    [Fact]
    public async Task NotificationRoutingValidatesOwnershipAndStaleRequestsWithoutMutatingServer()
    {
        using Api api = new();
        using ServerConnections connections = new(new Service(api));
        ServerProfile profile = connections.Save(null, "Test", "http://127.0.0.1:4096")!;
        await connections.ConnectAsync(profile.Id, "secret", TestContext.Current.CancellationToken);
        using LiveWorkspace workspace = new(connections);
        await workspace.OpenProjectSessionAsync("/dir", "s");
        SessionTaskState state = workspace.Tasks!.Find("s")!;
        var completion = workspace.RememberNotification(state, new(TaskNotificationKind.Completion, null));
        var permission = workspace.RememberNotification(state, new(TaskNotificationKind.Permission, "settled"));
        Assert.Null(await workspace.ActivateNotificationAsync(completion.Token));
        Assert.Contains("no longer pending", await workspace.ActivateNotificationAsync(permission.Token), StringComparison.Ordinal);
        Assert.Contains("expired", await workspace.ActivateNotificationAsync(Guid.NewGuid()), StringComparison.Ordinal);
        workspace.RemoveProject("/dir");
        Assert.Contains("removed", await workspace.ActivateNotificationAsync(completion.Token), StringComparison.Ordinal);
        Assert.Empty(workspace.Projects);
        connections.Disconnect();
        Assert.Contains("explicitly", await workspace.ActivateNotificationAsync(completion.Token), StringComparison.Ordinal);
        Assert.Null(connections.Active);
        Assert.Equal(0, api.Submissions + api.PermissionCount + api.AnswerCount + api.CancelCount);
    }

    [Fact]
    public async Task NotificationFromAnotherBackendNeverSwitchesItOrChangesNavigation()
    {
        using Api api = new();
        using ServerConnections connections = new(new Service(api));
        ServerProfile first = connections.Save(null, "First", "http://127.0.0.1:4096")!;
        await connections.ConnectAsync(first.Id, "secret", TestContext.Current.CancellationToken);
        using LiveWorkspace workspace = new(connections);
        await workspace.OpenProjectSessionAsync("/dir", "s");
        var target = workspace.RememberNotification(workspace.Tasks!.Find("s")!, new(TaskNotificationKind.Completion, null));
        ServerProfile second = connections.Save(null, "Second", "http://127.0.0.1:4097")!;
        await connections.ConnectAsync(second.Id, "secret", TestContext.Current.CancellationToken);
        await workspace.ConnectionTransition;
        Assert.Contains("explicitly", await workspace.ActivateNotificationAsync(target.Token), StringComparison.Ordinal);
        Assert.Equal(second.Id, connections.Active!.Profile.Id);
        Assert.Null(workspace.Location);
    }
}
