using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

using OpenCodeDesktop.Core.Workspaces;

namespace OpenCodeDesktop.Presentation;

internal enum ConversationRole { User, Assistant, Tool, Divider, Error }

internal enum ConversationSegmentKind { Text, Code, Reasoning, Tool, Activity }

internal sealed record ConversationSegment(string Text, ConversationSegmentKind Kind)
{
    internal string? ToolName { get; init; }
    internal string? ToolInput { get; init; }
    internal string? ToolStatus { get; init; }
    internal bool ToolFailed { get; init; }
    /// <summary>Nested steps of an Activity block (Reasoning and Tool segments).</summary>
    internal IReadOnlyList<ConversationSegment> Items { get; init; } = [];
}

internal sealed record ConversationEntry(
    string Key,
    ConversationRole Role,
    IReadOnlyList<ConversationSegment> Segments,
    string? Title = null,
    string? Caption = null,
    bool IsWarning = false)
{
    internal IReadOnlyList<HistoryFileReference> FileReferences { get; init; } = [];
    internal IReadOnlyList<string> ChangedFiles { get; init; } = [];
    internal string? ReviewFrom { get; init; }
    internal string PlainText => string.Join("\n\n", Segments.SelectMany(Flatten).Select(segment => segment.Text).Where(text => text.Length > 0));

    private static IEnumerable<ConversationSegment> Flatten(ConversationSegment segment) =>
        segment.Kind == ConversationSegmentKind.Activity ? segment.Items : [segment];
    internal string AutomationName => (Role switch
    {
        ConversationRole.User => "You said: " + PlainText,
        ConversationRole.Assistant => "OpenCode said: " + PlainText,
        ConversationRole.Tool => "Tool activity: " + (Title ?? "") + ". " + PlainText,
        ConversationRole.Error => "Error: " + PlainText,
        _ => Title ?? PlainText,
    }) + (Caption is { } caption ? ". " + caption : "");
}

internal static class ConversationPresentation
{
    internal static IReadOnlyList<ConversationEntry> From(IEnumerable<HistoryMessage> history)
    {
        List<ConversationEntry> entries = [];
        Dictionary<string, int> assistantIndex = new(StringComparer.Ordinal);
        Dictionary<string, int> activityIndex = new(StringComparer.Ordinal);
        string? user = null;
        foreach (HistoryMessage message in history)
        {
            if (message.Kind.StartsWith("user", StringComparison.Ordinal)) { user = message.Id; }
            if (message.Kind.Equals("tool activity", StringComparison.OrdinalIgnoreCase))
            {
                FoldToolActivity(entries, assistantIndex, activityIndex, message, user);
                continue;
            }

            ConversationEntry? entry = From(message);
            if (entry is not null)
            {
                if (entry.Role == ConversationRole.Assistant)
                {
                    assistantIndex[message.Id] = entries.Count;
                }

                entries.Add(entry with { ReviewFrom = user });
            }
        }
        return CoalesceActivityRuns(entries);
    }

    // OpenCode records each step as its own assistant message, so a run of tool/thinking steps
    // arrives as consecutive entries. When one entry ends with an activity block and the next
    // begins with one — no visible content between — merge them into a single block. The next
    // entry keeps any remaining content (its text renders right after the merged block); an
    // entry left with no segments is removed.
    private static IReadOnlyList<ConversationEntry> CoalesceActivityRuns(List<ConversationEntry> entries)
    {
        int index = 0;
        while (index < entries.Count - 1)
        {
            ConversationEntry current = entries[index];
            ConversationEntry next = entries[index + 1];
            ConversationSegment? tail = Foldable(current) && current.Segments.Count > 0 && IsActivityPart(current.Segments[^1]) ? current.Segments[^1] : null;
            ConversationSegment? head = Foldable(next) && next.Segments.Count > 0 && IsActivityPart(next.Segments[0]) ? next.Segments[0] : null;
            if (tail is null || head is null)
            {
                index++;
                continue;
            }

            List<ConversationSegment> items = tail.Kind == ConversationSegmentKind.Activity ? [.. tail.Items] : [tail];
            items.AddRange(head.Kind == ConversationSegmentKind.Activity ? head.Items : [head]);
            IReadOnlyList<ConversationSegment> remaining = [.. next.Segments.Skip(1)];
            entries[index] = current with
            {
                Segments = [.. current.Segments.Take(current.Segments.Count - 1), ActivitySegment(items)],
                FileReferences = remaining.Count == 0 ? [.. current.FileReferences, .. next.FileReferences] : current.FileReferences,
                ChangedFiles = remaining.Count == 0 ? [.. current.ChangedFiles, .. next.ChangedFiles] : current.ChangedFiles,
                IsWarning = current.IsWarning || next.IsWarning,
            };
            if (remaining.Count == 0)
            {
                entries.RemoveAt(index + 1);
            }
            else
            {
                entries[index + 1] = next with { Segments = remaining };
                index++;
            }
        }
        return entries;

        static bool Foldable(ConversationEntry entry) =>
            entry.Role == ConversationRole.Assistant || entry.Role == ConversationRole.Tool && IsStandaloneActivity(entry);

        static bool IsActivityPart(ConversationSegment segment) =>
            segment.Kind is ConversationSegmentKind.Activity or ConversationSegmentKind.Reasoning;

        static bool IsStandaloneActivity(ConversationEntry entry) =>
            entry.Key.EndsWith(":activity", StringComparison.Ordinal);
    }

