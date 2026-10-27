using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

using OpenCodeDesktop.Core.Workspaces;
using OpenCodeDesktop.Presentation;

namespace OpenCodeDesktop.Views;

public sealed partial class WorkspaceView
{
    private readonly List<string> _entryKeys = [];
    private readonly List<string> _entryHashes = [];
    private readonly List<FrameworkElement> _entryControls = [];
    private bool _historyMoreVisible;
    private bool _compactConversation;
    private readonly Dictionary<string, (double Offset, bool AtBottom)> _conversationPositions = new(StringComparer.Ordinal);
    private string? _conversationOwner;
    private bool _restoreConversationPosition;

    private Border ConversationState(string glyphKey, string title, string message, ShellTone tone, params UIElement[] actions) =>
        ShellUi.StatePanel(glyphKey, title, message, tone, _compactConversation, actions);

    private void RenderLiveHistory(LiveWorkspace live, LiveWorkspacePresentation presentation)
    {
        if (_conversationOwner != presentation.DraftOwner)
        {
            if (_conversationOwner is not null && !_restoreConversationPosition)
            {
                _conversationPositions[_conversationOwner] = (ConversationScroll.VerticalOffset,
                    ConversationScroll.ScrollableHeight - ConversationScroll.VerticalOffset < 48);
            }

            _conversationOwner = presentation.DraftOwner;
            _restoreConversationPosition = true;
        }

        IReadOnlyList<HistoryMessage> history = CurrentTask is { Hydrated: true } state
            ? live.History.Where(item => !state.Conversation.Any(message => message.Id == item.Id)).Concat(state.Conversation).ToArray()
            : live.History;
        IReadOnlyList<ConversationEntry> entries = ConversationPresentation.From(history);
        bool hasEntries = entries.Count > 0;
        UpdateConversationState(live, presentation, hasEntries);
        UpdateHistoryActions(live);
        RenderEntries(entries, _restoreConversationPosition);
        if (_restoreConversationPosition && (live.SelectedSessionId is null || live.HistoryLoaded || CurrentTask is { Hydrated: true }))
        {
            _restoreConversationPosition = false;
            string owner = presentation.DraftOwner;
            (double offset, bool atBottom) = _conversationPositions.GetValueOrDefault(owner, (0, true));
            DispatcherQueue.TryEnqueue(() =>
            {
                if (_conversationOwner == owner)
                {
                    ConversationScroll.ChangeView(null, atBottom ? double.MaxValue : offset, null, true);
                }
            });
        }
    }

    private void UpdateConversationState(LiveWorkspace live, LiveWorkspacePresentation presentation, bool hasEntries)
    {
        string message = presentation.ConversationNotice;
        if (_sendingDraft && !hasEntries)
        {
            // Draft send in flight: keep the current content (usually the draft guidance)
            // until the submitted message renders, instead of flashing transient loading or empty states.
            return;
        }

        if (!live.IsConnected)
        {
            ConversationStateHost.Content = ConversationState("ShellIconServer", "Connect to OpenCode",
                "Use Settings > Connections to connect a server. Saved profiles and project lists remain available there.",
                ShellTone.Neutral, ShellUi.LabelButton("Open Connections", "ShellIconSettings", (_, _) => SettingsRequested?.Invoke(this, EventArgs.Empty)));
            return;
        }

        if (live.Location is null)
        {
            ConversationStateHost.Content = ConversationState("ShellIconFolder", "Open a project",
                "Choose a server-side directory in Projects. Conversation history and task state come from the server.",
                ShellTone.Neutral, ShellUi.LabelButton("Add project", "ShellIconAdd", async (_, _) => await ShowAddProjectDialogAsync().ConfigureAwait(true)));
            return;
        }

        if (live.IsLoading && !live.HistoryLoaded)
        {
            ConversationStateHost.Content = ConversationState("ShellIconClock", "Loading conversation",
                message.Length > 0 ? message : "Reading history and execution state from the server.", ShellTone.Info);
            return;
        }

        if (live.Selected is null && !hasEntries)
        {
            ConversationStateHost.Content = ConversationState("ShellIconMessage", "New-session draft",
                "Describe a task below. The session is created on the server when you send; your choice of model and agent is used.",
                ShellTone.Neutral);
            return;
        }

        if (!live.HistoryLoaded)
        {
            ConversationStateHost.Content = ConversationState("ShellIconWarning", "History unavailable",
                message.Length > 0 ? message : "The server did not return session history. Retry or choose another session.",
                ShellTone.Warning, ShellUi.LabelButton("Retry", "ShellIconRefresh", async (_, _) => await live.RetryAsync().ConfigureAwait(true)));
            return;
        }

        if (!hasEntries)
        {
            ConversationStateHost.Content = ConversationState("ShellIconMessage", "No messages yet",
                message.Length > 0 ? message : "This session has no recorded messages. You can keep an unsent draft below.",
                ShellTone.Neutral);
            return;
        }

        ConversationStateHost.Content = message.Length == 0 ? null : Notice(message, ShellTone.Info);
    }

