using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

using OpenCodeDesktop.Core.Workspaces;

namespace OpenCodeDesktop.Presentation;

internal enum ConversationRole { User, Assistant, Tool, Activity, Error }

internal sealed record ConversationSegment(string Text, bool IsCode, bool IsTool = false, string? ToolName = null, bool IsActivity = false);

internal sealed record ConversationEntry(
    string Key,
    ConversationRole Role,
    string Title,
    IReadOnlyList<ConversationSegment> Segments,
    bool IsWarning = false)
{
    internal IReadOnlyList<HistoryFileReference> FileReferences { get; init; } = [];
    internal IReadOnlyList<string> ChangedFiles { get; init; } = [];
    internal string? ReviewFrom { get; init; }
    internal string PlainText => string.Join("\n\n", Segments.Select(segment => segment.Text));
    internal string AutomationName => Role == ConversationRole.User ? "You said: " + PlainText : Title + ": " + PlainText;
}

internal static class ConversationPresentation
{
    internal static IReadOnlyList<ConversationEntry> From(IEnumerable<HistoryMessage> history)
    {
        List<ConversationEntry> entries = [];
        string? user = null;
        foreach (HistoryMessage message in history)
        {
            if (message.Kind == "user") { user = message.Id; }
            entries.Add(From(message) with { ReviewFrom = user });
        }
        return entries;
    }

    internal static ConversationEntry From(HistoryMessage message)
    {
        string kind = message.Kind;
        bool uncertain = kind.Contains("delivery uncertain", StringComparison.OrdinalIgnoreCase);
        bool queued = kind.Contains("accepted / queued", StringComparison.OrdinalIgnoreCase);
        ConversationRole role = kind.Equals("assistant", StringComparison.OrdinalIgnoreCase)
            ? ConversationRole.Assistant
            : kind.Equals("tool activity", StringComparison.OrdinalIgnoreCase) ? ConversationRole.Tool
            : kind.Contains("error", StringComparison.OrdinalIgnoreCase) ? ConversationRole.Error
            : kind.StartsWith("user", StringComparison.OrdinalIgnoreCase) ? ConversationRole.User
            : IsActivity(kind) ? ConversationRole.Activity
            : ConversationRole.Activity;
        string title = role switch
        {
            ConversationRole.User => "You",
            ConversationRole.Assistant => "OpenCode",
            ConversationRole.Tool => "Tool activity",
            ConversationRole.Activity => IsActivity(kind) ? "Activity" : "Unsupported entry",
            _ => "Unsupported entry",
        };
        if (uncertain)
        {
            title += " · delivery uncertain";
        }
        else if (queued)
        {
            title += " · queued";
        }

        IReadOnlyList<ConversationSegment> segments = role == ConversationRole.Tool
            ? [new(Clean(message.Text), false, true)]
            : Segments(message.Text, role == ConversationRole.Assistant || role == ConversationRole.User);
        return new(message.Id, role, title, segments, uncertain || role == ConversationRole.Error) { FileReferences = message.FileReferences, ChangedFiles = message.ChangedFiles };
    }

    internal static IReadOnlyList<ConversationSegment> Segments(string text, bool classifyRecordedActivity)
    {
        List<ConversationSegment> segments = [];
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
                segments.Add(new(part.Trim('\r', '\n'), true));
                continue;
            }

            foreach (string block in part.Split("\n\n", StringSplitOptions.RemoveEmptyEntries))
            {
                string trimmed = block.Trim();
                if (trimmed.Length == 0)
                {
                    continue;
                }

                if (classifyRecordedActivity && trimmed.StartsWith("Recorded tool:", StringComparison.Ordinal))
                {
                    int separator = trimmed.IndexOf('\n');
                    string name = trimmed["Recorded tool:".Length..(separator < 0 ? trimmed.Length : separator)].Trim();
                    string body = separator < 0 ? "Recorded tool call." : trimmed[(separator + 1)..].Trim();
                    segments.Add(new(body, false, true, name.Length == 0 ? null : name));
                }
                else if (classifyRecordedActivity && IsRecordedActivity(trimmed))
                {
                    segments.Add(new(trimmed, false, IsActivity: true));
                }
                else
                {
                    segments.Add(new(trimmed, false));
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

    private static bool IsActivity(string kind) => kind is "system" or "synthetic" or "idle" or "compaction" or "skill"
        or "agent-switched" or "model-switched" or "location-switched";
    private static bool IsRecordedActivity(string value) =>
        value.StartsWith("Recorded status:", StringComparison.Ordinal) ||
        value.StartsWith("Recorded reasoning:", StringComparison.Ordinal) ||
        value.StartsWith("Recorded shell command:", StringComparison.Ordinal) ||
        value.StartsWith("Recorded skill:", StringComparison.Ordinal) ||
        value.StartsWith("Recorded agent selection:", StringComparison.Ordinal) ||
        value.StartsWith("Recorded context compaction:", StringComparison.Ordinal);
}
