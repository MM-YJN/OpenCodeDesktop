using System.Threading.Channels;

using OpenCodeDesktop.Core.Connections;
using OpenCodeDesktop.Core.Workspaces;

namespace OpenCodeDesktop.Core.Tests;

public sealed partial class LiveTaskWorkflowTests
{
    [Fact]
    public async Task RemovingProjectRetainsRunningTaskAndPendingAttentionForReopening()
    {
        using Api api = new();
        api.Snapshot = api.Snapshot with { Active = true, Forms = [new("pending", "s", "Question", [])] };
        using ServerConnections connections = new(new Service(api));
        ServerProfile profile = connections.Save(null, "Test", "http://127.0.0.1:4096")!;
        await connections.ConnectAsync(profile.Id, "secret", TestContext.Current.CancellationToken);
        using LiveWorkspace workspace = new(connections);
        await workspace.OpenProjectAsync("/dir");
        await workspace.SelectSessionAsync("s");
        workspace.Draft = "keep";
        SessionTaskState state = workspace.Tasks!.Find("s")!;
        workspace.RemoveProject("/dir");
        Assert.Empty(workspace.Projects);
        Assert.Null(workspace.Selected);
        Assert.Same(state, workspace.Tasks.Find("s"));
        Assert.True(state.Active);
        Assert.Single(state.Forms);
        await workspace.OpenProjectAsync(state.Directory);
        await workspace.SelectAttentionSessionAsync(state.Id);
        Assert.Equal("keep", workspace.Draft);
        Assert.Single(workspace.Projects);
        Assert.Single(state.Forms);
        Assert.Equal(0, api.Submissions);
        workspace.RemoveProject("/dir");
        connections.Disconnect();
        api.PendingSessions = ["s"];
        api.Snapshot = api.Snapshot with { Forms = [new("later", "s", "New question", [])] };
        await connections.ConnectAsync(profile.Id, "secret", TestContext.Current.CancellationToken);
        await ObserveAsync(workspace.Tasks!, () => workspace.Tasks!.Find("s")!.Forms.Any(f => f.Id == "later"));
        Assert.Null(workspace.Location);
        Assert.Empty(workspace.Projects);
    }

    [Fact]
    public async Task HealthyBackgroundReadDoesNotPreventInterruptAndReconcilesItsResult()
    {
        using Api api = new();
        api.Snapshot = api.Snapshot with { Active = true };
        using LiveTaskWorkflow workflow = new(api, api);
        await workflow.OpenAsync(api.Session);
        SessionTaskState state = workflow.Find("s")!;
        TaskSnapshot old = api.Snapshot;
        TaskCompletionSource<TaskSnapshot> held = new(TaskCreationOptions.RunContinuationsAsynchronously);
        api.HeldSnapshot = held;
        Task refreshing = workflow.RefreshAsync(state);
        Assert.True(state.Loading);
        Assert.False(state.Stale);
        await workflow.StopAsync(state);
        held.SetResult(old);
        await refreshing;
        Assert.Equal("Interrupted", state.Outcome);
        Assert.False(state.Active);
    }

    [Fact]
    public async Task InitialHistoryReadFailureRecoversWithoutLosingSessionDraft()
    {
        using Api api = new();
        Scheduler scheduler = new();
        using ServerConnections connections = new(new Service(api));
        ServerProfile profile = connections.Save(null, "Test", "http://127.0.0.1:4096")!;
        await connections.ConnectAsync(profile.Id, "secret", TestContext.Current.CancellationToken);
        using LiveWorkspace workspace = new(connections, scheduler);
        await workspace.OpenProjectAsync("/dir");
        api.HistoryFailure = new WorkspaceException(WorkspaceFailure.Transport, "offline");
        await workspace.SelectSessionAsync("s");
        workspace.Draft = "keep this unsent";
        Assert.False(workspace.HistoryLoaded);
        DelayRequest delay = await scheduler.NextAsync();
        api.HistoryFailure = null;
        api.History = [new("missed", "assistant", "server history")];
        delay.Release.SetResult();
        await ObserveAsync(workspace.Tasks!, () => workspace.Tasks!.Recovery == WorkspaceRecovery.Current);
        Assert.True(workspace.HistoryLoaded);
        Assert.Null(workspace.Error);
        Assert.Single(workspace.History);
        Assert.Equal("keep this unsent", workspace.Draft);
        Assert.Equal(0, api.Submissions);
    }

