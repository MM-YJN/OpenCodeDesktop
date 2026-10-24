using System.Threading.Channels;

using OpenCodeDesktop.Core.Connections;
using OpenCodeDesktop.Core.Workspaces;

namespace OpenCodeDesktop.Core.Tests;

public sealed class LiveTaskWorkflowTests
{
    [Fact]
    public async Task EventDuringHydrationInvalidatesOldTerminalSnapshot()
    {
        using Api api = new();
        using LiveTaskWorkflow workflow = new(api, api);
        await workflow.OpenAsync(api.Session);
        SessionTaskState state = workflow.Find("s")!;
        TaskSnapshot old = api.Snapshot with { Outcome = "failed", Idle = 1 };
        api.History = [new("old", "idle", "old")];
        TaskCompletionSource<TaskSnapshot> held = new(TaskCreationOptions.RunContinuationsAsynchronously);
        api.HeldSnapshot = held;
        Task refreshing = workflow.RefreshAsync(state);
        TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        void Changed(object? sender, EventArgs args)
        {
            if (state.Active)
            {
                started.TrySetResult();
            }
        }
        workflow.Changed += Changed;
        try
        {
            api.Snapshot = api.Snapshot with { Active = true, Outcome = "failed", Idle = 1 };
            await api.Events.Writer.WriteAsync(new("start", "session.execution.started", "s", null, 0, null), TestContext.Current.CancellationToken);
            await started.Task.WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
            held.SetResult(old);
            await refreshing;
            Assert.Equal("Running", state.Status);
            Assert.NotEqual("Failed", state.Outcome);
        }
        finally
        {
            workflow.Changed -= Changed;
        }
    }

    [Fact]
    public async Task RejectedAnswerRetainsValuesAndUnsupportedFormCanBeCancelled()
    {
        using Api api = new();
        using LiveTaskWorkflow workflow = new(api, api);
        TaskForm form = new("frm", "s", "Answer", [new("q", "string") { Required = true }]);
        api.Snapshot = api.Snapshot with { Forms = [form] };
        await workflow.OpenAsync(api.Session);
        SessionTaskState state = workflow.Find("s")!;
        state.Answers[form.Id]["q"] = "retained";
        api.RejectAnswer = true;
        await workflow.AnswerAsync(state, form, false);
        Assert.Equal("retained", state.Answers[form.Id]["q"]);
        Assert.Single(state.Forms);
        Assert.Contains("validation", state.Error!, StringComparison.Ordinal);
        Assert.Equal(1, api.AnswerCount);
        api.Snapshot = api.Snapshot with { Forms = [form with { Fields = [new("hidden", "future") { Hidden = true, Required = true }] }] };
        await workflow.RefreshAsync(state);
        await workflow.AnswerAsync(state, state.Forms[0], true);
        Assert.Empty(state.Forms);
        Assert.Equal(1, api.CancelCount);
        Assert.Equal(1, api.AnswerCount);
    }
    [Fact]
    public async Task StreamingStaysWithOwningSessionAndDisconnectDoesNotReconnectOrCompleteTask()
    {
        using Api api = new();
        using LiveTaskWorkflow workflow = new(api, api);
        api.Snapshot = api.Snapshot with { Active = true };
        await workflow.OpenAsync(api.Session);
        SessionTaskState state = workflow.Find("s")!;
        SessionTaskState other = workflow.Track(new("other", "/dir", "project", "Other"));
        await EmitAsync(new("start", "session.text.started", "s", "message", 0, null), () => true);
        await EmitAsync(new("delta", "session.text.delta", "s", "message", 0, "partial"), () => state.Conversation.Any(m => m.Text == "partial"));
        Assert.DoesNotContain(other.Conversation, m => m.Text.Contains("partial", StringComparison.Ordinal));
        await EmitAsync(new("unknown", "session.text.delta", "unopened", "message", 0, "foreign"), () => true);
        Assert.DoesNotContain(state.Conversation, m => m.Text.Contains("foreign", StringComparison.Ordinal));
        await EmitAsync(new("disconnect", "fixture.disconnect", null, null, 0, null), () => state.Stale);
        Assert.True(state.Active);
        Assert.Equal(1, api.Watches);
        Assert.False(state.CanSend);

        async Task EmitAsync(TaskEvent e, Func<bool> observed)
        {
            TaskCompletionSource changed = new(TaskCreationOptions.RunContinuationsAsynchronously);
            void OnChanged(object? sender, EventArgs args)
            {
                if (observed())
                {
                    changed.TrySetResult();
                }
            }
            workflow.Changed += OnChanged;
            try
            {
                await api.Events.Writer.WriteAsync(e, TestContext.Current.CancellationToken);
                await changed.Task.WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
            }
            finally
            {
                workflow.Changed -= OnChanged;
            }
        }
    }
    [Fact]
    public async Task EditableDraftIsSeparateFromAdmittedPayloadAndUncertainDraftIsRetained()
    {
        using Api api = new();
        using ServerConnections connections = new(new Service(api));
        ServerProfile profile = connections.Save(null, "Test", "http://127.0.0.1:4096")!;
        await connections.ConnectAsync(profile.Id, "secret");
        using LiveWorkspace workspace = new(connections);
        await workspace.OpenProjectAsync("/dir");
        await workspace.SelectSessionAsync("s");
        workspace.Draft = "original";
        api.PendingSubmit = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Task sending = workspace.SendTaskAsync();
        workspace.Draft = "typed while sending";
        api.PendingSubmit.SetResult();
        await sending;
        Assert.Equal("typed while sending", workspace.Draft);
        Assert.Equal("original", workspace.Tasks!.Find("s")!.Submission!.Text);
    }

