using System;
using System.Threading.Tasks;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;

using OpenCodeDesktop.Core.Workspaces;
using OpenCodeDesktop.Presentation;

using Windows.System;

namespace OpenCodeDesktop.Views;

public sealed partial class WorkspaceView : UserControl
{
    private bool? _projectsInline;
    private bool? _detailsInline;
    private bool _expanded;
    private double _detailsWidth = ShellUi.Dimension("ShellDetailsWidth");

    public event EventHandler? SettingsRequested;
    internal Button ConnectionSelector => ServerSelector;

    public WorkspaceView()
    {
        InitializeComponent();
        KeyboardAccelerator send = new() { Key = VirtualKey.Enter, Modifiers = VirtualKeyModifiers.Control };
        send.Invoked += async (_, args) =>
        {
            args.Handled = true;
            await SendTaskAsync().ConfigureAwait(true);
        };
        DraftBox.KeyboardAccelerators.Add(send);
        DraftBox.TextChanged += (_, _) =>
        {
            if (_live is not null)
            {
                _live.Draft = DraftBox.Text;
            }

            UpdateSend();
        };
        ActualThemeChanged += (_, _) => RefreshThemeProjections();
        ConversationScroll.SizeChanged += (_, _) =>
        {
            bool compact = ConversationScroll.ActualHeight is > 0 and < 260;
            if (compact != _compactConversation)
            {
                _compactConversation = compact;
                if (_live is not null)
                {
                    LiveChanged(this, EventArgs.Empty);
                }
            }
        };
        ModelSelector.SelectionChanged += (_, _) =>
        {
            if (CurrentTask is { Busy: false, Active: false } state && ModelSelector.SelectedItem is TaskModel model)
            {
                state.Model = model;
            }

            UpdateSend();
        };
        AgentSelector.SelectionChanged += (_, _) =>
        {
            if (CurrentTask is { Busy: false, Active: false } state && AgentSelector.SelectedItem is TaskAgent agent)
            {
                state.Agent = agent.Id;
            }

            UpdateSend();
        };
    }

    internal void FocusWorkspace() => ProjectsToggle.Focus(FocusState.Programmatic);

    private void RefreshThemeProjections()
    {
        _composerProjection = "";
        _attentionKey = "";
        _catalogKey = "";
        _historyMoreVisible = false;
        _entryKeys.Clear();
        _entryHashes.Clear();
        _entryControls.Clear();
        ConversationEntries.Children.Clear();
        if (_live is not null)
        {
            LiveChanged(this, EventArgs.Empty);
        }
    }

    internal async Task OpenAttentionAsync(AttentionTargetPresentation target)
    {
        if (_live is null)
        {
            return;
        }

        await _live.OpenProjectAsync(target.Directory).ConfigureAwait(true);
        await _live.SelectAttentionSessionAsync(target.SessionId).ConfigureAwait(true);
        FocusPendingRequests();
    }

    internal void FocusPendingRequests()
    {
        if (PendingRequests.Children.Count == 0)
        {
            return;
        }

        PendingRequestsHeading.StartBringIntoView();
        if (_requestActions.Count > 0)
        {
            _requestActions[0].Focus(FocusState.Programmatic);
        }
    }

    private async void NewTask_ClickAsync(object sender, RoutedEventArgs args)
    {
        if (_live is not null)
        {
            await _live.SelectSessionAsync(null).ConfigureAwait(true);
            DraftBox.Focus(FocusState.Programmatic);
        }
    }

    private void Settings_Click(object sender, RoutedEventArgs args) => SettingsRequested?.Invoke(this, EventArgs.Empty);
    private void Projects_Click(object sender, RoutedEventArgs args) => ProjectsLayout.IsPaneOpen = !ProjectsLayout.IsPaneOpen;
    private void Details_Click(object sender, RoutedEventArgs args) => DetailsLayout.IsPaneOpen = !DetailsLayout.IsPaneOpen;

    private void CloseDetails_Click(object sender, RoutedEventArgs args)
    {
        _expanded = false;
        UpdateDetailsLayout();
        DetailsLayout.IsPaneOpen = false;
        DetailsToggle.Focus(FocusState.Programmatic);
    }

    private void Expand_Click(object sender, RoutedEventArgs args)
    {
        _expanded = !_expanded;
        UpdateDetailsLayout();
        DetailsToggle.Focus(FocusState.Programmatic);
    }

    private async void AddProject_ClickAsync(object sender, RoutedEventArgs args) => await ShowAddProjectDialogAsync().ConfigureAwait(true);

