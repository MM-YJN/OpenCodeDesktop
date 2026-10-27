using System.Threading.Channels;

using OpenCodeDesktop.Core.Connections;
using OpenCodeDesktop.Core.Workspaces;

namespace OpenCodeDesktop.Core.Tests;

public sealed partial class LiveTaskWorkflowTests
{
    [Fact]
    public async Task PartialHistoryDoesNotHideStreamingTextOrDiscardLaterDeltas()
    {
        using Api api = new();
        api.Snapshot = api.Snapshot with { Active = true };
        api.History = [new("message", "assistant", "tool context")
        {
            IsComplete = false,
            Parts = [new(0, "tool context", false), new(1, "", true)],
        }];
        using LiveTaskWorkflow workflow = new(api, api);
        await workflow.OpenAsync(api.Session);
        SessionTaskState state = workflow.Find("s")!;
        int reads = api.SnapshotReads;
        await EmitAsync(new("start", "session.text.started", "s", "message", 1, null), () => true);
        await EmitAsync(new("first", "session.text.delta", "s", "message", 1, "first"), () => state.Conversation.Any(m => m.Text.Contains("first", StringComparison.Ordinal)));
        await EmitAsync(new("hydrate", "session.updated", "s", null, 0, null), () => api.SnapshotReads > reads && !state.Loading);
        Assert.Contains("first", Assert.Single(state.Conversation).Text, StringComparison.Ordinal);
        await EmitAsync(new("second", "session.text.delta", "s", "message", 1, " second"), () => state.Conversation.Any(m => m.Text.Contains("first second", StringComparison.Ordinal)));
        Assert.Equal("tool context\n\nfirst second", Assert.Single(state.Conversation).Text);
        api.Snapshot = api.Snapshot with { Active = false, Idle = 2, Outcome = "succeeded" };
        api.History = [new("idle", "idle", "idle"), new("message", "assistant", "server final")];
        await EmitAsync(new("terminal", "session.execution.succeeded", "s", null, 0, null), () => state.Conversation.Any(m => m.Id == "message" && m.Text == "server final"));
        Assert.Equal("server final", Assert.Single(state.Conversation, m => m.Id == "message").Text);

        async Task EmitAsync(TaskEvent taskEvent, Func<bool> observed)
        {
            TaskCompletionSource signal = new(TaskCreationOptions.RunContinuationsAsynchronously);
            void Changed(object? sender, EventArgs args)
            {
                if (observed())
                {
                    signal.TrySetResult();
                }
            }
            workflow.Changed += Changed;
            try
            {
                await api.Events.Writer.WriteAsync(taskEvent, TestContext.Current.CancellationToken);
                await signal.Task.WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
            }
            finally
            {
                workflow.Changed -= Changed;
            }
        }
    }

    [Theory]
    [InlineData(HistoryFailureOutcome.Interrupted, "Interrupted")]
    [InlineData(HistoryFailureOutcome.Failed, "Failed — review errors and model access")]
    public async Task CompletedAssistantFailureReleasesOwnedSubmissionWithoutNewIdle(HistoryFailureOutcome failure, string outcome)
    {
        using Api api = new();
        api.Snapshot = api.Snapshot with { Idle = 1, Outcome = "succeeded" };
        api.History = [new("old", "idle", "old boundary")];
        using LiveTaskWorkflow workflow = new(api, api);
        await workflow.OpenAsync(api.Session);
        SessionTaskState state = workflow.Find("s")!;
        await workflow.SendAsync(state, "read fixture");
        string input = state.Submission!.Id;
        api.History = [new("failure", "assistant", "completed failure") { CompletedFailure = failure }, new(input, "user", "read fixture"), new("old", "idle", "old boundary")];
        await workflow.RefreshAsync(state);
        Assert.Null(state.Submission);
        Assert.True(state.CanSend);
        Assert.Equal(outcome, state.Status);
        Assert.Equal(1, api.Submissions);
    }

    [Theory]
    [InlineData("older")]
    [InlineData("active")]
    [InlineData("queued")]
    [InlineData("different-input")]
    public async Task UnrelatedOrNonterminalFailureDoesNotReleaseSubmission(string scenario)
    {
        using Api api = new();
        api.Snapshot = api.Snapshot with { Idle = 1, Outcome = "succeeded" };
        api.History = [new("old", "idle", "old boundary")];
        using LiveTaskWorkflow workflow = new(api, api);
        await workflow.OpenAsync(api.Session);
        SessionTaskState state = workflow.Find("s")!;
        await workflow.SendAsync(state, "read fixture");
        string input = state.Submission!.Id;
        HistoryMessage failed = new("failure", "assistant", "completed failure") { CompletedFailure = HistoryFailureOutcome.Interrupted };
        api.History = scenario == "older"
            ? [new(input, "user", "read fixture"), failed, new("old", "idle", "old boundary")]
            : [failed, new(scenario == "different-input" ? "foreign" : input, "user", "read fixture"), new("old", "idle", "old boundary")];
        api.Snapshot = api.Snapshot with { Active = scenario == "active", Inbox = scenario == "queued" ? [input] : [] };
        await workflow.RefreshAsync(state);
        Assert.NotNull(state.Submission);
        Assert.False(state.CanSend);
        Assert.Equal(1, api.Submissions);
    }

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
    public async Task StreamingStaysWithOwningSessionAndDisconnectRetainsLastExecutionState()
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