    [Fact]
    public async Task AdmittedUneditedDraftIsConsumedAndPreflightFailureRetainsDraft()
    {
        using Api api = new();
        using ServerConnections connections = new(new Service(api));
        ServerProfile profile = connections.Save(null, "Test", "http://127.0.0.1:4096")!;
        await connections.ConnectAsync(profile.Id, "secret");
        using LiveWorkspace workspace = new(connections);
        await workspace.OpenProjectAsync("/dir");
        await workspace.SelectSessionAsync("s");
        workspace.Draft = "admitted";
        api.RecoverInOlderHistory = true;
        await workspace.SendTaskAsync();
        Assert.Empty(workspace.Draft);
        workspace.Draft = "unsent";
        api.Catalog = new([], []);
        await workspace.SendTaskAsync();
        Assert.Equal("unsent", workspace.Draft);
    }

    private sealed class Service(Api api) : IServerConnectionService, IWorkspaceConnectionFactory
    {
        public IWorkspaceConnection Create(ServerAddress address, string password) => api;
        public Task<ConnectionResult> ProbeAsync(ServerAddress address, string password, CancellationToken cancellationToken) => Task.FromResult(new ConnectionResult(ConnectionOutcome.Ready, "Ready", "2.1.4"));
    }
    [Fact]
    public async Task ActiveTaskOverridesOldFailureAndInactiveAttentionDoesNotImplyRunning()
    {
        using Api api = new();
        using LiveTaskWorkflow workflow = new(api, api);
        api.Snapshot = api.Snapshot with { Active = true, Outcome = "failed", Idle = 1 };
        api.History = [new("old", "idle", "old boundary")];
        await workflow.OpenAsync(api.Session);
        SessionTaskState state = workflow.Find("s")!;
        Assert.Equal("Running", state.Status);
        Assert.False(state.CanSend);
        api.Snapshot = api.Snapshot with { Active = false, Permissions = [new("p", "s", "read", ["file"], ["*"])] };
        await workflow.RefreshAsync(state);
        Assert.Equal("Waiting for you · session inactive", state.Status);
    }