    [Fact]
    public async Task FailedStreamsExhaustSameBudgetAndExplicitRetryResubscribes()
    {
        using Api api = new() { StreamFailure = new IOException("offline") };
        Scheduler scheduler = new();
        using LiveTaskWorkflow workflow = new(api, api, scheduler);
        await workflow.OpenAsync(api.Session);
        foreach (int seconds in new[] { 1, 2, 4, 8, 15 })
        {
            DelayRequest delay = await scheduler.NextAsync();
            Assert.Equal(TimeSpan.FromSeconds(seconds), delay.Delay);
            delay.Release.SetResult();
        }
        await ObserveAsync(workflow, () => workflow.CanRetryRecovery);
        Assert.Equal(6, api.Watches);
        api.StreamFailure = null;
        await workflow.RetryRecoveryAsync();
        Assert.Equal(7, api.Watches);
        Assert.Equal(WorkspaceRecovery.Current, workflow.Recovery);
        Assert.False(workflow.Find("s")!.Stale);
    }

    [Fact]
    public async Task EventBurstDuringRecoveryInvalidatesSnapshotAndDoesNotDuplicateRefreshedText()
    {
        using Api api = new();
        using LiveTaskWorkflow workflow = new(api, api);
        await workflow.OpenAsync(api.Session);
        SessionTaskState state = workflow.Find("s")!;
        TaskSnapshot old = api.Snapshot with { Outcome = "failed", Idle = 1 };
        TaskCompletionSource<TaskSnapshot> held = new(TaskCreationOptions.RunContinuationsAsynchronously);
        api.HeldSnapshot = held;
        Task recovery = workflow.RetryRecoveryAsync();
        await ObserveAsync(workflow, () => state.Loading);
        await api.Events.Writer.WriteAsync(new("", "session.text.started", "s", "m", 0, null), TestContext.Current.CancellationToken);
        for (int i = 0; i < 300; i++)
        {
            await api.Events.Writer.WriteAsync(new("burst" + i, "session.text.delta", "s", "m", 0, "x"), TestContext.Current.CancellationToken);
        }
        await api.Events.Writer.WriteAsync(new("marker", "session.execution.started", "s", null, 0, null), TestContext.Current.CancellationToken);
        await ObserveAsync(workflow, () => state.Active);
        api.Snapshot = api.Snapshot with { Active = true, Outcome = "failed", Idle = 1 };
        api.History = [new("m", "assistant", new string('x', 300))];
        held.SetResult(old);
        await recovery;
        Assert.Equal(WorkspaceRecovery.Current, workflow.Recovery);
        Assert.Equal("Running", state.Status);
        Assert.Equal(new string('x', 300), Assert.Single(state.Conversation).Text);
        Assert.True(api.SnapshotReads >= 3);
    }

    [Fact]
    public async Task RecoveryUsesFiveBoundedDelaysThenExplicitRetry()
    {
        using Api api = new();
        Scheduler scheduler = new();
        using LiveTaskWorkflow workflow = new(api, api, scheduler);
        await workflow.OpenAsync(api.Session);
        SessionTaskState state = workflow.Find("s")!;
        api.ReadFailure = new WorkspaceException(WorkspaceFailure.Transport, "offline");
        await workflow.RefreshAsync(state);
        foreach (int seconds in new[] { 1, 2, 4, 8, 15 })
        {
            DelayRequest delay = await scheduler.NextAsync();
            Assert.Equal(TimeSpan.FromSeconds(seconds), delay.Delay);
            Assert.True(state.Stale);
            Assert.False(state.CanSend);
            delay.Release.SetResult();
        }
        await ObserveAsync(workflow, () => workflow.CanRetryRecovery);
        Assert.Equal(WorkspaceRecovery.RetryRequired, workflow.Recovery);
        Assert.Equal(7, api.SnapshotReads); // initial hydration, failed refresh, five retries
        api.ReadFailure = null;
        await workflow.RetryRecoveryAsync();
        Assert.Equal(WorkspaceRecovery.Current, workflow.Recovery);
        Assert.False(state.Stale);
        Assert.True(state.CanSend);
    }

