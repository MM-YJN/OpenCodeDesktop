using System;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

using OpenCodeDesktop.Core.Connections;
using OpenCodeDesktop.Core.Workspaces;
using OpenCodeDesktop.Presentation;

namespace OpenCodeDesktop.Views;

public sealed partial class SettingsView : UserControl
{
    private readonly ConnectionView _connectionsView;
    private readonly LiveWorkspace _workspace;
    private bool _refreshingNotifications;
    internal string NotificationStatus { set => NotificationStatusText.Text = value; }
    public event EventHandler? ReturnRequested;

    public SettingsView(ServerConnections connections, ILocalServerDiscovery localDiscovery, LiveWorkspace workspace)
    {
        _workspace = workspace;
        InitializeComponent();
        _connectionsView = new ConnectionView(connections, localDiscovery);
        ConnectionsHost.Content = _connectionsView;
        Sections.SelectedIndex = 0;
        ActualThemeChanged += (_, _) =>
        {
            if (Sections.SelectedIndex is > 0 and < 5)
            {
                BuildUnavailable(Sections.SelectedIndex);
            }
        };
    }

    public void ClearPassword() => _connectionsView.Deactivate();
    public void Shutdown() => _connectionsView.Shutdown();

    public void Activate()
    {
        if (Sections.SelectedIndex == 0)
        {
            _connectionsView.Activate();
        }
    }

    public void FocusInitial() => ReturnButton.Focus(FocusState.Programmatic);

    public void ShowConnections(ServerProfile? profile)
    {
        Sections.SelectedIndex = 0;
        _connectionsView.Edit(profile);
        DispatcherQueue.TryEnqueue(() => _connectionsView.FocusInitial());
    }

    public void ShowConnectionsSection()
    {
        Sections.SelectedIndex = 0;
        DispatcherQueue.TryEnqueue(() => _connectionsView.FocusInitial());
    }

    private void Sections_Changed(object sender, SelectionChangedEventArgs args)
    {
        if (Pages is null)
        {
            return;
        }

        int index = Sections.SelectedIndex;
        if (index != 0)
        {
            ClearPassword();
        }
        else if (IsLoaded && Visibility == Visibility.Visible)
        {
            _connectionsView.Activate();
        }

        bool connections = index == 0;
        ConnectionsHost.Visibility = connections ? Visibility.Visible : Visibility.Collapsed;
        UnavailableHost.Visibility = index is 0 or 5 ? Visibility.Collapsed : Visibility.Visible;
        NotificationsHost.Visibility = index == 5 ? Visibility.Visible : Visibility.Collapsed;
        if (index == 5) { RefreshNotificationPreferences(); }
        if (index is > 0 and < 5)
        {
            BuildUnavailable(index);
        }
    }

    private void BuildUnavailable(int index)
    {
        SettingsSectionPresentation section = ShellPresentation.Section(index);
        UnavailableHost.Content = ShellUi.StatePanel(
            "ShellIconInfo",
            section.Title,
            section.Description,
            ShellTone.Info,
            ShellUi.LabelButton("Back to Connections", "ShellIconServer", (_, _) => Sections.SelectedIndex = 0));
    }

    private void Return_Click(object sender, RoutedEventArgs args) => ReturnRequested?.Invoke(this, EventArgs.Empty);

    internal void RefreshNotificationPreferences()
    {
        _refreshingNotifications = true;
        CompletionNotifications.IsOn = _workspace.NotificationPreferences.Completion;
        AttentionNotifications.IsOn = _workspace.NotificationPreferences.Attention;
        _refreshingNotifications = false;
    }

    private void Notifications_Toggled(object sender, RoutedEventArgs args)
    {
        if (!_refreshingNotifications && CompletionNotifications is not null && AttentionNotifications is not null)
        {
            _workspace.SetNotificationPreferences(new(CompletionNotifications.IsOn, AttentionNotifications.IsOn));
        }
    }
}
