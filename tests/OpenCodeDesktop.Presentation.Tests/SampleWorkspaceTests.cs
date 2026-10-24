using OpenCodeDesktop.Presentation;

namespace OpenCodeDesktop.Presentation.Tests;

public sealed class SampleWorkspaceTests
{
    [Fact]
    public void SessionNavigationPreservesIndependentDraftsAndDetails()
    {
        SampleWorkspace workspace = new();
        Assert.NotNull(workspace.Selected);
        SampleSession first = workspace.Selected!;
        first.Draft = "Keep this draft";
        first.DetailsTab = 2;
        first.SelectedChange = "src/RecipeFilter.cs";
        first.DetailsOffsets[2] = 180;
        workspace.Selected = workspace.Sessions.ElementAt(1);
        workspace.Selected.Draft = "Different draft";
        workspace.Selected = first;
        Assert.Equal("Keep this draft", workspace.Selected.Draft);
        Assert.Equal(2, workspace.Selected.DetailsTab);
        Assert.Equal("src/RecipeFilter.cs", workspace.Selected.SelectedChange);
        Assert.Equal(180, workspace.Selected.DetailsOffsets[2]);
    }

    [Fact]
    public void ResponsesAffectOnlyOwningSessionAndNewWorkspaceRestoresRequests()
    {
        SampleWorkspace workspace = new();
        SampleSession permission = workspace.Selected!;
        SampleSession question = workspace.Sessions.Single(session => session.QuestionPending);
        Assert.Equal(2, workspace.AttentionCount);
        Assert.False(question.RespondQuestion(false));
        Assert.Equal(2, workspace.AttentionCount);
        permission.RespondPermission("Rejected");
        Assert.Equal(1, workspace.AttentionCount);
        Assert.Same(question, Assert.Single(workspace.Attention));
        question.Answer = "Use metric quantities";
        Assert.True(question.RespondQuestion(false));
        Assert.Empty(workspace.Attention);
        SampleWorkspace nextLaunch = new();
        Assert.Equal(2, nextLaunch.AttentionCount);
        Assert.NotSame(permission, nextLaunch.Selected);
        Assert.Empty(nextLaunch.Selected!.Draft);
        Assert.Empty(workspace.Attention);
    }

    [Fact]
    public void ForkAndDeleteKeepCollectionsAndDraftsIndependent()
    {
        SampleWorkspace workspace = new();
        SampleSession source = workspace.Selected!;
        source.Draft = "Original";
        source.Attachments.Add("docs/notes.md");
        SampleSession fork = workspace.Fork(source);
        fork.Draft = "Fork only";
        fork.Attachments.Clear();
        Assert.Equal("Original", source.Draft);
        Assert.Single(source.Attachments);
        Assert.False(fork.PermissionPending);
        workspace.Delete(source);
        Assert.Null(workspace.Owner(source));
        Assert.Equal(1, workspace.AttentionCount);
        workspace.Delete(fork);
        Assert.NotSame(fork, workspace.Selected);
    }

    [Fact]
    public void StartupSelectsFictionalSampleDataWithMatchingFileReferences()
    {
        SampleWorkspace workspace = new();
        Assert.Equal(2, workspace.Projects.Count);
        Assert.Equal(6, workspace.Sessions.Count());
        Assert.Equal("Cinder Orchard", workspace.Owner(workspace.Selected!)!.Name);
        Assert.Equal("Add recipe tags", workspace.Selected!.Title);
        Assert.Contains(workspace.Selected.SelectedFile, SampleFixtures.Files.Keys);
        Assert.All(workspace.Sessions, session => Assert.Contains(session.SelectedFile, SampleFixtures.Files.Keys));
    }

    [Fact]
    public void SubmissionSeparatesLocalMessagesFromPendingInputs()
    {
        SampleSession session = new("Sample", "Running");
        Assert.False(session.Submit("Append locally"));
        session.Draft = "Queue me";
        session.Attachments.Add("docs/notes.md");
        Assert.True(session.Submit("Queue"));
        Assert.Empty(session.Messages);
        Assert.Equal("Queue", Assert.Single(session.PendingInputs).Mode);
        Assert.Contains("docs/notes.md", session.PendingInputs[0].Text, StringComparison.Ordinal);
        Assert.Empty(session.Draft);
        Assert.Empty(session.Attachments);
        session.Draft = "Local message";
        Assert.True(session.Submit("Append locally"));
        Assert.Contains("simulated local message", Assert.Single(session.Messages), StringComparison.Ordinal);
        Assert.Equal("Running", session.Execution);
    }
}
