using OpenCodeDesktop.Core.Workspaces;
using OpenCodeDesktop.Presentation;

namespace OpenCodeDesktop.Presentation.Tests;

public sealed class ConversationPresentationTests
{
    [Fact]
    public void UserTextKeepsCodeFencesSelectableAndStructured()
    {
        ConversationEntry entry = ConversationPresentation.From(new HistoryMessage("1", "user",
            "Run this:\n```csharp\nvar value = 1;\n```\nThen report back."))!;

        Assert.Equal(ConversationRole.User, entry.Role);
        Assert.Equal(3, entry.Segments.Count);
        Assert.Equal(ConversationSegmentKind.Text, entry.Segments[0].Kind);
        Assert.Equal(ConversationSegmentKind.Code, entry.Segments[1].Kind);
        Assert.Contains("var value", entry.Segments[1].Text, StringComparison.Ordinal);
        Assert.StartsWith("You said:", entry.AutomationName, StringComparison.Ordinal);
    }

    [Fact]
    public void AssistantPartsBecomeTypedThinkingToolAndTextSegments()
    {
        ConversationEntry entry = ConversationPresentation.From(new HistoryMessage("2", "assistant", "unused")
        {
            Parts =
            [
                new HistoryContentPart(0, "consider the options", HistoryPartKind.Reasoning),
                new HistoryContentPart(1, "answer prefix", HistoryPartKind.Text),
                new HistoryContentPart(2, "passed", HistoryPartKind.Tool) { ToolName = "shell", ToolInput = "dotnet test", ToolStatus = "completed" },
            ],
        })!;

        Assert.Equal(ConversationRole.Assistant, entry.Role);
        Assert.Equal(ConversationSegmentKind.Reasoning, entry.Segments[0].Kind);
        Assert.Equal("consider the options", entry.Segments[0].Text);
        Assert.Equal(ConversationSegmentKind.Text, entry.Segments[1].Kind);
        ConversationSegment tool = Assert.Single(entry.Segments, segment => segment.Kind == ConversationSegmentKind.Tool);
        Assert.Equal("shell", tool.ToolName);
        Assert.Equal("dotnet test", tool.ToolInput);
        Assert.Equal("completed", tool.ToolStatus);
        Assert.StartsWith("OpenCode said:", entry.AutomationName, StringComparison.Ordinal);
    }

    [Fact]
    public void LiveToolActivityIsStructuredAndAnsiControlSequencesAreRemoved()
    {
        ConversationEntry entry = ConversationPresentation.From(new HistoryMessage("3", "tool activity",
            "[31mbash[0m [1;32m·[0m called"))!;

        Assert.Equal(ConversationRole.Tool, entry.Role);
        ConversationSegment segment = Assert.Single(entry.Segments);
        Assert.Equal(ConversationSegmentKind.Tool, segment.Kind);
        Assert.Equal("bash", segment.ToolName);
        Assert.Equal("called", segment.ToolStatus);
    }

    [Fact]
    public void RecordedShellCommandRendersAsAToolCall()
    {
        ConversationEntry entry = ConversationPresentation.From(new HistoryMessage("4", "shell", "output text")
        {
            Parts = [new HistoryContentPart(0, "output text", HistoryPartKind.Tool) { ToolName = "shell", ToolInput = "dotnet build", ToolStatus = "completed" }],
        })!;

        Assert.Equal(ConversationRole.Tool, entry.Role);
        ConversationSegment segment = Assert.Single(entry.Segments);
        Assert.Equal(ConversationSegmentKind.Tool, segment.Kind);
        Assert.Equal("shell", segment.ToolName);
        Assert.Equal("dotnet build", segment.ToolInput);
        Assert.Equal("completed", segment.ToolStatus);
    }

    [Fact]
    public void LifecycleEventsBecomeSingleLineDividers()
    {
        IReadOnlyList<ConversationEntry> entries = ConversationPresentation.From(
        [
            new HistoryMessage("1", "user", "prompt"),
            new HistoryMessage("2", "compaction", "Context compacted · completed"),
            new HistoryMessage("3", "agent-switched", "Agent changed: build"),
            new HistoryMessage("4", "assistant", "answer"),
        ]);

        Assert.Equal(4, entries.Count);
        Assert.Equal(ConversationRole.Divider, entries[1].Role);
        Assert.Equal("Context compacted · completed", entries[1].Title);
        Assert.Equal(ConversationRole.Divider, entries[2].Role);
        Assert.Equal("Agent changed: build", entries[2].Title);
    }

    [Fact]
    public void IdleSystemAndSyntheticEntriesStayOutOfTheConversation()
    {
        IReadOnlyList<ConversationEntry> entries = ConversationPresentation.From(
        [
            new HistoryMessage("1", "user", "prompt"),
            new HistoryMessage("2", "idle", "Recorded idle activity."),
            new HistoryMessage("3", "system", "system payload"),
            new HistoryMessage("4", "synthetic", "synthetic payload"),
            new HistoryMessage("5", "assistant", "answer"),
        ]);

        Assert.Equal(2, entries.Count);
        Assert.Equal(ConversationRole.User, entries[0].Role);
        Assert.Equal(ConversationRole.Assistant, entries[1].Role);
    }

    [Fact]
    public void UnknownHistoryVariantsRemainTruthfulDividersInsteadOfErrors()
    {
        ConversationEntry entry = ConversationPresentation.From(new HistoryMessage("6", "future-event", "Unsupported historical entry."))!;

        Assert.Equal(ConversationRole.Divider, entry.Role);
        Assert.Equal("Unsupported entry · future-event", entry.Title);
        Assert.False(entry.IsWarning);
    }

    [Fact]
    public void DeliveryUncertaintyIsACaptionWithoutChangingTheStableMessageKey()
    {
        ConversationEntry entry = ConversationPresentation.From(new HistoryMessage("7", "user · delivery uncertain", "retained prompt"))!;

        Assert.Equal("7", entry.Key);
        Assert.Equal(ConversationRole.User, entry.Role);
        Assert.True(entry.IsWarning);
        Assert.Contains("Delivery uncertain", entry.Caption, StringComparison.Ordinal);
        Assert.Contains("Delivery uncertain", entry.AutomationName, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("completed", "completed")]
    [InlineData("error", "failed")]
    [InlineData("running", "running")]
    [InlineData("input.started", "running")]
    [InlineData("called", "running")]
    [InlineData("stopped", "stopped")]
    public void ToolStatusesNormalizeForDisplay(string? status, string? expected) =>
        Assert.Equal(expected, ConversationPresentation.ToolStatusText(status));
}