    [Fact]
    public async Task OneImmutableSubmissionOwnsGateWhileOtherSessionsCanBeOpened()
    {
        using Api api = new();
        using LiveTaskWorkflow workflow = new(api, api);
        await workflow.OpenAsync(api.Session);
        SessionTaskState state = workflow.Find("s")!;
        Assert.True(state.CanSend);
        api.PendingSubmit = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Task send = workflow.SendAsync(state, "original");
        Assert.True(state.Busy);
        Assert.False(state.CanSend);
        Assert.Equal("original", state.Submission!.Text);
        await workflow.SendAsync(state, "edited");
        SessionTaskState other = workflow.Track(new("other", "/dir", "project", "Other"));
        Assert.NotSame(state, other);
        Assert.Null(other.Submission);
        api.PendingSubmit.SetResult();
        await send;
        Assert.Equal(1, api.Submissions);
        Assert.StartsWith("msg_", state.Submission!.Id, StringComparison.Ordinal);
        Assert.Equal("original", state.Submission.Text);
    }

    [Fact]
    public async Task LostAcknowledgementIsReconciledThroughOlderHistoryWithoutPostingAgain()
    {
        using Api api = new();
        using LiveTaskWorkflow workflow = new(api, api);
        await workflow.OpenAsync(api.Session);
        SessionTaskState state = workflow.Find("s")!;
        api.LoseAcknowledgement = true;
        api.RecoverInOlderHistory = true;
        await workflow.SendAsync(state, "once");
        Assert.Equal(1, api.Submissions);
        Assert.False(state.DeliveryUncertain);
        Assert.Equal("Completed", state.Status);
        Assert.Null(state.Submission);
        Assert.Contains(state.History, m => m.Text == "once");
        await workflow.RefreshAsync(state);
        Assert.Equal(1, api.Submissions);
        Assert.Equal(1, state.History.Count(m => m.Text == "once"));
    }

    [Fact]
    public async Task LostAcknowledgementWithoutEvidenceKeepsOriginalAndPreventsEditedSend()
    {
        using Api api = new();
        using LiveTaskWorkflow workflow = new(api, api);
        await workflow.OpenAsync(api.Session);
        SessionTaskState state = workflow.Find("s")!;
        api.LoseAcknowledgement = true;
        await workflow.SendAsync(state, "original");
        Assert.True(state.DeliveryUncertain);
        Assert.False(state.CanSend);
        await workflow.SendAsync(state, "edited");
        Assert.Equal("original", state.Submission!.Text);
        Assert.Equal(1, api.Submissions);
    }

    [Fact]
    public async Task InterruptionRequiresAuthoritativeIdleAndRetainsParkedInbox()
    {
        using Api api = new();
        using LiveTaskWorkflow workflow = new(api, api);
        api.Snapshot = api.Snapshot with { Active = true };
        await workflow.OpenAsync(api.Session);
        SessionTaskState state = workflow.Find("s")!;
        await workflow.StopAsync(state);
        Assert.Equal("Interrupted", state.Outcome);
        Assert.Contains("Interrupted", state.Status, StringComparison.Ordinal);
        Assert.False(state.Active);
        Assert.Contains("parked", state.Inbox);
        Assert.False(state.CanSend);
        Assert.False(state.Stopping);
    }

    [Fact]
    public async Task WrongSessionDataIsRejectedAndNoMutationIsPossible()
    {
        using Api api = new();
        using LiveTaskWorkflow workflow = new(api, api);
        api.Snapshot = api.Snapshot with { Session = api.Session with { Directory = "/wrong" } };
        await workflow.OpenAsync(api.Session);
        Assert.True(workflow.Find("s")!.Stale);
        Assert.False(workflow.Find("s")!.CanSend);
        Assert.Equal(0, api.Submissions);
    }

    [Fact]
    public async Task ChangedCatalogPreventsPromptAndKeepsSessionSelectable()
    {
        using Api api = new();
        using LiveTaskWorkflow workflow = new(api, api);
        await workflow.OpenAsync(api.Session);
        SessionTaskState state = workflow.Find("s")!;
        api.Catalog = new([], []);
        await workflow.SendAsync(state, "keep draft");
        Assert.Equal(0, api.Submissions);
        Assert.Null(state.Submission);
        Assert.False(state.CanSend);
    }

