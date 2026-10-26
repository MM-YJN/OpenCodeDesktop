using System;
using System.Linq;
using System.Threading.Tasks;

using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

using OpenCodeDesktop.Core.Connections;
using OpenCodeDesktop.Core.Workspaces;
using OpenCodeDesktop.Presentation;
using OpenCodeDesktop.Views;

using Windows.Graphics;
using Windows.UI.ViewManagement;

namespace OpenCodeDesktop;

/// <summary>The single-window presentation shell, sharing one connection use case across its views.</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001:Types that own disposable fields should be disposable", Justification = "WinUI Window lifetime is owned by Closed, which unbinds and disposes the workspace and connection use cases.")]
public sealed partial class MainWindow : Window
{
    private readonly WorkspaceView _workspaceView = new();
    private readonly SettingsView _settingsView;
    private readonly ServerConnections _connections;
    private readonly LiveWorkspace _liveWorkspace;
    private double _rasterizationScale;
    private readonly WorkspacePersistence _persistence;
    private bool _closeReady;
    private bool _closing;

    public MainWindow(ServerConnections connections, ILocalServerDiscovery localDiscovery, IWorkspaceSettingsStore settings, ICredentialStore credentials)
    {
        _connections = connections;
        _liveWorkspace = new LiveWorkspace(connections);
        _persistence = new(connections, _liveWorkspace, settings, credentials);
        _workspaceView.Bind(_liveWorkspace);
        _settingsView = new SettingsView(connections, localDiscovery, _liveWorkspace);
        InitializeComponent();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        ShellContentHost.Children.Add(_workspaceView);
        ShellContentHost.Children.Add(_settingsView);
        _settingsView.Visibility = Visibility.Collapsed;
        _workspaceView.SettingsRequested += (_, _) => ShowSettings(true);
        _workspaceView.ProjectsPaneChanged += WorkspaceView_PaneChanged;
        _settingsView.ReturnRequested += (_, _) => ShowSettings(false);
        _connections.Changed += Connections_Changed;
        _liveWorkspace.Changed += Workspace_Changed;
        ShellRoot.ActualThemeChanged += (_, _) =>
        {
            UpdateConnectionStatus();
            UpdateAttention();
            UpdateTitleBarTheme();
            PersistenceChanged(this, EventArgs.Empty);
        };
        UpdateConnectionStatus();
        UpdateAttention();
        UpdateTitleBarTheme();
        Closed += MainWindow_Closed;
        _persistence.Changed += PersistenceChanged;
        AppWindow.Closing += AppWindow_ClosingAsync;
        InitializeNotifications();
        _settingsView.NotificationStatus = _notifications.Status;
        _liveWorkspace.Changed += ObserveNotificationsAsync;
    }

    private void ShowSettings(bool show)
    {
        _workspaceView.Visibility = show ? Visibility.Collapsed : Visibility.Visible;
        _settingsView.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        ProjectsPaneToggle.Visibility = show ? Visibility.Collapsed : Visibility.Visible;
        if (show)
        {
            _notifications.RefreshStatus();
            _settingsView.NotificationStatus = _notifications.Status;
            _settingsView.Activate();
            _settingsView.FocusInitial();
        }
        else
        {
            _settingsView.ClearPassword();
            _workspaceView.FocusWorkspace();
        }
    }

    private void ShowConnections(ServerProfile? profile)
    {
        ShowSettings(true);
        _settingsView.ShowConnections(profile);
    }

    private void ProjectsPaneToggle_Click(object sender, RoutedEventArgs args) => _workspaceView.ToggleProjectsPane();

    private void WorkspaceView_PaneChanged(object? sender, EventArgs args) => SyncProjectsPaneToggle();

    private void ProjectsAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (_workspaceView.Visibility == Visibility.Visible)
        {
            _workspaceView.ToggleProjectsPane();
        }