    private void UpdateHistoryActions(LiveWorkspace live)
    {
        bool visible = live.CanLoadHistory;
        if (visible == _historyMoreVisible && ConversationHistoryActions.Children.Count > 0)
        {
            return;
        }

        _historyMoreVisible = visible;
        ConversationHistoryActions.Children.Clear();
        if (visible)
        {
            ConversationHistoryActions.Children.Add(ShellUi.LabelButton("Load older messages", "ShellIconHistory",
                async (_, _) => await live.LoadOlderHistoryAsync().ConfigureAwait(true), "ShellQuietButtonStyle"));
        }
    }

    private Border Notice(string text, ShellTone tone)
    {
        Border notice = WorkspaceTemplate<Border>("ConversationNoticeTemplate");
        notice.Background = ShellUi.ToneSurface(tone);
        ShellUi.Part<FontIcon>(notice, "Icon").Foreground = ShellUi.ToneBrush(tone);
        ShellUi.Part<TextBlock>(notice, "Message").Text = text;
        return notice;
    }

    private void RenderEntries(IReadOnlyList<ConversationEntry> entries, bool restorePending)
    {
        var keys = entries.Select(entry => entry.Key).ToList();
        var hashes = entries.Select(Hash).ToList();
        double offset = ConversationScroll.VerticalOffset;
        double height = ConversationScroll.ScrollableHeight;
        bool atBottom = height - offset < 48;
        bool prepend = false;
        if (_entryKeys.SequenceEqual(keys))
        {
            for (int index = 0; index < entries.Count; index++)
            {
                if (_entryHashes[index] != hashes[index])
                {
                    FrameworkElement replacement = BuildEntry(entries[index]);
                    ConversationEntries.Children[index] = replacement;
                    _entryControls[index] = replacement;
                    _entryHashes[index] = hashes[index];
                }
            }
        }
        else if (entries.Count > _entryKeys.Count && keys.Take(_entryKeys.Count).SequenceEqual(_entryKeys))
        {
            for (int index = _entryKeys.Count; index < entries.Count; index++)
            {
                FrameworkElement element = BuildEntry(entries[index]);
                ConversationEntries.Children.Add(element);
                _entryControls.Add(element);
            }
        }
        else
        {
            prepend = entries.Count > _entryKeys.Count && keys.TakeLast(_entryKeys.Count).SequenceEqual(_entryKeys);
            ConversationEntries.Children.Clear();
            _entryControls.Clear();
            foreach (ConversationEntry entry in entries)
            {
                FrameworkElement element = BuildEntry(entry);
                ConversationEntries.Children.Add(element);
                _entryControls.Add(element);
            }
        }

        _entryKeys.Clear();
        _entryKeys.AddRange(keys);
        _entryHashes.Clear();
        _entryHashes.AddRange(hashes);
        if (!restorePending && prepend)
        {
            double anchor = height;
            double previous = offset;
            DispatcherQueue.TryEnqueue(() => ConversationScroll.ChangeView(null, previous + (ConversationScroll.ScrollableHeight - anchor), null, true));
        }
        else if (!restorePending && atBottom)
        {
            DispatcherQueue.TryEnqueue(() => ConversationScroll.ChangeView(null, double.MaxValue, null, true));
        }
    }