    [Fact]
    public async Task HiddenDefaultsArePreservedAndStaleFormRemainsNonActionable()
    {
        using Api api = new();
        using LiveTaskWorkflow workflow = new(api, api);
        TaskForm form = new("frm_one", "s", "Question", [new("hidden", "string") { Required = true, Hidden = true, Default = "default" }]);
        api.Snapshot = api.Snapshot with { Forms = [form] };
        await workflow.OpenAsync(api.Session);
        SessionTaskState state = workflow.Find("s")!;
        Assert.Equal("default", state.Answers[form.Id]["hidden"]);
        api.Snapshot = api.Snapshot with { Forms = [form with { Fields = form.Fields.ToArray() }] };
        await workflow.RefreshAsync(state);
        await workflow.AnswerAsync(state, form, false);
        Assert.Empty(state.Forms);
        await workflow.AnswerAsync(state, form, false);
        Assert.Equal(1, api.AnswerCount);
    }

    [Fact]
    public async Task BackendReplacementRetainsUncertainInputWithoutAcceptingLateAcknowledgement()
    {
        using Api api = new();
        using LiveTaskWorkflow first = new(api, api);
        await first.OpenAsync(api.Session);
        SessionTaskState original = first.Find("s")!;
        api.PendingSubmit = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Task sending = first.SendAsync(original, "original backend payload");
        using LiveTaskWorkflow replacement = new(api, api);
        replacement.Restore(first.Sessions);
        first.Dispose();
        SessionTaskState retained = replacement.Find("s")!;
        Assert.True(retained.Stale);
        Assert.True(retained.DeliveryUncertain);
        api.PendingSubmit.SetResult();
        await sending;
        Assert.True(retained.DeliveryUncertain);
        Assert.False(retained.CanSend);
        Assert.Equal("original backend payload", retained.Submission!.Text);
        Assert.Equal(1, api.Submissions);
    }

    [Fact]
    public async Task EquivalentRefreshedPermissionCanBeAnsweredButChangedScopeCannot()
    {
        using Api api = new();
        using LiveTaskWorkflow workflow = new(api, api);
        TaskPermission permission = new("p", "s", "read", ["file"], ["*"]);
        api.Snapshot = api.Snapshot with { Permissions = [permission] };
        await workflow.OpenAsync(api.Session);
        SessionTaskState state = workflow.Find("s")!;
        api.Snapshot = api.Snapshot with { Permissions = [permission with { Resources = ["file"], Save = ["*"] }] };
        await workflow.RefreshAsync(state);
        await workflow.PermissionAsync(state, permission, "once");
        Assert.Equal(1, api.PermissionCount);
        api.Snapshot = api.Snapshot with { Permissions = [permission with { Resources = ["different"] }] };
        await workflow.RefreshAsync(state);
        await workflow.PermissionAsync(state, permission, "always");
        Assert.Equal(1, api.PermissionCount);
    }