    private async Task ShowAddProjectDialogAsync()
    {
        if (_live is not { IsConnected: true } live || XamlRoot is null)
        {
            return;
        }

        TextBox input = new()
        {
            Header = "Server-side directory",
            PlaceholderText = "Path on the selected server",
            Text = live.Location?.Directory ?? "",
            MaxLength = 4096,
        };
        AutomationProperties.SetName(input, "Server-side project directory");
        ContentDialog dialog = new()
        {
            Title = "Add project",
            Content = new StackPanel
            {
                Spacing = ShellUi.Dimension("ShellSpaceSmall"),
                Children =
                {
                    ShellUi.Text("Open a directory that exists on the selected server. The client does not read this path locally.", "ShellSecondaryTextStyle"),
                    input,
                },
            },
            PrimaryButtonText = "Add project",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            IsPrimaryButtonEnabled = !string.IsNullOrWhiteSpace(input.Text),
            XamlRoot = XamlRoot,
        };
        input.TextChanged += (_, _) => dialog.IsPrimaryButtonEnabled = !string.IsNullOrWhiteSpace(input.Text);
        if (await dialog.ShowAsync() == ContentDialogResult.Primary && !string.IsNullOrWhiteSpace(input.Text))
        {
            await live.OpenProjectAsync(input.Text).ConfigureAwait(true);
            AddProjectButton.Focus(FocusState.Programmatic);
        }
    }

    private void TaskMenu_Click(object sender, RoutedEventArgs args)
    {
        MenuFlyout menu = new();
        MenuFlyoutItem refresh = new() { Text = "Refresh task state", Icon = new FontIcon { Glyph = ShellUi.Glyph("ShellIconRefresh") } };
        refresh.Click += async (_, _) => await RefreshCurrentTaskAsync().ConfigureAwait(true);
        refresh.IsEnabled = _live?.SelectedSessionId is not null;
        menu.Items.Add(refresh);
        if (_live?.Tasks is { CanRetryRecovery: true })
        {
            MenuFlyoutItem retry = new() { Text = "Retry live recovery", Icon = new FontIcon { Glyph = ShellUi.Glyph("ShellIconRefresh") } };
            retry.Click += async (_, _) => await RetryRecoveryAsync().ConfigureAwait(true);
            menu.Items.Add(retry);
        }

        if (_live?.Location is not null)
        {
            MenuFlyoutItem sessions = new() { Text = "Refresh sessions", Icon = new FontIcon { Glyph = ShellUi.Glyph("ShellIconHistory") } };
            sessions.Click += async (_, _) => await _live.RefreshSessionsAsync().ConfigureAwait(true);
            sessions.IsEnabled = _live.IsConnected && !_live.IsLoading;
            menu.Items.Add(sessions);
        }

        if (menu.Items.Count == 0)
        {
            menu.Items.Add(new MenuFlyoutItem { Text = "No task actions available", IsEnabled = false });
        }

        menu.ShowAt(TaskMenuButton);
    }

    private void ProjectsLayout_SizeChanged(object sender, SizeChangedEventArgs args)
    {
        bool inline = args.NewSize.Width >= ShellUi.Dimension("ShellProjectsInlineThreshold");
        if (_projectsInline != inline)
        {
            _projectsInline = inline;
            ProjectsLayout.DisplayMode = inline ? SplitViewDisplayMode.Inline : SplitViewDisplayMode.Overlay;
            ProjectsLayout.IsPaneOpen = inline;
        }
    }

    private void DetailsLayout_SizeChanged(object sender, SizeChangedEventArgs args)
    {
        ComposerScroll.MaxHeight = Math.Min(ShellUi.Dimension("ShellComposerMaximumHeight"), args.NewSize.Height * 0.4);
        PendingRequestsScroll.MaxHeight = Math.Min(320, Math.Max(120, args.NewSize.Height * 0.36));
        bool inline = args.NewSize.Width >= ShellUi.Dimension("ShellInspectorInlineThreshold");
        if (_detailsInline != inline)
        {
            _detailsInline = inline;
            DetailsLayout.IsPaneOpen = inline;
        }

        UpdateDetailsLayout();
    }

    private void UpdateDetailsLayout()
    {
        DetailsLayout.DisplayMode = _expanded || _detailsInline == true ? SplitViewDisplayMode.Inline : SplitViewDisplayMode.Overlay;
        double available = Math.Max(0, DetailsLayout.ActualWidth);
        DetailsLayout.OpenPaneLength = _expanded ? available : Math.Min(_detailsWidth, Math.Max(0, available - 48));
        DetailsRegion.Width = DetailsLayout.OpenPaneLength;
        CentralWorkspace.Visibility = _expanded ? Visibility.Collapsed : Visibility.Visible;
        if (ExpandButton.Content is FontIcon icon)
        {
            icon.Glyph = ShellUi.Glyph(_expanded ? "ShellIconRestore" : "ShellIconExpand");
        }

        AutomationProperties.SetName(ExpandButton, _expanded ? "Restore Details" : "Expand Details");
        DetailsGrip.IsEnabled = !_expanded && _detailsInline == true;
    }

    private void ResizeDetails(double delta)
    {
        double minimum = ShellUi.Dimension("ShellDetailsMinimumWidth");
        _detailsWidth = Math.Clamp(_detailsWidth - delta, minimum, Math.Max(minimum, DetailsLayout.ActualWidth - ShellUi.Dimension("ShellConversationMinimumWidth")));
        UpdateDetailsLayout();
    }

    private void DetailsGrip_DragDelta(object sender, DragDeltaEventArgs args) => ResizeDetails(args.HorizontalChange);

    private void DetailsGrip_KeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (args.Key is VirtualKey.Left or VirtualKey.Right)
        {
            ResizeDetails(args.Key == VirtualKey.Left ? -24 : 24);
            args.Handled = true;
        }
    }
}