        args.Handled = true;
    }

    private void SyncProjectsPaneToggle()
    {
        bool open = _workspaceView.IsProjectsPaneOpen;
        ProjectsPaneToggle.IsChecked = open;
        ShellUi.Tooltip(ProjectsPaneToggle, open ? "Hide the Projects pane (Ctrl+B)." : "Show the Projects pane (Ctrl+B).");
    }

    private void Connections_Changed(object? sender, EventArgs args) => UpdateConnectionStatus();
    private void Workspace_Changed(object? sender, EventArgs args) => UpdateAttention();

    private void UpdateConnectionStatus()
    {
        ServerProfile? connecting = _connections.ConnectingId is { } id
            ? _connections.Profiles.FirstOrDefault(profile => profile.Id == id)
            : null;
        ShellStatusPresentation status = ShellPresentation.Connection(_connections.Active, connecting, _liveWorkspace.HasConnected);
        ConnectionStatusLabel.Text = status.Label;
        ConnectionStatusText.Text = status.Detail;
        ConnectionStatusLabel.Foreground = status.Tone == ShellTone.Neutral ? ShellUi.Brush("ShellTextSecondaryBrush") : ShellUi.ToneBrush(status.Tone);
        ConnectionDot.Background = ShellUi.ToneBrush(status.Tone);
        AutomationProperties.SetName(ConnectionStatusButton, "Server connection. " + status.AutomationName + ". Open connection settings.");
        ShellUi.Tooltip(ConnectionStatusButton, status.AutomationName + "\nOpen Settings > Connections.");
        ServerSelectorPresenter.Update(_workspaceView.ConnectionSelector, _connections, ShowConnections);
    }

    private void UpdateAttention()
    {
        var targets = ShellPresentation.Attention(_liveWorkspace);
        int count = targets.Sum(target => target.Count);
        AttentionText.Text = count == 0 ? "No pending requests" : "Needs attention: " + count;
        AttentionIcon.Foreground = count == 0 ? ShellUi.Brush("ShellTextSecondaryBrush") : ShellUi.Brush("ShellWarningBrush");
        AttentionButton.IsEnabled = count > 0;
        AutomationProperties.SetName(AttentionButton, count == 0 ? "Attention: no pending requests" : "Attention: " + count + " pending requests");
        ShellUi.Tooltip(AttentionButton, count == 0 ? "Permission and question requests appear here." : "Review pending permission and question requests.");
    }

    private void Attention_Click(object sender, RoutedEventArgs args)
    {
        var targets = ShellPresentation.Attention(_liveWorkspace);
        if (targets.Count == 0)
        {
            return;
        }

        MenuFlyout flyout = new();
        foreach (AttentionTargetPresentation target in targets)
        {
            MenuFlyoutItem item = new() { Text = target.Label, Tag = target };
            item.Click += async (_, _) => await OpenAttentionAsync(target).ConfigureAwait(true);
            flyout.Items.Add(item);
        }

        flyout.ShowAt(AttentionButton);
    }

    private async Task OpenAttentionAsync(AttentionTargetPresentation target)
    {
        ShowSettings(false);
        await _workspaceView.OpenAttentionAsync(target).ConfigureAwait(true);
    }

    private async void ShellRoot_LoadedAsync(object sender, RoutedEventArgs args)
    {
        ShellRoot.Loaded -= ShellRoot_LoadedAsync;
        ShellRoot.XamlRoot.Changed += XamlRoot_Changed;
        UpdateWindowMinimumSize();
        ResizeInitialWindow();
        _workspaceView.FocusWorkspace();
        await _persistence.StartAsync().ConfigureAwait(true);
        _notificationReady.TrySetResult();
        _settingsView.RefreshNotificationPreferences();
    }

    private void ResizeInitialWindow()
    {
        var display = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary);
        RectInt32 work = display.WorkArea;
        int width = Math.Min(ScaleDimension("ShellWindowInitialWidth"), work.Width);
        int height = Math.Min(ScaleDimension("ShellWindowInitialHeight"), work.Height);
        AppWindow.ResizeClient(new SizeInt32(width, height));
        AppWindow.Move(new PointInt32(work.X + Math.Max(0, (work.Width - width) / 2), work.Y + Math.Max(0, (work.Height - height) / 2)));
    }

    private void XamlRoot_Changed(XamlRoot sender, XamlRootChangedEventArgs args)
    {
        if (sender.RasterizationScale != _rasterizationScale)
        {
            UpdateWindowMinimumSize();
        }
    }

    private void UpdateWindowMinimumSize()
    {
        _rasterizationScale = ShellRoot.XamlRoot.RasterizationScale;
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.PreferredMinimumWidth = ScaleDimension("ShellWindowMinimumWidth");
            presenter.PreferredMinimumHeight = ScaleDimension("ShellWindowMinimumHeight");
        }
    }

    private int ScaleDimension(string key) => (int)Math.Ceiling(ShellUi.Dimension(key) * _rasterizationScale);

    private void UpdateTitleBarTheme()
    {
        if (!AppWindowTitleBar.IsCustomizationSupported())
        {
            return;
        }

        AppWindowTitleBar bar = AppWindow.TitleBar;
        bar.PreferredHeightOption = TitleBarHeightOption.Tall;
        AccessibilitySettings accessibility = new();
        UISettings settings = new();
        bool highContrast = accessibility.HighContrast;
        bool dark = ShellRoot.ActualTheme == ElementTheme.Dark;
        Windows.UI.Color transparent = Windows.UI.Color.FromArgb(0, 0, 0, 0);
        Windows.UI.Color foreground = highContrast
            ? settings.GetColorValue(UIColorType.Foreground)
            : dark ? Windows.UI.Color.FromArgb(255, 255, 255, 255) : Windows.UI.Color.FromArgb(255, 26, 26, 26);
        Windows.UI.Color hover = highContrast
            ? Windows.UI.Color.FromArgb(72, foreground.R, foreground.G, foreground.B)
            : dark ? Windows.UI.Color.FromArgb(32, 255, 255, 255) : Windows.UI.Color.FromArgb(24, 0, 0, 0);
        Windows.UI.Color pressed = highContrast
            ? Windows.UI.Color.FromArgb(120, foreground.R, foreground.G, foreground.B)
            : dark ? Windows.UI.Color.FromArgb(64, 255, 255, 255) : Windows.UI.Color.FromArgb(48, 0, 0, 0);

        // The app draws its own 48 DIP title-bar row hosting the Projects pane
        // toggle; the system only owns the caption buttons, so their background
        // must stay transparent. The Tall option aligns them with the custom row.
        bar.BackgroundColor = transparent;
        bar.InactiveBackgroundColor = transparent;
        bar.ForegroundColor = foreground;
        bar.InactiveForegroundColor = foreground;
        bar.ButtonBackgroundColor = transparent;
        bar.ButtonForegroundColor = foreground;
        bar.ButtonHoverBackgroundColor = hover;
        bar.ButtonHoverForegroundColor = foreground;
        bar.ButtonPressedBackgroundColor = pressed;
        bar.ButtonPressedForegroundColor = foreground;
        bar.ButtonInactiveBackgroundColor = transparent;
        bar.ButtonInactiveForegroundColor = foreground;
    }

    private void PersistenceChanged(object? sender, EventArgs args)
    {
        var state = WorkspacePersistencePresentation.From(_persistence);
        PersistenceBanner.Visibility = state.Message is null ? Visibility.Collapsed : Visibility.Visible;
        PersistenceTitle.Text = state.IsRestoring ? "Restoring your workspace" : state.IsError ? "Saved state needs attention" : "Workspace storage";
        PersistenceMessage.Text = state.Message ?? "";
        PersistenceBanner.Background = ShellUi.Brush(state.IsError ? "ShellWarningSurfaceBrush" : "ShellInfoSurfaceBrush");
        PersistenceIcon.Glyph = ShellUi.Glyph(state.IsError ? "ShellIconWarning" : "ShellIconInfo");
        PersistenceIcon.Foreground = state.IsError ? ShellUi.Brush("ShellWarningBrush") : ShellUi.Brush("ShellAccentBrush");
        RestoreProgress.IsActive = state.IsRestoring;
        RestoreRetry.Visibility = state.CanRetry ? Visibility.Visible : Visibility.Collapsed;
        SaveRetry.Visibility = state.CanSave ? Visibility.Visible : Visibility.Collapsed;
        SettingsReset.Visibility = state.CanReset ? Visibility.Visible : Visibility.Collapsed;
        ConnectionsNoticeAction.Visibility = state.IsError ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void RestoreRetry_ClickAsync(object sender, RoutedEventArgs args) => await _persistence.RetryAsync().ConfigureAwait(true);
    private void SaveRetry_Click(object sender, RoutedEventArgs args) => _persistence.RetrySave();
    private async void SettingsReset_ClickAsync(object sender, RoutedEventArgs args) => await _persistence.ResetAsync().ConfigureAwait(true);
    private void Connections_Click(object sender, RoutedEventArgs args)
    {
        ShowSettings(true);
        _settingsView.ShowConnectionsSection();
    }

    private async void AppWindow_ClosingAsync(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_closeReady)
        {
            return;
        }

        args.Cancel = true;
        if (_closing)
        {
            return;
        }

        _closing = true;
        _persistence.Dispose();
        _connections.Cancel();
        await _persistence.FlushAsync().ConfigureAwait(true);
        _closeReady = true;
        Close();
    }

    private void MainWindow_Closed(object sender, WindowEventArgs args)
    {
        _notificationReady.TrySetResult();
        _liveWorkspace.Changed -= ObserveNotificationsAsync;
        _notifications.Invoked -= NotificationInvoked;
        _notifications.Dispose();
        _persistence.Changed -= PersistenceChanged;
        _persistence.Dispose();
        AppWindow.Closing -= AppWindow_ClosingAsync;
        _settingsView.ClearPassword();
        _settingsView.Shutdown();
        _workspaceView.ProjectsPaneChanged -= WorkspaceView_PaneChanged;
        _workspaceView.Unbind();
        _liveWorkspace.Changed -= Workspace_Changed;
        _liveWorkspace.Dispose();
        _connections.Changed -= Connections_Changed;
        _connections.Dispose();
        if (ShellRoot.XamlRoot is { } root)
        {
            root.Changed -= XamlRoot_Changed;
        }
    }
}
