using OpenCodeDesktop.Core.Workspaces;

namespace OpenCodeDesktop.Core.Tests;

public sealed class ChangeReviewTests
{
    [Fact]
    public async Task LateComparisonsCannotReplaceNewBasesOrOwners()
    {
        using ChangeReviewWorkspace workspace = new();
        PendingConnection connection = new();
        Guid id = Guid.NewGuid();
        workspace.SetConnection(id, connection);
        workspace.Open(new("/a", "project"), "session-a");
        Task first = workspace.LoadAsync(new(ReviewMode.Working));
        Task second = workspace.LoadAsync(new(ReviewMode.Branch, "main"));
        connection.Pending[1].SetResult(Result("main"));
        await second;
        connection.Pending[0].SetResult(Result("old"));
        await first;
        Assert.Equal("main", workspace.Current!.Result!.Basis);
        Task third = workspace.LoadAsync(new(ReviewMode.Branch, "other"));
        workspace.Open(new("/b", "project"), "session-b");
        connection.Pending[2].SetResult(Result("wrong-owner"));
        await third;
        Assert.Null(workspace.Current!.Result);
        Assert.Equal("session-b", workspace.Current.SessionId);
    }

    [Fact]
    public async Task SelectionAndReadingPositionBelongToSessionAndComparison()
    {
        using ChangeReviewWorkspace workspace = new();
        PendingConnection connection = new();
        Guid id = Guid.NewGuid();
        workspace.SetConnection(id, connection);
        ProjectLocation location = new("/a", "p");
        workspace.Open(location, "a");
        await Complete(new(ReviewMode.Working));
        ChangeReviewState state = workspace.Current!;
        state.SelectedPath = "second"; state.PatchOffset = 200; state.HorizontalOffset = 10; state.ListOffset = 120;
        await Complete(new(ReviewMode.Turns, From: "user-1", To: "user-2"));
        Assert.Equal(0, state.PatchOffset);
        state.PatchOffset = 80;
        await Complete(new(ReviewMode.Working));
        Assert.Equal("second", state.SelectedPath);
        Assert.Equal(200, state.PatchOffset);
        Assert.Equal(10, state.HorizontalOffset);
        Assert.Equal(120, state.ListOffset);
        workspace.Open(location, "b");
        Assert.Null(workspace.Current!.Result);
        workspace.Open(location, "a");
        Assert.Same(state, workspace.Current);
        workspace.SetConnection(Guid.NewGuid(), connection);
        workspace.Open(location, "a");
        Assert.NotSame(state, workspace.Current);

        async Task Complete(ReviewComparison comparison)
        {
            Task load = workspace.LoadAsync(comparison);
            connection.Pending[^1].SetResult(new("basis", [new("first", "added", 1, 0, "+a", false), new("second", "modified", 1, 1, "-b\n+c", false)], false));
            await load;
        }
    }

    [Fact]
    public async Task MissingTurnDoesNotReachTransportAndFailuresUseSafeText()
    {
        using ChangeReviewWorkspace workspace = new();
        PendingConnection connection = new();
        workspace.SetConnection(Guid.NewGuid(), connection);
        workspace.Open(new("/a", "p"), "a");
        await workspace.LoadAsync(new(ReviewMode.Turns));
        Assert.Empty(connection.Pending);
        Assert.Contains("Choose", workspace.Current!.Error, StringComparison.Ordinal);
        Task failed = workspace.LoadAsync(new(ReviewMode.Working));
        connection.Pending[0].SetException(new WorkspaceException(WorkspaceFailure.Size, "private server error"));
        await failed;
        Assert.Contains("4 MiB", workspace.Current.Error, StringComparison.Ordinal);
        Assert.DoesNotContain("private", workspace.Current.Error, StringComparison.Ordinal);
        Assert.False(workspace.Current.Loading);
    }

    [Fact]
    public async Task ReconnectRejectsOldResponsesEvenForSameProfileAndLocation()
    {
        using ChangeReviewWorkspace workspace = new();
        PendingConnection connection = new();
        Guid id = Guid.NewGuid();
        workspace.SetConnection(id, connection);
        workspace.Open(new("/a", "p"), "a");
        Task load = workspace.LoadAsync(new(ReviewMode.Working));
        workspace.SetConnection(null, null);
        connection.Pending[0].SetResult(Result("old"));
        await load;
        Assert.Null(workspace.Current);
        workspace.ForgetConnection(id);
        workspace.SetConnection(id, connection);
        workspace.Open(new("/a", "p"), "a");
        Assert.Null(workspace.Current!.Result);
    }

    private static ReviewResult Result(string basis) => new(basis, [], false);
    private sealed class PendingConnection : IChangeReviewConnection
    {
        public List<TaskCompletionSource<ReviewResult>> Pending { get; } = [];
        public Task<ReviewResult> ReviewAsync(ProjectLocation location, string? sessionId, ReviewComparison comparison, CancellationToken cancellationToken)
        {
            TaskCompletionSource<ReviewResult> pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
            Pending.Add(pending);
            return pending.Task;
        }
    }
}
