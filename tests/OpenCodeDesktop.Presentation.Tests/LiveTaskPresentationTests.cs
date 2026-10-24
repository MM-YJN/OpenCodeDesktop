using OpenCodeDesktop.Core.Workspaces;
using OpenCodeDesktop.Presentation;

namespace OpenCodeDesktop.Presentation.Tests;

public sealed class LiveTaskPresentationTests
{
    [Fact]
    public async Task SendingLoadingRunningAndDisconnectedControlsStayDistinct()
    {
        using Api api = new();
        using LiveTaskWorkflow workflow = new(api, api);
        Task loading = workflow.OpenAsync(api.Session);
        SessionTaskState state = workflow.Find("s")!;
        var presentation = LiveTaskPresentation.From(state, "draft", null);
        Assert.True(presentation.IsBusy);
        Assert.False(presentation.CanSend);
        api.Catalog.SetResult(new([new("m", "p", "Model")], [new("build", "Build")]));
        await loading;
        Assert.True(LiveTaskPresentation.From(state, "draft", null).CanSend);
        Assert.False(LiveTaskPresentation.From(state, "  ", null).CanSend);
        Task sending = workflow.SendAsync(state, "draft");
        presentation = LiveTaskPresentation.From(state, "edited draft", null);
        Assert.True(presentation.IsBusy);
        Assert.False(presentation.CanSend);
        Assert.False(presentation.CanSelect);
        api.Submitted.SetResult();
        await sending;
        Assert.Contains("queued", LiveTaskPresentation.From(state, "edited draft", null).Status, StringComparison.Ordinal);
        api.Snapshot = api.Snapshot with { Active = true, Outcome = "failed", Idle = 1 };
        await workflow.RefreshAsync(state);
        presentation = LiveTaskPresentation.From(state, "edited draft", null);
        Assert.Equal("Running", presentation.Status);
        Assert.True(presentation.CanStop);
        Assert.False(presentation.CanSend);
        Assert.False(presentation.CanSelect);
        presentation = LiveTaskPresentation.From(null, "draft", "Attention unavailable");
        Assert.False(presentation.CanSend);
        Assert.False(presentation.CanStop);
        Assert.Equal("Attention unavailable", presentation.Error);
    }

    private sealed class Api : IWorkspaceConnection, ITaskConnection
    {
        public WorkspaceSession Session { get; } = new("s", "/dir", "project", "Task");
        public TaskSnapshot Snapshot { get; set; } = new(new("s", "/dir", "project", "Task"), false, null, null, new("m", "p", "Model"), "build", [], [], []);
        public TaskCompletionSource<TaskCatalog> Catalog { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Submitted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<TaskCatalog> CatalogAsync(string directory, CancellationToken cancellationToken) => Catalog.Task;
        public Task<TaskSnapshot> TaskSnapshotAsync(string id, CancellationToken cancellationToken) => Task.FromResult(Snapshot);
        public Task SelectModelAsync(string id, TaskModel model, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task SelectAgentAsync(string id, string agent, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task SubmitAsync(string id, TaskSubmission submission, CancellationToken cancellationToken) => Submitted.Task;
        public Task<WorkspacePage<HistoryMessage>> HistoryAsync(string id, string? cursor, CancellationToken cancellationToken) => Task.FromResult(new WorkspacePage<HistoryMessage>([], null));
        public async Task WatchAsync(Func<TaskEvent, Task> receive, CancellationToken cancellationToken)
        {
            await receive(new("connected", "server.connected", null, null, 0, null));
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }
        public Task<IReadOnlyList<string>> AttentionSessionsAsync(string directory, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task InterruptAsync(string id, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task ReplyPermissionAsync(string id, string requestId, string decision, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<TaskForm> FormAsync(string id, string formId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task ReplyFormAsync(string id, string formId, IReadOnlyDictionary<string, object> answer, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task CancelFormAsync(string id, string formId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ProjectLocation> ResolveAsync(string directory, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task ValidateDirectoryAsync(string directory, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<WorkspacePage<WorkspaceSession>> SessionsAsync(string directory, string? cursor, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<WorkspaceSession> CreateAsync(string directory, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<WorkspaceSession> SessionAsync(string id, CancellationToken cancellationToken) => Task.FromResult(Session);
        public void Dispose() { }
    }
}
