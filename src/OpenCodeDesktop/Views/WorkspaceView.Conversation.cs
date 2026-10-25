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
            ConversationStateHost.Content = ConversationState("ShellIconMessage", "Start a session",
                "Create a session to run a task, or keep an unsent draft below. No session is created until you choose Create session.",
                ShellTone.Neutral,
                ShellUi.LabelButton("Create session", "ShellIconAdd", async (_, _) => await live.CreateSessionAsync().ConfigureAwait(true), "ShellPrimaryButtonStyle"),
                ShellUi.LabelButton("Keep draft", null, (_, _) => DraftBox.Focus(FocusState.Programmatic), "ShellQuietButtonStyle"));
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

    private static Border Notice(string text, ShellTone tone)
    {
        StackPanel content = ShellUi.Stack(ShellUi.Dimension("ShellSpaceSmall"), Orientation.Horizontal);
        content.Children.Add(ShellUi.Icon("ShellIconInfo", ShellUi.Dimension("ShellIconSmall"), ShellUi.ToneBrush(tone)));
        content.Children.Add(ShellUi.Text(text, "ShellSecondaryTextStyle"));
        return new Border
        {
            Style = ShellUi.Resource<Style>("ShellSubtleCardStyle"),
            Background = ShellUi.ToneSurface(tone),
            Padding = new Thickness(12, 9, 12, 9),
            Child = content,
        };
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
            if (ProjectFileReferences.Candidate(location.Directory, reference.Uri) is not { } candidate) { continue; }
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
        StackPanel content = ShellUi.Stack(6);
        TextBlock title = ShellUi.Text(entry.Title, "ShellCaptionTextStyle");
        title.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
        title.Foreground = ShellUi.Brush("ShellAccentTextBrush");
        content.Children.Add(title);
        AddSegments(content, entry, ShellUi.Brush("ShellTextPrimaryBrush"));
        Border bubble = new()
        {
            Background = ShellUi.Brush("ShellAccentSurfaceBrush"),
            BorderBrush = ShellUi.Brush("ShellAccentBrush"),
            BorderThickness = ShellUi.Resource<Thickness>("ShellHairlineThickness"),
            CornerRadius = ShellUi.Resource<CornerRadius>("ShellCornerRadiusLarge"),
            Padding = new Thickness(14, 10, 14, 10),
            MaxWidth = 620,
            HorizontalAlignment = HorizontalAlignment.Left,
            Child = content,
        };
        AutomationProperties.SetName(bubble, entry.AutomationName);
        return bubble;
    }

    private FrameworkElement BuildAssistantEntry(ConversationEntry entry)
    {
        Grid layout = new() { ColumnSpacing = 12 };
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Border avatar = new()
        {
            Width = 30,
            Height = 30,
            CornerRadius = ShellUi.Resource<CornerRadius>("ShellCornerRadiusMedium"),
            Background = ShellUi.Brush("ShellAccentSurfaceBrush"),
            BorderBrush = ShellUi.Brush("ShellBorderBrush"),
            BorderThickness = ShellUi.Resource<Thickness>("ShellHairlineThickness"),
            Child = ShellUi.Icon("ShellIconAssistant", ShellUi.Dimension("ShellIconMedium"), ShellUi.Brush("ShellAccentTextBrush")),
            VerticalAlignment = VerticalAlignment.Top,
        };
        layout.Children.Add(avatar);
        StackPanel content = ShellUi.Stack(6);
        Grid.SetColumn(content, 1);
        TextBlock title = ShellUi.Text(entry.Title, "ShellCaptionTextStyle");
        title.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
        content.Children.Add(title);
        AddSegments(content, entry, ShellUi.Brush("ShellTextPrimaryBrush"));
        layout.Children.Add(content);
        AutomationProperties.SetName(layout, entry.AutomationName);
        return layout;
    }

    private FrameworkElement BuildActivityEntry(ConversationEntry entry)
    {
        StackPanel content = ShellUi.Stack(ShellUi.Dimension("ShellSpaceSmall"), Orientation.Horizontal);
        content.Children.Add(ShellUi.Icon("ShellIconInfo", ShellUi.Dimension("ShellIconSmall"), ShellUi.Brush("ShellTextTertiaryBrush")));
        StackPanel text = ShellUi.Stack(2);
        text.Children.Add(ShellUi.Text(entry.Title, "ShellTertiaryTextStyle"));
        text.Children.Add(ShellUi.Text(entry.PlainText, "ShellTertiaryTextStyle"));
        content.Children.Add(text);
        Border card = new()
        {
            Padding = new Thickness(10, 8, 10, 8),
            CornerRadius = ShellUi.Resource<CornerRadius>("ShellCornerRadiusMedium"),
            Background = ShellUi.Brush("ShellSubtleSurfaceBrush"),
            Child = content,
        };
        AutomationProperties.SetName(card, entry.AutomationName);
        return card;
    }

    private FrameworkElement BuildToolEntry(string title, string text, string? toolName, bool warning)
    {
        StackPanel content = ShellUi.Stack(6);
        StackPanel header = ShellUi.Stack(ShellUi.Dimension("ShellSpaceSmall"), Orientation.Horizontal);
        header.Children.Add(ShellUi.Icon("ShellIconTool", ShellUi.Dimension("ShellIconSmall"), warning ? ShellUi.Brush("ShellWarningBrush") : ShellUi.Brush("ShellAccentBrush")));
        header.Children.Add(ShellUi.Text(toolName is { Length: > 0 } ? "Tool · " + toolName : title, "ShellBodyStrongTextStyle"));

        content.Children.Add(header);
        content.Children.Add(CodeBlock(text, wrap: true));
        Border card = new()
        {
            Style = ShellUi.Resource<Style>("ShellCardStyle"),
            Padding = new Thickness(12, 10, 12, 10),
            BorderBrush = warning ? ShellUi.Brush("ShellWarningBrush") : ShellUi.Brush("ShellBorderBrush"),
            Child = content,
        };
        AutomationProperties.SetName(card, title + ": " + text);
        return card;
    }

    private FrameworkElement BuildErrorEntry(ConversationEntry entry)
    {
        StackPanel content = ShellUi.Stack(6);
        StackPanel header = ShellUi.Stack(ShellUi.Dimension("ShellSpaceSmall"), Orientation.Horizontal);
        header.Children.Add(ShellUi.Icon("ShellIconError", ShellUi.Dimension("ShellIconSmall"), ShellUi.Brush("ShellDangerBrush")));
        header.Children.Add(ShellUi.Text(entry.Title, "ShellBodyStrongTextStyle"));
        content.Children.Add(header);
        AddSegments(content, entry, ShellUi.Brush("ShellDangerBrush"));
        Border card = new()
        {
            Style = ShellUi.Resource<Style>("ShellCardStyle"),
            Background = ShellUi.Brush("ShellDangerSurfaceBrush"),
            BorderBrush = ShellUi.Brush("ShellDangerBrush"),
            Padding = new Thickness(12, 10, 12, 10),
            Child = content,
        };
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

    private static Border CodeBlock(string text, bool wrap)
    {
        TextBlock code = ShellUi.Text(text, wrap ? "ShellWrappedCodeTextStyle" : "ShellCodeTextStyle");
        ScrollViewer scroll = new()
        {
            Content = code,
            MaxHeight = 280,
            HorizontalScrollBarVisibility = wrap ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto,
            HorizontalScrollMode = wrap ? ScrollMode.Disabled : ScrollMode.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollMode = ScrollMode.Auto,
            Padding = new Thickness(10, 8, 10, 8),
        };
        return new Border
        {
            Style = ShellUi.Resource<Style>("ShellCodeBlockStyle"),
            Padding = new Thickness(0),
            Child = scroll,
        };
    }
}