    [Fact]
    public async Task SingleCatalogAgentIsSelectedWhenTheServerReportsNone()
    {
        using Api api = new();
        api.Snapshot = api.Snapshot with { Agent = null };
        using LiveTaskWorkflow workflow = new(api, api);
        await workflow.OpenAsync(api.Session);
        SessionTaskState state = workflow.Find("s")!;
        Assert.Equal("build", state.Agent);
        Assert.True(state.CanSend);
    }

    [Fact]
    public async Task DraftSendCreatesTheSessionAppliesAndRemembersTheProjectSelection()
    {
        using Api api = new();
        api.Catalog = new([new("model-a", "provider-a", "Model A"), new("model-b", "provider-b", "Model B")], [new("build", "Build"), new("plan", "Plan")]);
        api.Snapshot = api.Snapshot with { Model = new("model-a", "provider-a", "Model A"), Agent = "build" };
        using ServerConnections connections = new(new Service(api));
        ServerProfile profile = connections.Save(null, "Test", "http://127.0.0.1:4096")!;
        await connections.ConnectAsync(profile.Id, "secret");
        using LiveWorkspace workspace = new(connections);
        await workspace.OpenProjectAsync("/dir");

        Assert.Null(workspace.SelectedSessionId);
        Assert.NotNull(workspace.DraftCatalog);
        workspace.Draft = "draft task";
        workspace.RememberModelPreference(new("model-b", "provider-b", "Model B"));
        workspace.RememberAgentPreference("plan");
        await workspace.SendDraftTaskAsync();

        Assert.Equal(1, api.CreateCalls);
        Assert.Equal("created", workspace.SelectedSessionId);
        Assert.Equal(new TaskModel("model-b", "provider-b", "Model B"), api.ModelSelections.Single().Model);
        Assert.Equal("plan", Assert.Single(api.AgentSelections).Agent);
        Assert.Equal(1, api.Submissions);
        Assert.Equal("draft task", workspace.Tasks!.Find("created")!.Submission!.Text);
        Assert.Empty(workspace.Draft);
        SessionTaskState state = workspace.Tasks.Find("created")!;
        Assert.Equal("model-b", state.Model!.Id);
        Assert.Equal("plan", state.Agent);
        Assert.False(state.DeliveryUncertain);
    }