    [Theory]
    [InlineData(WorkspaceFailure.Unauthorized, WorkspaceRecovery.ReconnectRequired)]
    [InlineData(WorkspaceFailure.Protocol, WorkspaceRecovery.ActionRequired)]
    [InlineData(WorkspaceFailure.Size, WorkspaceRecovery.ActionRequired)]
    public async Task NonRecoverableReadsExplainUserActionWithoutRetry(WorkspaceFailure failure, WorkspaceRecovery expected)
    {
        using Api api = new();
        Scheduler scheduler = new();
        using LiveTaskWorkflow workflow = new(api, api, scheduler);
        await workflow.OpenAsync(api.Session);
        api.ReadFailure = new WorkspaceException(failure, "safe failure");
        await workflow.RefreshAsync(workflow.Find("s")!);
        await workflow.RetryRecoveryAsync();
        Assert.Equal(expected, workflow.Recovery);
        Assert.Contains("Settings > Connections", workflow.RecoveryStatus, StringComparison.Ordinal);
        Assert.Equal(2, api.SnapshotReads);
        Assert.Equal(0, scheduler.Count);
        Assert.True(workflow.Find("s")!.Stale);
    }

    [Fact]
    public async Task RecoveryFindsMissedQuestionAndTerminalAndSettlesOldAttentionWithoutResending()
    {
        using Api api = new();
        Scheduler scheduler = new();
        using LiveTaskWorkflow workflow = new(api, api, scheduler);
        await workflow.OpenDirectoryAsync(new("/dir", "project"));
        await workflow.OpenAsync(api.Session);
        SessionTaskState state = workflow.Find("s")!;
        api.LoseAcknowledgement = true;
        await workflow.SendAsync(state, "immutable original");
        Assert.True(state.DeliveryUncertain);
        string inputId = state.Submission!.Id;
        api.Snapshot = api.Snapshot with { Permissions = [new("old", "s", "read", ["file"], [])] };
        await workflow.RefreshAsync(state);
        await api.Events.Writer.WriteAsync(new("", "fixture.disconnect", null, null, 0, null), TestContext.Current.CancellationToken);
        DelayRequest delay = await scheduler.NextAsync();
        Assert.Contains("last observed", state.Status, StringComparison.Ordinal);
        api.RecoverInOlderHistory = true;
        api.History = [new("terminal", "idle", "finished while disconnected")];
        api.Snapshot = api.Snapshot with { Active = false, Outcome = "succeeded", Idle = 9, Permissions = [] };
        WorkspaceSession other = new("question", "/dir", "project", "Missed question");
        api.OtherSnapshots[other.Id] = new(other, false, null, null, null, null, [], [], [new("new-form", other.Id, "Question", [new("q", "string")])]);
        api.PendingSessions = [other.Id];
        delay.Release.SetResult();
        await ObserveAsync(workflow, () => workflow.Recovery == WorkspaceRecovery.Current);
        Assert.False(state.DeliveryUncertain);
        Assert.Equal("Completed", state.Outcome);
        Assert.Empty(state.Permissions);
        Assert.Single(workflow.Find(other.Id)!.Forms);
        Assert.False(workflow.Find(other.Id)!.Stale);
        Assert.Single(state.Conversation, m => m.Id == inputId);
        Assert.Equal(1, api.Submissions);
        Assert.Equal(2, api.Watches);
    }

    [Fact]
    public async Task PartialRecoveryLeavesEverySessionStaleUntilAllRequiredReadsSucceed()
    {
        using Api api = new();
        Scheduler scheduler = new();
        using LiveTaskWorkflow workflow = new(api, api, scheduler);
        await workflow.OpenDirectoryAsync(new("/dir", "project"));
        await workflow.OpenAsync(api.Session);
        WorkspaceSession other = new("other", "/dir", "project", "Other");
        api.OtherSnapshots[other.Id] = api.Snapshot with { Session = other };
        SessionTaskState second = workflow.Track(other);
        await workflow.RefreshAsync(second);
        api.PendingSessions = ["other"];
        TaskCompletionSource<TaskSnapshot> held = new(TaskCreationOptions.RunContinuationsAsynchronously);
        api.HeldSnapshot = held;
        Task recovery = workflow.RetryRecoveryAsync();
        await ObserveAsync(workflow, () => workflow.Find("s")!.Loading);
        held.SetException(new WorkspaceException(WorkspaceFailure.Timeout, "timeout"));
        DelayRequest delay = await scheduler.NextAsync();
        Assert.Equal(WorkspaceRecovery.Recovering, workflow.Recovery);
        Assert.All(workflow.Sessions, s => Assert.True(s.Stale));
        delay.Release.SetResult();
        await recovery;
        Assert.All(workflow.Sessions, s => Assert.False(s.Stale));
    }