    private sealed class Api : IWorkspaceConnection, ITaskConnection
    {
        public WorkspaceSession Session { get; } = new("s", "/dir", "project", "Task");
        public TaskCatalog Catalog { get; set; } = new([new("model", "provider", "Model")], [new("build", "Build")]);
        public TaskSnapshot Snapshot { get; set; } = new(new("s", "/dir", "project", "Task"), false, null, null, new("model", "provider", "Model"), "build", [], [], []);
        public IReadOnlyList<HistoryMessage> History { get; set; } = [];
        public TaskCompletionSource? PendingSubmit { get; set; }
        public bool LoseAcknowledgement { get; set; }
        public bool RecoverInOlderHistory { get; set; }
        public int Submissions { get; private set; }
        public int AnswerCount { get; private set; }
        public int PermissionCount { get; private set; }
        public int Watches { get; private set; }
        public int CancelCount { get; private set; }
        public bool RejectAnswer { get; set; }
        public TaskCompletionSource<TaskSnapshot>? HeldSnapshot { get; set; }
        public Channel<TaskEvent> Events { get; } = Channel.CreateUnbounded<TaskEvent>();
        private TaskSubmission? _submitted;
        public Task<IReadOnlyList<string>> AttentionSessionsAsync(string directory, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<string>>([]);
        public Task<TaskCatalog> CatalogAsync(string directory, CancellationToken cancellationToken) => Task.FromResult(Catalog);
        public Task<TaskSnapshot> TaskSnapshotAsync(string id, CancellationToken cancellationToken)
        {
            if (HeldSnapshot is { } held)
            {
                HeldSnapshot = null;
                return held.Task;
            }
            return Task.FromResult(Snapshot);
        }
        public Task SelectModelAsync(string id, TaskModel model, CancellationToken cancellationToken)
        {
            Snapshot = Snapshot with { Model = model };
            return Task.CompletedTask;
        }
        public Task SelectAgentAsync(string id, string agent, CancellationToken cancellationToken)
        {
            Snapshot = Snapshot with { Agent = agent };
            return Task.CompletedTask;
        }
        public async Task SubmitAsync(string id, TaskSubmission submission, CancellationToken cancellationToken)
        {
            Submissions++;
            _submitted = submission;
            if (PendingSubmit is not null)
            {
                await PendingSubmit.Task;
            }

            if (RecoverInOlderHistory)
            {
                Snapshot = Snapshot with { Outcome = "succeeded", Idle = 2 };
                History = [new("idle", "idle", "complete")];
            }
            if (LoseAcknowledgement)
            {
                throw new WorkspaceException(WorkspaceFailure.Transport, "Lost");
            }
        }
        public Task InterruptAsync(string id, CancellationToken cancellationToken)
        {
            Snapshot = Snapshot with { Active = false, Outcome = "interrupted", Idle = 3, Inbox = ["parked"] };
            History = [new("idle", "idle", "interrupted")];
            return Task.CompletedTask;
        }
        public Task ReplyPermissionAsync(string id, string requestId, string decision, CancellationToken cancellationToken)
        {
            PermissionCount++;
            Snapshot = Snapshot with { Permissions = [] };
            return Task.CompletedTask;
        }
        public Task<TaskForm> FormAsync(string id, string formId, CancellationToken cancellationToken) => Task.FromResult(Snapshot.Forms.Single());
        public Task ReplyFormAsync(string id, string formId, IReadOnlyDictionary<string, object> answer, CancellationToken cancellationToken)
        {
            AnswerCount++;
            if (RejectAnswer)
            {
                throw new WorkspaceException(WorkspaceFailure.Http, "Server validation rejected the answer.");
            }

            Snapshot = Snapshot with { Forms = [] };
            return Task.CompletedTask;
        }
        public Task CancelFormAsync(string id, string formId, CancellationToken cancellationToken)
        {
            CancelCount++;
            Snapshot = Snapshot with { Forms = [] };
            return Task.CompletedTask;
        }
        public async Task WatchAsync(Func<TaskEvent, Task> receive, CancellationToken cancellationToken)
        {
            Watches++;
            await receive(new("connected", "server.connected", null, null, 0, null));
            await foreach (TaskEvent e in Events.Reader.ReadAllAsync(cancellationToken))
            {
                if (e.Type == "fixture.disconnect")
                {
                    throw new IOException("Fixture stream closed");
                }

                await receive(e);
            }
        }
        public Task<WorkspacePage<HistoryMessage>> HistoryAsync(string id, string? cursor, CancellationToken cancellationToken) => Task.FromResult(
            cursor is not null && _submitted is not null ? new WorkspacePage<HistoryMessage>([new(_submitted.Id, "user", _submitted.Text)], null) : new(History, RecoverInOlderHistory && _submitted is not null ? "older" : null));
        public Task<ProjectLocation> ResolveAsync(string directory, CancellationToken cancellationToken) => Task.FromResult(new ProjectLocation(directory, "project"));
        public Task ValidateDirectoryAsync(string directory, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<WorkspacePage<WorkspaceSession>> SessionsAsync(string directory, string? cursor, CancellationToken cancellationToken) => Task.FromResult(new WorkspacePage<WorkspaceSession>([Session], null));
        public Task<WorkspaceSession> CreateAsync(string directory, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<WorkspaceSession> SessionAsync(string id, CancellationToken cancellationToken) => Task.FromResult(Session);
        public void Dispose() { }
    }
}
