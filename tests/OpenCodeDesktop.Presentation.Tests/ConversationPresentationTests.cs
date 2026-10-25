using OpenCodeDesktop.Core.Workspaces;
using OpenCodeDesktop.Presentation;

namespace OpenCodeDesktop.Presentation.Tests;

public sealed class ConversationPresentationTests
{
    [Fact]
    public void UserAndAssistantTextKeepCodeFencesSelectableAndStructured()
    {
        ConversationEntry entry = ConversationPresentation.From(new HistoryMessage("1", "user",
            "Run this:\n```csharp\nvar value = 1;\n```\nThen report back."));

        Assert.Equal(ConversationRole.User, entry.Role);
        Assert.Equal("You", entry.Title);
        Assert.Equal(3, entry.Segments.Count);
        Assert.False(entry.Segments[0].IsCode);
        Assert.True(entry.Segments[1].IsCode);
        Assert.Contains("var value", entry.Segments[1].Text, StringComparison.Ordinal);
        Assert.StartsWith("You said:", entry.AutomationName, StringComparison.Ordinal);
    }

    [Fact]
    public void ToolActivityIsStructuredAndAnsiControlSequencesAreRemoved()
    {
        ConversationEntry entry = ConversationPresentation.From(new HistoryMessage("2", "tool activity",
            "\u001b[31mred\u001b[0m and [1;32mgreen[0m"));

        Assert.Equal(ConversationRole.Tool, entry.Role);
        ConversationSegment segment = Assert.Single(entry.Segments);
        Assert.True(segment.IsTool);
        Assert.Equal("red and green", segment.Text);
    }

    [Fact]
    public void RecordedToolTextBecomesAComposableToolSegment()
    {
        ConversationEntry entry = ConversationPresentation.From(new HistoryMessage("3", "assistant",
            "Intro\n\nRecorded tool: shell\nRecorded status: completed\nDone"));

        ConversationSegment tool = Assert.Single(entry.Segments, segment => segment.IsTool);
        Assert.Equal("shell", tool.ToolName);
        Assert.Contains("completed", tool.Text, StringComparison.Ordinal);
        Assert.Contains(entry.Segments, segment => !segment.IsCode && segment.Text == "Intro");
    }

    [Fact]
    public void UnknownHistoryVariantsRemainTruthfulActivityInsteadOfErrors()
    {
        ConversationEntry entry = ConversationPresentation.From(new HistoryMessage("4", "future-event", "Unsupported historical entry."));

        Assert.Equal(ConversationRole.Activity, entry.Role);
        Assert.Equal("Unsupported entry", entry.Title);
        Assert.False(entry.IsWarning);
    }

    [Fact]
    public void DeliveryUncertaintyIsVisibleWithoutChangingTheStableMessageKey()
    {
        ConversationEntry entry = ConversationPresentation.From(new HistoryMessage("5", "user · delivery uncertain", "retained prompt"));

        Assert.Equal("5", entry.Key);
        Assert.Equal(ConversationRole.User, entry.Role);
        Assert.True(entry.IsWarning);
        Assert.Contains("delivery uncertain", entry.Title, StringComparison.Ordinal);
    }
}