    [Fact]
    public async Task DisposingCancelsPendingBackoffAndDoesNotReconnect()
    {
        using Api api = new();
        Scheduler scheduler = new();
        LiveTaskWorkflow workflow = new(api, api, scheduler);
        await workflow.OpenAsync(api.Session);
        api.ReadFailure = new WorkspaceException(WorkspaceFailure.Transport, "offline");
        await workflow.RefreshAsync(workflow.Find("s")!);
        DelayRequest delay = await scheduler.NextAsync();
        workflow.Dispose();
        await delay.Cancelled.Task.WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
        Assert.Equal(1, api.Watches);
        Assert.Equal(2, api.SnapshotReads);
    }

    [Fact]
    public async Task ObsoleteRecoveryCannotApplyHeldSnapshotAfterReplacement()
    {
        using Api api = new();
        using LiveTaskWorkflow previous = new(api, api, new Scheduler());
        await previous.OpenAsync(api.Session);
        TaskCompletionSource<TaskSnapshot> held = new(TaskCreationOptions.RunContinuationsAsynchronously);
        api.HeldSnapshot = held;
        Task recovery = previous.RetryRecoveryAsync();
        await ObserveAsync(previous, () => previous.Find("s")!.Loading);
        using LiveTaskWorkflow replacement = new(api, api);
        replacement.Restore(previous.Sessions);
        previous.Dispose();
        held.SetResult(api.Snapshot with { Active = true });
        await recovery;
        Assert.False(replacement.Find("s")!.Active);
        Assert.True(replacement.Find("s")!.Stale);
    }

    [Fact]
    public async Task FreshWorkspaceRequiresExplicitConnectionAndSelectionToReopenServerWork()
    {
        using Api api = new();
        using (ServerConnections connections = new(new Service(api)))
        {
            ServerProfile profile = connections.Save(null, "First", "http://127.0.0.1:4096")!;
            await connections.ConnectAsync(profile.Id, "secret", TestContext.Current.CancellationToken);
            using LiveWorkspace workspace = new(connections);
            await workspace.OpenProjectAsync("/dir");
            await workspace.SelectSessionAsync("s");
            workspace.Draft = "only in the first app lifetime";
        }
        api.History = [new("server-message", "assistant", "retained server work")];
        api.Snapshot = api.Snapshot with { Active = true, Forms = [new("pending", "s", "Question", [])] };
        using ServerConnections freshConnections = new(new Service(api));
        using LiveWorkspace fresh = new(freshConnections);
        Assert.Empty(freshConnections.Profiles);
        Assert.Null(fresh.Selected);
        ServerProfile freshProfile = freshConnections.Save(null, "Explicit", "http://127.0.0.1:4096")!;
        await freshConnections.ConnectAsync(freshProfile.Id, "secret", TestContext.Current.CancellationToken);
        await fresh.OpenProjectAsync("/dir");
        await fresh.SelectSessionAsync("s");
        Assert.Empty(fresh.Draft);
        Assert.Contains(fresh.Tasks!.Find("s")!.Conversation, m => m.Id == "server-message");
        Assert.True(fresh.Tasks.Find("s")!.Active);
        Assert.Single(fresh.Tasks.Find("s")!.Forms);
        Assert.Equal(0, api.Submissions);
    }

    private static async Task ObserveAsync(LiveTaskWorkflow workflow, Func<bool> predicate)
    {
        TaskCompletionSource observed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        void Changed(object? sender, EventArgs args)
        {
            if (predicate())
            {
                observed.TrySetResult();
            }
        }
        workflow.Changed += Changed;
        try
        {
            Changed(null, EventArgs.Empty);
            await observed.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        }
        finally
        {
            workflow.Changed -= Changed;
        }
    }

    private sealed class Scheduler : IRecoveryScheduler
    {
        private readonly Channel<DelayRequest> _delays = Channel.CreateUnbounded<DelayRequest>();
        public int Count { get; private set; }
        public async Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            Count++;
            DelayRequest request = new(delay);
            await _delays.Writer.WriteAsync(request, cancellationToken);
            try
            {
                await request.Release.Task.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                request.Cancelled.TrySetResult();
                throw;
            }
        }
        public async Task<DelayRequest> NextAsync() => await _delays.Reader.ReadAsync(TestContext.Current.CancellationToken).AsTask().WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
    }
    private sealed class DelayRequest(TimeSpan delay)
    {
        public TimeSpan Delay { get; } = delay;
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Cancelled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