    private static string Hash(ConversationEntry entry) =>
        entry.Role + "|" + entry.Title + "|" + entry.ReviewFrom + "|" + string.Join('|', entry.ChangedFiles) + "|" + string.Join('|', entry.FileReferences.Select(reference => reference.Uri + reference.Name)) + "|" + string.Join('\u001f', entry.Segments.Select(segment =>
            (segment.IsCode ? "code" : segment.IsTool ? "tool" : segment.IsActivity ? "activity" : "text") + ":" + segment.ToolName + ":" + segment.Text));

    private FrameworkElement BuildEntry(ConversationEntry entry)
    {
        FrameworkElement body = entry.Role switch
        {
            ConversationRole.User => BuildUserEntry(entry),
            ConversationRole.Assistant => BuildAssistantEntry(entry),
            ConversationRole.Tool => BuildToolEntry(entry.Title, entry.PlainText, null, entry.IsWarning),
            ConversationRole.Error => BuildErrorEntry(entry),
            _ => BuildActivityEntry(entry),
        };
        if (_live?.Location is not { } location || entry.FileReferences.Count == 0 && entry.ChangedFiles.Count == 0) { return body; }
        StackPanel wrapper = ShellUi.Stack(6);
        wrapper.Children.Add(body);
        if (entry.ReviewFrom is { } from)
        {
            foreach (string file in entry.ChangedFiles)
            {
                string? session = _live.SelectedSessionId;
                wrapper.Children.Add(ShellUi.LabelButton("Review change · " + file, "ShellIconInfo", async (_, _) =>
                {
                    if (_live?.Location != location || _live.SelectedSessionId != session) { return; }
                    DetailsLayout.IsPaneOpen = true;
                    await DetailsContent.OpenTurnAsync(from, file).ConfigureAwait(true);
                }, "ShellQuietButtonStyle"));
            }
        }
        foreach (HistoryFileReference reference in entry.FileReferences)
        {
            if (ProjectFileReferences.Candidate(location.Directory, reference.Uri) is not { } candidate)
            {
                wrapper.Children.Add(ShellUi.Text("Attachment · " + (reference.Name ?? "Recorded file") + " · content retained by server", "ShellCaptionTextStyle", TextWrapping.Wrap));
                continue;
            }
            Button open = ShellUi.LabelButton("Open file · " + candidate, "ShellIconFolder", (_, _) => { }, "ShellQuietButtonStyle");
            TextBlock status = ShellUi.Text("", "ShellCaptionTextStyle");
            AutomationProperties.SetLiveSetting(status, Microsoft.UI.Xaml.Automation.Peers.AutomationLiveSetting.Polite);
            open.Click += async (_, _) =>
            {
                if (_live is not { } live || live.Location != location) { return; }
                long generation = _tabGeneration;
                open.IsEnabled = false;
                try
                {
                    string? path = await live.VerifyFileReferenceAsync(reference.Uri, System.Threading.CancellationToken.None).ConfigureAwait(true);
                    if (generation != _tabGeneration || _live != live) { return; }
                    if (path is not null) { await OpenProjectFilesAsync(location, path).ConfigureAwait(true); }
                    else { status.Text = "File identity could not be verified on this server."; }
                }
                catch (WorkspaceException) { status.Text = "Unable to verify the server file. Retry when connected."; }
                finally { open.IsEnabled = true; }
            };
            wrapper.Children.Add(open);
            wrapper.Children.Add(status);
        }
        return wrapper;
    }

    private FrameworkElement BuildUserEntry(ConversationEntry entry)
    {
        Border bubble = WorkspaceTemplate<Border>("UserEntryTemplate");
        ShellUi.Part<TextBlock>(bubble, "Title").Text = entry.Title;
        AddSegments(ShellUi.Part<StackPanel>(bubble, "Segments"), entry, ShellUi.Brush("ShellTextPrimaryBrush"));
        AutomationProperties.SetName(bubble, entry.AutomationName);
        return bubble;
    }

