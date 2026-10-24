using System;

using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

using OpenCodeDesktop.Core.Connections;
using OpenCodeDesktop.Core.Workspaces;
using OpenCodeDesktop.Views;

using Windows.Graphics;

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
        _settingsView = new SettingsView(connections, localDiscovery);
        InitializeComponent();
        ShellContentHost.Children.Add(_workspaceView);
        ShellContentHost.Children.Add(_settingsView);
        _settingsView.Visibility = Visibility.Collapsed;
        _workspaceView.SettingsRequested += (_, _) => ShowSettings(true);
        _settingsView.ReturnRequested += (_, _) => ShowSettings(false);
        _connections.Changed += Connections_Changed;
        ShellRoot.ActualThemeChanged += (_, _) => UpdateConnectionStatus();
        UpdateConnectionStatus();
        Closed += MainWindow_Closed;
        _persistence.Changed += PersistenceChanged;
        AppWindow.Closing += AppWindow_ClosingAsync;
    }

    private void ShowSettings(bool show)
    {
        if (!show)
        {
            _settingsView.ClearPassword();
        }

        _workspaceView.Visibility = show ? Visibility.Collapsed : Visibility.Visible;
        _settingsView.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (show)
        {
            _settingsView.Activate();
        }

        (show ? (Control)_settingsView : _workspaceView).Focus(FocusState.Programmatic);
    }

    private void Connections_Changed(object? sender, EventArgs args) => UpdateConnectionStatus();

    private void UpdateConnectionStatus()
    {
        ActiveServer? active = _connections.Active;
        string status = active is null ? "Disconnected" : "Connected · " + active.Profile.Name + " · " + active.Profile.Address.Display + " · OpenCode " + active.Version;
        if (_connections.IsConnecting)
        {
            status += " · Connecting to another server…";
        }

        ConnectionStatusText.Text = status;
        AutomationProperties.SetName(ConnectionStatusText, "Server connection: " + ConnectionStatusText.Text);
        ConnectionStatusText.Foreground = (Brush)Application.Current.Resources[active is null ? "ShellDisconnectedBrush" : "ShellConnectedBrush"];
        ServerSelectorPresenter.Update(_workspaceView.ConnectionSelector, _connections, profile =>
        {
            ShowSettings(true);
            _settingsView.ShowConnections(profile);
        });
    }

    private async void ShellRoot_LoadedAsync(object sender, RoutedEventArgs args)
    {
        ShellRoot.Loaded -= ShellRoot_LoadedAsync;
        ShellRoot.XamlRoot.Changed += XamlRoot_Changed;
        UpdateWindowMinimumSize();
        AppWindow.ResizeClient(new SizeInt32(
            ScaleDimension("ShellWindowInitialWidth"),
            ScaleDimension("ShellWindowInitialHeight")));
        _workspaceView.Focus(FocusState.Programmatic);
        await _persistence.StartAsync().ConfigureAwait(true);
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

    private int ScaleDimension(string key) => (int)Math.Ceiling((double)Application.Current.Resources[key] * _rasterizationScale);

    private void PersistenceChanged(object? sender, EventArgs args)
    {
        var state = Presentation.WorkspacePersistencePresentation.From(_persistence);
        PersistenceNotice.IsOpen = state.Message is not null;
        PersistenceNotice.Message = state.Message ?? "";
        PersistenceNotice.Severity = state.IsError ? InfoBarSeverity.Warning : InfoBarSeverity.Informational;
        RestoreProgress.IsActive = state.IsRestoring;
        RestoreRetry.Visibility = state.CanRetry ? Visibility.Visible : Visibility.Collapsed;
        SaveRetry.Visibility = state.CanSave ? Visibility.Visible : Visibility.Collapsed;
        SettingsReset.Visibility = state.CanReset ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void RestoreRetry_ClickAsync(object sender, RoutedEventArgs args) => await _persistence.RetryAsync().ConfigureAwait(true);
    private void SaveRetry_Click(object sender, RoutedEventArgs args) => _persistence.RetrySave();
    private async void SettingsReset_ClickAsync(object sender, RoutedEventArgs args) => await _persistence.ResetAsync().ConfigureAwait(true);
    private void Connections_Click(object sender, RoutedEventArgs args) => ShowSettings(true);

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
        _persistence.Changed -= PersistenceChanged;
        _persistence.Dispose();
        AppWindow.Closing -= AppWindow_ClosingAsync;
        _settingsView.ClearPassword();
        _settingsView.Shutdown();
        _workspaceView.Unbind();
        _liveWorkspace.Dispose();
        _connections.Changed -= Connections_Changed;
        _connections.Dispose();
        if (ShellRoot.XamlRoot is { } root)
        {
            root.Changed -= XamlRoot_Changed;
        }
    }
}
