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
        ConversationSegment activity = Assert.Single(entry.Segments, segment => segment.Kind == ConversationSegmentKind.Activity);
        ConversationSegment tool = Assert.Single(activity.Items);
        Assert.Equal(ConversationSegmentKind.Tool, tool.Kind);
        Assert.Equal("shell", tool.ToolName);
        Assert.Equal("dotnet test", tool.ToolInput);
        Assert.Equal("completed", tool.ToolStatus);
        Assert.StartsWith("OpenCode said:", entry.AutomationName, StringComparison.Ordinal);
    }

    [Fact]
    public void ConsecutiveThinkingAndToolPartsCollapseIntoOneActivityBlock()
    {
        ConversationEntry entry = ConversationPresentation.From(new HistoryMessage("8", "assistant", "unused")
        {
            Parts =
            [
                new HistoryContentPart(0, "text before", HistoryPartKind.Text),
                new HistoryContentPart(1, "first thought", HistoryPartKind.Reasoning),
                new HistoryContentPart(2, "ran build", HistoryPartKind.Tool) { ToolName = "shell", ToolStatus = "completed" },
                new HistoryContentPart(3, "second thought", HistoryPartKind.Reasoning),
                new HistoryContentPart(4, "text after", HistoryPartKind.Text),
            ],
        })!;

        Assert.Equal(3, entry.Segments.Count);
        Assert.Equal(ConversationSegmentKind.Text, entry.Segments[0].Kind);
        ConversationSegment activity = entry.Segments[1];
        Assert.Equal(ConversationSegmentKind.Activity, activity.Kind);
        Assert.Equal(3, activity.Items.Count);
        Assert.Equal(ConversationSegmentKind.Reasoning, activity.Items[0].Kind);
        Assert.Equal(ConversationSegmentKind.Tool, activity.Items[1].Kind);
        Assert.Equal(ConversationSegmentKind.Reasoning, activity.Items[2].Kind);
        Assert.Equal("1 tool call", ConversationPresentation.ActivityTitle(activity));
        Assert.Equal(ConversationSegmentKind.Text, entry.Segments[2].Kind);
    }

    [Fact]
    public void LoneThinkingPartKeepsThePlainReasoningBlock()
    {
        ConversationEntry entry = ConversationPresentation.From(new HistoryMessage("9", "assistant", "unused")
        {
            Parts =
            [
                new HistoryContentPart(0, "only a thought", HistoryPartKind.Reasoning),
                new HistoryContentPart(1, "answer", HistoryPartKind.Text),
            ],
        })!;

        Assert.Equal(ConversationSegmentKind.Reasoning, entry.Segments[0].Kind);
        Assert.DoesNotContain(entry.Segments, segment => segment.Kind == ConversationSegmentKind.Activity);
    }

    [Fact]
    public void LiveToolActivityFoldsIntoTheAssistantEntryTrailingActivityBlock()
    {
        IReadOnlyList<ConversationEntry> entries = ConversationPresentation.From(
        [
            new HistoryMessage("m1", "assistant", "working on it"),
            new HistoryMessage("m1:t1", "tool activity", "[31mbash[0m · called"),
            new HistoryMessage("m1:t2", "tool activity", "read · progress"),
        ]);

        ConversationEntry entry = Assert.Single(entries);
        Assert.Equal(ConversationRole.Assistant, entry.Role);
        Assert.Equal(ConversationSegmentKind.Text, entry.Segments[0].Kind);
        ConversationSegment activity = Assert.Single(entry.Segments, segment => segment.Kind == ConversationSegmentKind.Activity);
        Assert.Equal(2, activity.Items.Count);
        Assert.All(activity.Items, item => Assert.Equal(ConversationSegmentKind.Tool, item.Kind));
        Assert.True(ConversationPresentation.ActivityRunning(activity));
        Assert.False(ConversationPresentation.ActivityFailed(activity));
        Assert.Equal("read", ConversationPresentation.ActivityRunningTool(activity));
        Assert.Equal("2 tool calls · running", ConversationPresentation.ActivityTitle(activity));
    }

    [Fact]
    public void LiveToolActivityWithoutAnAssistantMessageFormsOneStableGroupedEntry()
    {
        IReadOnlyList<ConversationEntry> entries = ConversationPresentation.From(
        [
            new HistoryMessage("m2:t1", "tool activity", "bash · called"),
            new HistoryMessage("m2:t2", "tool activity", "read · input.started"),
        ]);

        ConversationEntry entry = Assert.Single(entries);
        Assert.Equal("m2:activity", entry.Key);
        Assert.Equal(ConversationRole.Tool, entry.Role);
        ConversationSegment activity = Assert.Single(entry.Segments);
        Assert.Equal(ConversationSegmentKind.Activity, activity.Kind);
        Assert.Equal(2, activity.Items.Count);
    }

    [Fact]
    public void EmptyTextPartsDoNotSplitAnActivityGroup()
    {
        ConversationEntry entry = ConversationPresentation.From(new HistoryMessage("11", "assistant", "unused")
        {
            Parts =
            [
                new HistoryContentPart(0, "first thought", HistoryPartKind.Reasoning),
                new HistoryContentPart(1, "ran build", HistoryPartKind.Tool) { ToolName = "shell", ToolStatus = "completed" },
                new HistoryContentPart(2, "", HistoryPartKind.Text),
                new HistoryContentPart(3, "ran tests", HistoryPartKind.Tool) { ToolName = "shell", ToolStatus = "completed" },
                new HistoryContentPart(4, "  \n  ", HistoryPartKind.Text),
                new HistoryContentPart(5, "second thought", HistoryPartKind.Reasoning),
            ],
        })!;

        ConversationSegment activity = Assert.Single(entry.Segments);
        Assert.Equal(ConversationSegmentKind.Activity, activity.Kind);
        Assert.Equal(4, activity.Items.Count);
        Assert.Equal("2 tool calls", ConversationPresentation.ActivityTitle(activity));
    }

    [Fact]
    public void FailedToolMarksTheWholeActivityGroup()
    {
        ConversationEntry entry = ConversationPresentation.From(new HistoryMessage("10", "assistant", "unused")
        {
            Parts =
            [
                new HistoryContentPart(0, "ok", HistoryPartKind.Tool) { ToolName = "read", ToolStatus = "completed" },
                new HistoryContentPart(1, "boom", HistoryPartKind.Tool) { ToolName = "shell", ToolStatus = "error", ToolFailed = true },
            ],
        })!;

        ConversationSegment activity = Assert.Single(entry.Segments);
        Assert.Equal(ConversationSegmentKind.Activity, activity.Kind);
        Assert.True(ConversationPresentation.ActivityFailed(activity));
        Assert.False(ConversationPresentation.ActivityRunning(activity));
        Assert.Equal("2 tool calls · failed", ConversationPresentation.ActivityTitle(activity));
        Assert.True(entry.IsWarning);
    }

    [Fact]
    public void ConsecutiveAssistantStepsMergeIntoOneActivityBlock()
    {
        IReadOnlyList<ConversationEntry> entries = ConversationPresentation.From(
        [
            new HistoryMessage("u1", "user", "prompt"),
            new HistoryMessage("a1", "assistant", "unused")
            {
                Parts = [new HistoryContentPart(0, "out", HistoryPartKind.Tool) { ToolName = "shell", ToolStatus = "completed" }],
            },
            new HistoryMessage("a2", "assistant", "unused")
            {
                Parts =
                [
                    new HistoryContentPart(0, "a thought", HistoryPartKind.Reasoning),
                    new HistoryContentPart(1, "out2", HistoryPartKind.Tool) { ToolName = "read", ToolStatus = "completed" },
                ],
                ChangedFiles = ["src/App.cs"],
            },
        ]);

        ConversationEntry entry = Assert.Single(entries, candidate => candidate.Role == ConversationRole.Assistant);
        Assert.Equal(2, entries.Count);
        Assert.Equal("a1", entry.Key);
        ConversationSegment activity = Assert.Single(entry.Segments);
        Assert.Equal(ConversationSegmentKind.Activity, activity.Kind);
        Assert.Equal(3, activity.Items.Count);
        Assert.Equal("2 tool calls", ConversationPresentation.ActivityTitle(activity));
        Assert.Equal(["src/App.cs"], entry.ChangedFiles);
    }

    [Fact]
    public void TrailingAndLeadingActivityMergeAcrossMessagesWhileTextKeepsItsMessage()
    {
        IReadOnlyList<ConversationEntry> entries = ConversationPresentation.From(
        [
            new HistoryMessage("a1", "assistant", "unused")
            {
                Parts =
                [
                    new HistoryContentPart(0, "Let me check.", HistoryPartKind.Text),
                    new HistoryContentPart(1, "out", HistoryPartKind.Tool) { ToolName = "read", ToolStatus = "completed" },
                ],
            },
            new HistoryMessage("a2", "assistant", "unused")
            {
                Parts =
                [
                    new HistoryContentPart(0, "fix", HistoryPartKind.Tool) { ToolName = "edit", ToolStatus = "completed" },
                    new HistoryContentPart(1, "Done.", HistoryPartKind.Text),
                ],
            },
        ]);

        Assert.Equal(2, entries.Count);
        Assert.Equal(ConversationSegmentKind.Text, entries[0].Segments[0].Kind);
        ConversationSegment activity = Assert.Single(entries[0].Segments, segment => segment.Kind == ConversationSegmentKind.Activity);
        Assert.Equal(2, activity.Items.Count);
        Assert.Equal("2 tool calls", ConversationPresentation.ActivityTitle(activity));
        ConversationSegment text = Assert.Single(entries[1].Segments);
        Assert.Equal("Done.", text.Text);
    }

    [Fact]
    public void VisibleContentBetweenAssistantStepsPreventsMerging()
    {
        IReadOnlyList<ConversationEntry> entries = ConversationPresentation.From(
        [
            new HistoryMessage("a1", "assistant", "unused")
            {
                Parts = [new HistoryContentPart(0, "out", HistoryPartKind.Tool) { ToolName = "shell", ToolStatus = "completed" }],
            },
            new HistoryMessage("a2", "assistant", "Still working."),
            new HistoryMessage("a3", "assistant", "unused")
            {
                Parts = [new HistoryContentPart(0, "out2", HistoryPartKind.Tool) { ToolName = "read", ToolStatus = "completed" }],
            },
            new HistoryMessage("c1", "compaction", "Context compacted · completed"),
            new HistoryMessage("a4", "assistant", "unused")
            {
                Parts = [new HistoryContentPart(0, "out3", HistoryPartKind.Tool) { ToolName = "edit", ToolStatus = "completed" }],
            },
        ]);

        Assert.Equal(5, entries.Count);
        Assert.Equal(ConversationRole.Divider, entries[3].Role);
    }

    [Fact]
    public void LiveStandaloneToolGroupMergesIntoThePreviousAssistantStep()
    {
        IReadOnlyList<ConversationEntry> entries = ConversationPresentation.From(
        [
            new HistoryMessage("a1", "assistant", "unused")
            {
                Parts = [new HistoryContentPart(0, "out", HistoryPartKind.Tool) { ToolName = "shell", ToolStatus = "completed" }],
            },
            new HistoryMessage("m9:t1", "tool activity", "bash · called"),
        ]);

        ConversationEntry entry = Assert.Single(entries);
        ConversationSegment activity = Assert.Single(entry.Segments);
        Assert.Equal(ConversationSegmentKind.Activity, activity.Kind);
        Assert.Equal(2, activity.Items.Count);
        Assert.Equal("2 tool calls · running", ConversationPresentation.ActivityTitle(activity));
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