    // Live tool events belong with their message: they extend the assistant entry's trailing
    // activity block when it exists, or one grouped standalone block per message otherwise.
    private static void FoldToolActivity(List<ConversationEntry> entries, Dictionary<string, int> assistantIndex,
        Dictionary<string, int> activityIndex, HistoryMessage message, string? user)
    {
        string parent = message.Id;
        int separator = parent.LastIndexOf(':');
        if (separator > 0)
        {
            parent = parent[..separator];
        }

        ConversationSegment tool = LiveToolSegment(message.Text);
        if (assistantIndex.TryGetValue(parent, out int assistant))
        {
            ConversationEntry entry = entries[assistant];
            List<ConversationSegment> segments = [.. entry.Segments];
            if (segments.Count > 0 && segments[^1].Kind == ConversationSegmentKind.Activity)
            {
                segments[^1] = segments[^1] with { Items = [.. segments[^1].Items, tool] };
            }
            else
            {
                segments.Add(ActivitySegment([tool]));
            }

            entries[assistant] = entry with { Segments = segments };
            return;
        }

        if (activityIndex.TryGetValue(parent, out int group))
        {
            ConversationEntry entry = entries[group];
            entries[group] = entry with { Segments = [entry.Segments[0] with { Items = [.. entry.Segments[0].Items, tool] }] };
            return;
        }

        activityIndex[parent] = entries.Count;
        entries.Add(new(parent + ":activity", ConversationRole.Tool, [ActivitySegment([tool])]) { ReviewFrom = user });
    }

    // Only task-relevant items surface in the conversation: user and assistant content (text, thinking,
    // tool calls), concise lifecycle dividers, and errors. Transport noise and server bookkeeping stay out.
    internal static ConversationEntry? From(HistoryMessage message)
    {
        string kind = message.Kind;
        bool uncertain = kind.Contains("delivery uncertain", StringComparison.OrdinalIgnoreCase);
        bool queued = kind.Contains("accepted / queued", StringComparison.OrdinalIgnoreCase);
        if (kind.StartsWith("user", StringComparison.OrdinalIgnoreCase))
        {
            return new(message.Id, ConversationRole.User, TextSegments(message.Text),
                Caption: uncertain ? "Delivery uncertain — refresh to confirm; do not resend" : queued ? "Queued" : null,
                IsWarning: uncertain)
            { FileReferences = message.FileReferences, ChangedFiles = message.ChangedFiles };
        }

        if (kind.Equals("assistant", StringComparison.OrdinalIgnoreCase))
        {
            return new(message.Id, ConversationRole.Assistant, AssistantSegments(message),
                IsWarning: message.Parts.Any(part => part.Kind is HistoryPartKind.Error || part.ToolFailed))
            { FileReferences = message.FileReferences, ChangedFiles = message.ChangedFiles };
        }

        if (kind.Equals("tool activity", StringComparison.OrdinalIgnoreCase))
        {
            return new(message.Id, ConversationRole.Tool, [LiveToolSegment(message.Text)]);
        }

        if (kind.Equals("shell", StringComparison.OrdinalIgnoreCase))
        {
            return new(message.Id, ConversationRole.Tool, [.. message.Parts.Where(part => part.Kind == HistoryPartKind.Tool).Select(ToSegment)]);
        }

        if (kind.Contains("error", StringComparison.OrdinalIgnoreCase))
        {
            return new(message.Id, ConversationRole.Error, TextSegments(message.Text), Title: "Error", IsWarning: true);
        }

        if (kind is "system" or "synthetic" or "idle")
        {
            return null;
        }

        if (kind is "compaction" or "skill" or "agent-switched" or "model-switched" or "location-switched")
        {
            return new(message.Id, ConversationRole.Divider, [], Title: message.Text);
        }

        return new(message.Id, ConversationRole.Divider, [], Title: "Unsupported entry · " + kind, IsWarning: false);
    }

