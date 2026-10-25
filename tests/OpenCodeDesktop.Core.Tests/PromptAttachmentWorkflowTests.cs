using OpenCodeDesktop.Core.Connections;
using OpenCodeDesktop.Core.Workspaces;

namespace OpenCodeDesktop.Core.Tests;

public sealed partial class LiveTaskWorkflowTests
{
    [Fact]
    public async Task LateAdmissionConsumesOnlyOriginalDraftAndNeverResendsAttachments()
    {
        using Api api = new() { LoseAcknowledgement = true };
        using ServerConnections connections = new(new Service(api));
        ServerProfile profile = connections.Save(null, "Test", "http://127.0.0.1:4096")!;
        await connections.ConnectAsync(profile.Id, "secret");
        using LiveWorkspace workspace = new(connections);
        await workspace.OpenProjectAsync("/dir");
        await workspace.SelectSessionAsync("s");
        PromptAttachment file = PromptAttachment.Local("local.txt", "local file", [65]);
        workspace.CurrentDraft!.Add(file);
        workspace.Draft = "request";
        await workspace.SendTaskAsync();
        SessionTaskState state = workspace.Tasks!.Find("s")!;
        TaskSubmission submitted = state.Submission!;
        Assert.True(state.DeliveryUncertain);
        Assert.Equal(file, Assert.Single(submitted.Files));
        Assert.Equal(file, Assert.Single(workspace.CurrentDraft.Files));
        api.Snapshot = api.Snapshot with { Inbox = [submitted.Id] };
        await workspace.Tasks.RefreshAsync(state);
        Assert.Empty(workspace.Draft);
        Assert.Empty(workspace.CurrentDraft.Files);
        Assert.Equal(file, Assert.Single(state.Submission!.Files));
        Assert.Equal(1, api.Submissions);
        Assert.Single(state.Conversation.SelectMany(m => m.FileReferences));
        await workspace.Tasks.RefreshAsync(state);
        Assert.Single(state.Conversation.SelectMany(m => m.FileReferences));
    }

    [Fact]
    public async Task EditsAndAttachmentRemovalDuringSendPreserveLaterVersion()
    {
        using Api api = new() { PendingSubmit = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        using ServerConnections connections = new(new Service(api));
        ServerProfile profile = connections.Save(null, "Test", "http://127.0.0.1:4096")!;
        await connections.ConnectAsync(profile.Id, "secret");
        using LiveWorkspace workspace = new(connections);
        await workspace.OpenProjectAsync("/dir");
        await workspace.SelectSessionAsync("s");
        PromptAttachment file = PromptAttachment.Local("local.txt", "local file", [65]);
        workspace.CurrentDraft!.Add(file);
        workspace.Draft = "request";
        Task sending = workspace.SendTaskAsync();
        workspace.Draft = "edit";
        workspace.Draft = "request";
        workspace.RemoveAttachment(file);
        api.PendingSubmit.SetResult();
        await sending;
        Assert.Equal("request", workspace.Draft);
        Assert.Empty(workspace.CurrentDraft.Files);
        Assert.Equal(file, Assert.Single(workspace.Tasks!.Find("s")!.Submission!.Files));
    }

    [Fact]
    public async Task PickerReadFinishingAfterNavigationDoesNotAttachToAnotherDraft()
    {
        using Api api = new();
        using ServerConnections connections = new(new Service(api));
        ServerProfile profile = connections.Save(null, "Test", "http://127.0.0.1:4096")!;
        await connections.ConnectAsync(profile.Id, "secret");
        using LiveWorkspace workspace = new(connections);
        await workspace.OpenProjectAsync("/dir");
        await workspace.SelectSessionAsync("s");
        PromptDraft original = workspace.CurrentDraft!;
        HeldFileReader reader = new();
        Task adding = workspace.AddLocalAttachmentAsync(reader, "local", TestContext.Current.CancellationToken);
        await workspace.OpenProjectDraftAsync("/dir");
        reader.Completion.SetResult(PromptAttachment.Local("local.txt", "local file", [65]));
        await adding;
        Assert.Empty(original.Files);
        Assert.Empty(workspace.CurrentDraft!.Files);
        workspace.CurrentDraft.Add(PromptAttachment.Local("draft.txt", "local", [66]));
        await workspace.SelectSessionAsync("s");
        Assert.Empty(workspace.CurrentDraft!.Files);
        await workspace.OpenProjectDraftAsync("/dir");
        Assert.Single(workspace.CurrentDraft!.Files);
        await workspace.CreateSessionAsync();
        Assert.Single(workspace.CurrentDraft!.Files);
    }

    [Fact]
    public async Task MissingOrChangedServerFilePreflightDoesNotSend()
    {
        using Api api = new() { MissingFile = true };
        using LiveTaskWorkflow workflow = new(api, api);
        await workflow.OpenAsync(api.Session);
        SessionTaskState state = workflow.Find("s")!;
        PromptAttachment file = PromptAttachment.Server("/dir", "note.txt", new(FilePreviewKind.Text, "hello", false));
        await workflow.SendAsync(state, new TaskSubmission("msg_file", "text") { Files = [file] });
        Assert.Equal(0, api.Submissions);
        Assert.Contains("missing", state.Error!, StringComparison.Ordinal);
        api.MissingFile = false;
        api.FilePreview = new(FilePreviewKind.Text, "changed too large", true);
        await workflow.RefreshAsync(state);
        await workflow.SendAsync(state, new TaskSubmission("msg_file2", "text") { Files = [file] });
        Assert.Equal(0, api.Submissions);
        Assert.Contains("complete preview", state.Error!, StringComparison.Ordinal);
    }

    private sealed class HeldFileReader : ILocalPromptFileReader
    {
        public TaskCompletionSource<PromptAttachment> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<PromptAttachment> ReadAsync(string path, CancellationToken cancellationToken) => Completion.Task;
    }
}