    [Fact]
    public async Task DraftSendWithoutContentOrSelectionOutsideTheCatalogDoesNotCreate()
    {
        using Api api = new();
        using ServerConnections connections = new(new Service(api));
        ServerProfile profile = connections.Save(null, "Test", "http://127.0.0.1:4096")!;
        await connections.ConnectAsync(profile.Id, "secret");
        using LiveWorkspace workspace = new(connections);
        await workspace.OpenProjectAsync("/dir");

        await workspace.SendDraftTaskAsync();
        Assert.Equal(0, api.CreateCalls);

        workspace.Draft = "draft task";
        workspace.RememberModelPreference(new("missing", "missing", "Missing"));
        workspace.RememberAgentPreference("missing");
        await workspace.SendDraftTaskAsync();
        // The session was still created, but the fallback snapshot selection was used.
        Assert.Equal(1, api.CreateCalls);
        Assert.Equal(1, api.Submissions);
        Assert.Equal(new TaskModel("model", "provider", "Model"), api.ModelSelections.Single().Model);
        Assert.Equal("build", Assert.Single(api.AgentSelections).Agent);
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
        PromptAttachment file = PromptAttachment.Local("note.txt", "local", [65]);
        Task sending = first.SendAsync(original, new TaskSubmission("msg_backend_files", "original backend payload") { Files = [file] });
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
        Assert.Equal(file, Assert.Single(retained.Submission.Files));
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

    private sealed class Api : IWorkspaceConnection, ITaskConnection, IProjectFilesConnection
    {
        public bool MissingFile { get; set; }
        public ProjectFilePreview FilePreview { get; set; } = new(FilePreviewKind.Text, "hello", false);
        public Task<ProjectFileList> ListFilesAsync(string directory, string path, CancellationToken cancellationToken) => Task.FromResult(new ProjectFileList(MissingFile ? [] : [new("note.txt", false)], false));
        public Task<ProjectFileList> FindFilesAsync(string directory, string query, CancellationToken cancellationToken) => ListFilesAsync(directory, query, cancellationToken);
        public Task<ProjectFilePreview> ReadFileAsync(string directory, string path, CancellationToken cancellationToken) => Task.FromResult(FilePreview);
        public WorkspaceSession Session { get; } = new("s", "/dir", "project", "Task");
        public TaskCatalog Catalog { get; set; } = new([new("model", "provider", "Model")], [new("build", "Build")]);
        public TaskSnapshot Snapshot { get; set; } = new(new("s", "/dir", "project", "Task"), false, null, null, new("model", "provider", "Model"), "build", [], [], []);
        public IReadOnlyList<HistoryMessage> History { get; set; } = [];
        public TaskCompletionSource? PendingSubmit { get; set; }
        public bool LoseAcknowledgement { get; set; }
        public bool RecoverInOlderHistory { get; set; }
        public int Submissions { get; private set; }
        public int CreateCalls { get; private set; }
        public int AnswerCount { get; private set; }
        public int PermissionCount { get; private set; }
        public int Watches { get; private set; }
        public int CancelCount { get; private set; }
        public bool RejectAnswer { get; set; }
        public List<(string Id, TaskModel Model)> ModelSelections { get; } = [];
        public List<(string Id, string Agent)> AgentSelections { get; } = [];
        public Exception? ReadFailure { get; set; }
        public Exception? StreamFailure { get; set; }
        public Exception? HistoryFailure { get; set; }
        public IReadOnlyList<string> PendingSessions { get; set; } = [];
        public Dictionary<string, TaskSnapshot> OtherSnapshots { get; } = [];
        public int SnapshotReads { get; private set; }
        public TaskCompletionSource<TaskSnapshot>? HeldSnapshot { get; set; }
        public Channel<TaskEvent> Events { get; } = Channel.CreateUnbounded<TaskEvent>();
        private TaskSubmission? _submitted;
        public Task<IReadOnlyList<string>> AttentionSessionsAsync(string directory, CancellationToken cancellationToken) => Task.FromResult(PendingSessions);
        public Task<TaskCatalog> CatalogAsync(string directory, CancellationToken cancellationToken) => Task.FromResult(Catalog);
        public Task<TaskSnapshot> TaskSnapshotAsync(string id, CancellationToken cancellationToken)
        {
            SnapshotReads++;
            if (ReadFailure is { } error)
            {
                return Task.FromException<TaskSnapshot>(error);
            }
            if (HeldSnapshot is { } held)
            {
                HeldSnapshot = null;
                return held.Task;
            }
            return Task.FromResult(OtherSnapshots.GetValueOrDefault(id) ?? Snapshot);
        }
        public Task SelectModelAsync(string id, TaskModel model, CancellationToken cancellationToken)
        {
            ModelSelections.Add((id, model));
            if (OtherSnapshots.TryGetValue(id, out TaskSnapshot? snapshot))
            {
                OtherSnapshots[id] = snapshot with { Model = model };
            }
            else
            {
                Snapshot = Snapshot with { Model = model };
            }
            return Task.CompletedTask;
        }
        public Task SelectAgentAsync(string id, string agent, CancellationToken cancellationToken)
        {
            AgentSelections.Add((id, agent));
            if (OtherSnapshots.TryGetValue(id, out TaskSnapshot? snapshot))
            {
                OtherSnapshots[id] = snapshot with { Agent = agent };
            }
            else
            {
                Snapshot = Snapshot with { Agent = agent };
            }
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
            if (StreamFailure is { } error)
            {
                throw error;
            }
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
        public Task<WorkspacePage<HistoryMessage>> HistoryAsync(string id, string? cursor, CancellationToken cancellationToken) => HistoryFailure is { } error ? Task.FromException<WorkspacePage<HistoryMessage>>(error) : Task.FromResult(
            cursor is not null && _submitted is not null ? new WorkspacePage<HistoryMessage>([new(_submitted.Id, "user", _submitted.Text)], null) : new(History, RecoverInOlderHistory && _submitted is not null ? "older" : null));
        public Task<ProjectLocation> ResolveAsync(string directory, CancellationToken cancellationToken) => Task.FromResult(new ProjectLocation(directory, "project"));
        public Task ValidateDirectoryAsync(string directory, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<WorkspacePage<WorkspaceSession>> SessionsAsync(string directory, string? cursor, CancellationToken cancellationToken) => Task.FromResult(new WorkspacePage<WorkspaceSession>([Session], null));
        public Task<WorkspaceSession> CreateAsync(string directory, CancellationToken cancellationToken)
        {
            CreateCalls++;
            WorkspaceSession created = new("created", directory, "project", "Created");
            OtherSnapshots[created.Id] = Snapshot with { Session = created };
            return Task.FromResult(created);
        }
        public Task<WorkspaceSession> SessionAsync(string id, CancellationToken cancellationToken) => Task.FromResult(OtherSnapshots.GetValueOrDefault(id)?.Session ?? Session);
        public void Dispose() { }
    }
}