    private static IReadOnlyList<ConversationSegment> AssistantSegments(HistoryMessage message)
    {
        if (message.Parts.Count == 0)
        {
            return TextSegments(message.Text);
        }

        List<ConversationSegment> segments = [];
        List<ConversationSegment>? run = null;
        void FlushRun()
        {
            if (run is null)
            {
                return;
            }

            // Consecutive thinking/tool parts collapse into one Activity block; a lone
            // thinking part keeps the simpler standalone collapsible block.
            segments.Add(run.Count == 1 && run[0].Kind == ConversationSegmentKind.Reasoning
                ? run[0]
                : ActivitySegment(run));
            run = null;
        }

        foreach (HistoryContentPart part in message.Parts)
        {
            switch (part.Kind)
            {
                case HistoryPartKind.Text:
                    // Empty text parts render nothing; they must not split a group.
                    if (Clean(part.Text).Trim().Length > 0)
                    {
                        FlushRun();
                        AddTextSegments(segments, part.Text);
                    }
                    break;
                case HistoryPartKind.Reasoning:
                    if (Clean(part.Text).Trim().Length > 0)
                    {
                        (run ??= []).Add(new(Clean(part.Text).Trim(), ConversationSegmentKind.Reasoning));
                    }
                    break;
                case HistoryPartKind.Tool:
                    (run ??= []).Add(ToSegment(part));
                    break;
                default:
                    if (Clean(part.Text).Trim().Length > 0)
                    {
                        FlushRun();
                        segments.Add(new(Clean(part.Text).Trim(), ConversationSegmentKind.Text));
                    }
                    break;
            }
        }
        FlushRun();
        return segments;
    }

    private static ConversationSegment ActivitySegment(IReadOnlyList<ConversationSegment> items) =>
        new("", ConversationSegmentKind.Activity) { Items = items };

    private static ConversationSegment ToSegment(HistoryContentPart part) => new(Clean(part.Text).Trim(), ConversationSegmentKind.Tool)
    {
        ToolName = part.ToolName,
        ToolInput = part.ToolInput,
        ToolStatus = part.ToolStatus,
        ToolFailed = part.ToolFailed,
    };

    /// <summary>Display text for a recorded or live tool lifecycle status. Null hides the caption.</summary>
    internal static string? ToolStatusText(string? status) => status switch
    {
        null or "" => null,
        "completed" => "completed",
        "error" => "failed",
        "running" or "streaming" or "called" or "progress" or "input.started" or "input.ended" => "running",
        _ => status,
    };

    internal static bool ActivityFailed(ConversationSegment activity) =>
        activity.Items.Any(item => item.ToolFailed || item.ToolStatus == "error");

    internal static bool ActivityRunning(ConversationSegment activity) =>
        activity.Items.Any(item => ToolStatusText(item.ToolStatus) == "running");

    /// <summary>Name of the most recent running tool in a group, when any.</summary>
    internal static string? ActivityRunningTool(ConversationSegment activity) =>
        activity.Items.LastOrDefault(item => ToolStatusText(item.ToolStatus) == "running")?.ToolName;

    /// <summary>Collapsed header summary for an activity block: tool count plus failure/running status.</summary>
    internal static string ActivityTitle(ConversationSegment activity)
    {
        int tools = activity.Items.Count(item => item.Kind == ConversationSegmentKind.Tool);
        string title = tools switch
        {
            0 => "Thinking",
            1 => "1 tool call",
            _ => tools + " tool calls",
        };
        if (ActivityFailed(activity))
        {
            title += " · failed";
        }
        else if (ActivityRunning(activity))
        {
            title += " · running";
        }

        return title;
    }

    private static ConversationSegment LiveToolSegment(string text)
    {
        // Live tool notifications stream as "name · lifecycle-event" until history hydration replaces them.
        string clean = Clean(text).Trim();
        int separator = clean.IndexOf(" · ", StringComparison.Ordinal);
        return new("", ConversationSegmentKind.Tool)
        {
            ToolName = separator > 0 ? clean[..separator] : clean,
            ToolStatus = separator > 0 ? clean[(separator + 3)..] : null,
        };
    }

    internal static IReadOnlyList<ConversationSegment> TextSegments(string text) =>
        AddTextSegments([], text);

    private static IReadOnlyList<ConversationSegment> AddTextSegments(List<ConversationSegment> segments, string text)
    {
        string[] fences = Clean(text).Split("```", StringSplitOptions.None);
        for (int index = 0; index < fences.Length; index++)
        {
            bool code = index % 2 == 1;
            string part = fences[index];
            if (part.Length == 0)
            {
                continue;
            }

            if (code)
            {
                segments.Add(new(part.Trim('\r', '\n'), ConversationSegmentKind.Code));
                continue;
            }

            foreach (string block in part.Split("\n\n", StringSplitOptions.RemoveEmptyEntries))
            {
                string trimmed = block.Trim();
                if (trimmed.Length > 0)
                {
                    segments.Add(new(trimmed, ConversationSegmentKind.Text));
                }
            }
        }

        return segments;
    }

    private static string Clean(string text)
    {
        string clean = Regex.Replace(text, @"\x1B\[[0-9;?]*[ -/]*[@-~]", "");
        clean = Regex.Replace(clean, @"\[[0-9;]{1,12}m", "");
        return new string(clean.Where(character => character is '\n' or '\r' or '\t' || !char.IsControl(character)).ToArray());
    }
}
