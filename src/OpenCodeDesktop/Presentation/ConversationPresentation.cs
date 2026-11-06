using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

using OpenCodeDesktop.Core.Workspaces;

namespace OpenCodeDesktop.Presentation;

internal enum ConversationRole { User, Assistant, Tool, Divider, Error }

internal enum ConversationSegmentKind { Text, Code, Reasoning, Tool }

internal sealed record ConversationSegment(string Text, ConversationSegmentKind Kind)
{
    internal string? ToolName { get; init; }
    internal string? ToolInput { get; init; }
    internal string? ToolStatus { get; init; }
    internal bool ToolFailed { get; init; }
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
    internal string PlainText => string.Join("\n\n", Segments.Select(segment => segment.Text).Where(text => text.Length > 0));
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
        string? user = null;
        foreach (HistoryMessage message in history)
        {
            if (message.Kind.StartsWith("user", StringComparison.Ordinal)) { user = message.Id; }
            ConversationEntry? entry = From(message);
            if (entry is not null)
            {
                entries.Add(entry with { ReviewFrom = user });
            }
        }
        return entries;
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
        foreach (HistoryContentPart part in message.Parts)
        {
            switch (part.Kind)
            {
                case HistoryPartKind.Text:
                    AddTextSegments(segments, part.Text);
                    break;
                case HistoryPartKind.Reasoning:
                    if (Clean(part.Text).Trim().Length > 0)
                    {
                        segments.Add(new(Clean(part.Text).Trim(), ConversationSegmentKind.Reasoning));
                    }
                    break;
                case HistoryPartKind.Tool:
                    segments.Add(ToSegment(part));
                    break;
                default:
                    segments.Add(new(Clean(part.Text).Trim(), ConversationSegmentKind.Text));
                    break;
            }
        }
        return segments;
    }

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
