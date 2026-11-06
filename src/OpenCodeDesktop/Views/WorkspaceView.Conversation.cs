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
    private const double ConversationBottomTolerance = 48;
    private readonly List<string> _entryKeys = [];
    private readonly List<string> _entryHashes = [];
    private readonly List<FrameworkElement> _entryControls = [];
    private bool _historyMoreVisible;
    private bool _compactConversation;
    private readonly Dictionary<string, (double Offset, bool AtBottom)> _conversationPositions = new(StringComparer.Ordinal);
    private string? _conversationOwner;
    private bool _restoreConversationPosition;
    private bool _pinnedToBottom = true;
    private bool _unseenWhileUnpinned;
    private double _trackedScrollOffset = -1;
    private double _trackedScrollableHeight = -1;
    private FrameworkElement? _workingIndicator;
    private string _workingLabel = "";
    private bool _loadingStatePanelShown;

    private Border ConversationState(string glyphKey, string title, string message, ShellTone tone, params UIElement[] actions) =>
        ShellUi.StatePanel(glyphKey, title, message, tone, _compactConversation, actions);

    private void RenderLiveHistory(LiveWorkspace live, LiveWorkspacePresentation presentation)
    {
        if (_conversationOwner != presentation.DraftOwner)
        {
            if (_conversationOwner is not null && !_restoreConversationPosition)
            {
                _conversationPositions[_conversationOwner] = (ConversationScroll.VerticalOffset,
                    ConversationScroll.ScrollableHeight - ConversationScroll.VerticalOffset < ConversationBottomTolerance);
            }

            _conversationOwner = presentation.DraftOwner;
            _restoreConversationPosition = true;
            _unseenWhileUnpinned = false;
            _trackedScrollOffset = -1;
            _trackedScrollableHeight = -1;
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
            _pinnedToBottom = atBottom;
            DispatcherQueue.TryEnqueue(() =>
            {
                if (_conversationOwner == owner)
                {
                    ConversationScroll.ChangeView(null, atBottom ? double.MaxValue : offset, null, true);
                }
                UpdateJumpToLatest();
            });
        }

        UpdateWorkingIndicator();
    }

    private void UpdateConversationState(LiveWorkspace live, LiveWorkspacePresentation presentation, bool hasEntries)
    {
        _loadingStatePanelShown = false;
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
            _loadingStatePanelShown = true;
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
        bool prepend = false;
        bool changed = !_entryKeys.SequenceEqual(keys);
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
                    changed = true;
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
        if (!restorePending && !_pinnedToBottom && changed)
        {
            _unseenWhileUnpinned = true;
        }

        if (!restorePending && prepend)
        {
            double anchor = height;
            double previous = offset;
            DispatcherQueue.TryEnqueue(() => ConversationScroll.ChangeView(null, previous + (ConversationScroll.ScrollableHeight - anchor), null, true));
        }
        else if (!restorePending && _pinnedToBottom)
        {
            DispatcherQueue.TryEnqueue(() => ConversationScroll.ChangeView(null, double.MaxValue, null, true));
        }

        UpdateJumpToLatest();
    }

    private void ConversationScroll_ViewChanged(object? sender, ScrollViewerViewChangedEventArgs args)
    {
        double offset = ConversationScroll.VerticalOffset;
        double scrollable = ConversationScroll.ScrollableHeight;
        if (scrollable - offset < ConversationBottomTolerance)
        {
            // Any arrival at the bottom (user, restore, or content growth anchored by the ScrollViewer) re-pins.
            _pinnedToBottom = true;
        }
        else if (Math.Abs(offset - _trackedScrollOffset) > 0.5 || Math.Abs(scrollable - _trackedScrollableHeight) < 0.5)
        {
            // A deliberate offset change, or a no-op re-layout. Pure content growth at a fixed
            // offset (the else branch) keeps the pin state untouched.
            _pinnedToBottom = false;
        }

        _trackedScrollOffset = offset;
        _trackedScrollableHeight = scrollable;
        UpdateJumpToLatest();
    }

    private void PinConversationToBottom()
    {
        _pinnedToBottom = true;
        _unseenWhileUnpinned = false;
        DispatcherQueue.TryEnqueue(() => ConversationScroll.ChangeView(null, double.MaxValue, null, true));
        UpdateJumpToLatest();
    }

    private void JumpToLatest_Click(object sender, RoutedEventArgs args) => PinConversationToBottom();

    private void UpdateJumpToLatest()
    {
        bool show = !_pinnedToBottom && _entryKeys.Count > 0;
        JumpToLatest.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        JumpToLatestLabel.Text = _unseenWhileUnpinned ? "New messages" : "Jump to latest";
        AutomationProperties.SetName(JumpToLatest, _unseenWhileUnpinned ? "Jump to latest messages; new content below" : "Jump to latest messages");
    }

    private void UpdateWorkingIndicator()
    {
        string label = CurrentTask switch
        {
            { Stopping: true } => "Stopping… confirming the result",
            { Active: true, Stale: false } => "Working…",
            { Busy: true, Stale: false } => "Working…",
            _ => "",
        };
        if (label == _workingLabel)
        {
            return;
        }

        _workingLabel = label;
        if (label.Length == 0)
        {
            WorkingIndicatorHost.Visibility = Visibility.Collapsed;
            return;
        }

        _workingIndicator ??= WorkspaceTemplate<StackPanel>("WorkingIndicatorTemplate");
        WorkingIndicatorHost.Content = _workingIndicator;
        ShellUi.Part<TextBlock>(_workingIndicator, "Label").Text = label;
        AutomationProperties.SetName(_workingIndicator, label);
        WorkingIndicatorHost.Visibility = Visibility.Visible;
    }

    private static string Hash(ConversationEntry entry) =>
        entry.Role + "|" + entry.Title + "|" + entry.Caption + "|" + entry.ReviewFrom + "|" + string.Join('|', entry.ChangedFiles) + "|" + string.Join('|', entry.FileReferences.Select(reference => reference.Uri + reference.Name)) + "|" + string.Join('\u001f', entry.Segments.Select(segment =>
            segment.Kind + ":" + segment.ToolName + ":" + segment.ToolInput + ":" + segment.ToolStatus + ":" + segment.ToolFailed + ":" + segment.Text));

    private FrameworkElement BuildEntry(ConversationEntry entry)
    {
        FrameworkElement body = entry.Role switch
        {
            ConversationRole.User => BuildUserEntry(entry),
            ConversationRole.Assistant => BuildAssistantEntry(entry),
            ConversationRole.Tool => BuildToolCard(entry.Segments.FirstOrDefault(segment => segment.Kind == ConversationSegmentKind.Tool)
                ?? new ConversationSegment("", ConversationSegmentKind.Tool)),
            ConversationRole.Divider => BuildDividerEntry(entry),
            _ => BuildErrorEntry(entry),
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
        StackPanel layout = WorkspaceTemplate<StackPanel>("UserEntryTemplate");
        AddSegments(ShellUi.Part<StackPanel>(layout, "Segments"), entry, ShellUi.Brush("ShellTextPrimaryBrush"));
        if (entry.Caption is { } caption)
        {
            TextBlock block = ShellUi.Part<TextBlock>(layout, "Caption");
            block.Text = caption;
            block.Foreground = ShellUi.Brush(entry.IsWarning ? "ShellWarningBrush" : "ShellTextTertiaryBrush");
            block.Visibility = Visibility.Visible;
        }

        AutomationProperties.SetName(layout, entry.AutomationName);
        return layout;
    }

    private FrameworkElement BuildAssistantEntry(ConversationEntry entry)
    {
        StackPanel layout = WorkspaceTemplate<StackPanel>("AssistantEntryTemplate");
        AddSegments(ShellUi.Part<StackPanel>(layout, "Segments"), entry, ShellUi.Brush("ShellTextPrimaryBrush"));
        AutomationProperties.SetName(layout, entry.AutomationName);
        return layout;
    }

    private FrameworkElement BuildDividerEntry(ConversationEntry entry)
    {
        TextBlock label = WorkspaceTemplate<TextBlock>("DividerEntryTemplate");
        label.Text = entry.Title ?? "";
        AutomationProperties.SetName(label, entry.AutomationName);
        return label;
    }

    private FrameworkElement BuildToolCard(ConversationSegment segment)
    {
        Border card = WorkspaceTemplate<Border>("ToolEntryTemplate");
        bool failed = segment.ToolFailed || segment.ToolStatus == "error";
        bool running = ConversationPresentation.ToolStatusText(segment.ToolStatus) == "running";
        string name = segment.ToolName is { Length: > 0 } toolName ? toolName : "Tool";
        string? caption = ConversationPresentation.ToolStatusText(segment.ToolStatus);

        ShellUi.Part<TextBlock>(card, "Title").Text = name;
        ShellUi.Part<TextBlock>(card, "Title").Foreground = ShellUi.Brush(failed ? "ShellDangerBrush" : "ShellTextPrimaryBrush");
        FontIcon icon = ShellUi.Part<FontIcon>(card, "Icon");
        icon.Foreground = ShellUi.Brush(failed ? "ShellDangerBrush" : "ShellAccentBrush");
        if (segment.ToolInput is { Length: > 0 } input)
        {
            TextBlock subtitle = ShellUi.Part<TextBlock>(card, "Subtitle");
            subtitle.Text = input;
            subtitle.Visibility = Visibility.Visible;
        }

        if (running)
        {
            ShellUi.Part<ProgressRing>(card, "Running").Visibility = Visibility.Visible;
        }
        else if (caption is { Length: > 0 })
        {
            TextBlock status = ShellUi.Part<TextBlock>(card, "Status");
            status.Text = caption;
            status.Foreground = ShellUi.Brush(failed ? "ShellDangerBrush" : "ShellTextTertiaryBrush");
            status.Visibility = Visibility.Visible;
        }

        card.BorderBrush = ShellUi.Brush(failed ? "ShellDangerBrush" : "ShellBorderBrush");
        bool hasOutput = segment.Text.Length > 0;
        FontIcon chevron = ShellUi.Part<FontIcon>(card, "Chevron");
        chevron.Visibility = hasOutput ? Visibility.Visible : Visibility.Collapsed;
        ContentControl output = ShellUi.Part<ContentControl>(card, "Output");
        if (hasOutput)
        {
            output.Content = CodeBlock(segment.Text, wrap: true);
            Button header = ShellUi.Part<Button>(card, "Header");
            header.Click += (_, _) =>
            {
                bool show = output.Visibility == Visibility.Collapsed;
                output.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
                chevron.Glyph = show ? "\uE70E" : "\uE70D";
            };
        }

        AutomationProperties.SetName(card, "Tool " + name + (caption is { Length: > 0 } text ? ", " + text : "") + ".");
        AutomationProperties.SetName(ShellUi.Part<Button>(card, "Header"), "Tool " + name + (hasOutput ? ". Activate to show or hide the recorded output." : "."));
        return card;
    }

    private FrameworkElement BuildThinkingBlock(string text)
    {
        StackPanel block = WorkspaceTemplate<StackPanel>("ThinkingEntryTemplate");
        Button header = ShellUi.Part<Button>(block, "Header");
        TextBlock body = ShellUi.Part<TextBlock>(block, "Body");
        FontIcon chevron = ShellUi.Part<FontIcon>(block, "Chevron");
        body.Text = text;
        header.Click += (_, _) =>
        {
            bool show = body.Visibility == Visibility.Collapsed;
            body.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            chevron.Glyph = show ? "\uE70E" : "\uE70D";
        };
        AutomationProperties.SetName(header, "Thinking. Activate to show or hide the recorded thinking content");
        AutomationProperties.SetName(body, "Thinking content: " + text);
        return block;
    }

    private FrameworkElement BuildErrorEntry(ConversationEntry entry)
    {
        Border card = WorkspaceTemplate<Border>("ErrorEntryTemplate");
        ShellUi.Part<TextBlock>(card, "Title").Text = entry.Title ?? "Error";
        AddSegments(ShellUi.Part<StackPanel>(card, "Segments"), entry, ShellUi.Brush("ShellDangerBrush"));
        AutomationProperties.SetName(card, entry.AutomationName);
        return card;
    }

    private void AddSegments(StackPanel content, ConversationEntry entry, Brush foreground)
    {
        foreach (ConversationSegment segment in entry.Segments)
        {
            switch (segment.Kind)
            {
                case ConversationSegmentKind.Code:
                    content.Children.Add(CodeBlock(segment.Text, wrap: false));
                    break;
                case ConversationSegmentKind.Tool:
                    content.Children.Add(BuildToolCard(segment));
                    break;
                case ConversationSegmentKind.Reasoning:
                    content.Children.Add(BuildThinkingBlock(segment.Text));
                    break;
                default:
                    TextBlock text = ShellUi.Text(segment.Text, "ShellSelectableBodyTextStyle");
                    text.Foreground = foreground;
                    content.Children.Add(text);
                    break;
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