    private FrameworkElement BuildAssistantEntry(ConversationEntry entry)
    {
        Grid layout = WorkspaceTemplate<Grid>("AssistantEntryTemplate");
        ShellUi.Part<TextBlock>(layout, "Title").Text = entry.Title;
        AddSegments(ShellUi.Part<StackPanel>(layout, "Segments"), entry, ShellUi.Brush("ShellTextPrimaryBrush"));
        AutomationProperties.SetName(layout, entry.AutomationName);
        return layout;
    }

    private FrameworkElement BuildActivityEntry(ConversationEntry entry)
    {
        Border card = WorkspaceTemplate<Border>("ActivityEntryTemplate");
        ShellUi.Part<TextBlock>(card, "Title").Text = entry.Title;
        ShellUi.Part<TextBlock>(card, "Message").Text = entry.PlainText;
        AutomationProperties.SetName(card, entry.AutomationName);
        return card;
    }

    private FrameworkElement BuildToolEntry(string title, string text, string? toolName, bool warning)
    {
        Border card = WorkspaceTemplate<Border>("ToolEntryTemplate");
        ShellUi.Part<TextBlock>(card, "Title").Text = toolName is { Length: > 0 } ? "Tool · " + toolName : title;
        ShellUi.Part<FontIcon>(card, "Icon").Foreground = ShellUi.Brush(warning ? "ShellWarningBrush" : "ShellAccentBrush");
        ShellUi.Part<StackPanel>(card, "Segments").Children.Add(CodeBlock(text, wrap: true));
        card.BorderBrush = ShellUi.Brush(warning ? "ShellWarningBrush" : "ShellBorderBrush");
        AutomationProperties.SetName(card, title + ": " + text);
        return card;
    }

    private FrameworkElement BuildErrorEntry(ConversationEntry entry)
    {
        Border card = WorkspaceTemplate<Border>("ErrorEntryTemplate");
        ShellUi.Part<TextBlock>(card, "Title").Text = entry.Title;
        AddSegments(ShellUi.Part<StackPanel>(card, "Segments"), entry, ShellUi.Brush("ShellDangerBrush"));
        AutomationProperties.SetName(card, entry.AutomationName);
        return card;
    }

    private void AddSegments(StackPanel content, ConversationEntry entry, Brush foreground)
    {
        foreach (ConversationSegment segment in entry.Segments)
        {
            if (segment.IsCode)
            {
                content.Children.Add(CodeBlock(segment.Text, wrap: false));
            }
            else if (segment.IsTool)
            {
                content.Children.Add(BuildToolEntry("Tool activity", segment.Text, segment.ToolName, entry.IsWarning));
            }
            else if (segment.IsActivity)
            {
                TextBlock activity = ShellUi.Text(segment.Text, "ShellTertiaryTextStyle");
                activity.IsTextSelectionEnabled = true;
                content.Children.Add(activity);
            }
            else
            {
                TextBlock text = ShellUi.Text(segment.Text, "ShellSelectableBodyTextStyle");
                text.Foreground = foreground;
                content.Children.Add(text);
            }
        }
    }

    private Border CodeBlock(string text, bool wrap)
    {
        Border block = WorkspaceTemplate<Border>("CodeBlockTemplate");
        TextBlock code = ShellUi.Part<TextBlock>(block, "Code");
        code.Text = text;
        code.Style = ShellUi.Resource<Style>(wrap ? "ShellWrappedCodeTextStyle" : "ShellCodeTextStyle");
        ScrollViewer scroll = ShellUi.Part<ScrollViewer>(block, "Scroll");
        scroll.HorizontalScrollBarVisibility = wrap ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto;
        scroll.HorizontalScrollMode = wrap ? ScrollMode.Disabled : ScrollMode.Auto;
        return block;
    }
}
