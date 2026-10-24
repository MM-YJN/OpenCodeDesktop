using System;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

using OpenCodeDesktop.Core.Connections;

namespace OpenCodeDesktop.Views;

public sealed partial class ConnectionView : UserControl
{
    private readonly ServerConnections _connections;
    private Guid? _editingId;
    private bool _showFeedback;

    public ConnectionView(ServerConnections connections)
    {
        _connections = connections;
        InitializeComponent();
        _connections.Changed += (_, _) => Refresh();
        ActualThemeChanged += (_, _) => Refresh();
        Refresh();
    }

    public void ClearPassword() => ServerPassword.Password = "";

    public void Edit(ServerProfile? profile)
    {
        if (_connections.IsConnecting)
        {
            return;
        }

        ClearPassword();
        _editingId = profile?.Id;
        ServerName.Text = profile?.Name ?? "";
        ServerOrigin.Text = profile?.Address.Display ?? "";
        _showFeedback = false;
        Refresh();
        (profile is null ? (Control)ServerName : ServerPassword).Focus(FocusState.Programmatic);
    }

    private void Refresh()
    {
        bool busy = _connections.IsConnecting;
        bool editingActive = _editingId.HasValue && _connections.Active?.Profile.Id == _editingId;
        EmptyState.Visibility = _connections.Profiles.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        RenderProfiles(busy);

        EditorHeading.Text = editingActive ? "Active server" : _editingId.HasValue ? "Edit server" : "Add server";
        ServerName.IsEnabled = ServerOrigin.IsEnabled = ServerPassword.IsEnabled = !busy && !editingActive;
        AddServerButton.IsEnabled = !busy;
        SaveButton.IsEnabled = ConnectButton.IsEnabled = !busy && !editingActive;
        ConnectButton.Content = _connections.Active is null ? "Connect" : "Connect & switch";
        CancelButton.Content = busy ? "Cancel connection" : "Cancel";
        DisconnectButton.Visibility = _connections.Active is null ? Visibility.Collapsed : Visibility.Visible;
        DisconnectButton.IsEnabled = !busy;
        SwitchInformation.Message = editingActive ? "Disconnect this server before editing or removing it."
            : _connections.Active is null ? "Enter an existing local server address and its password to check readiness."
            : "Your current server stays active until the new connection is ready.";
        ConnectingProgress.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        ResultBar.IsOpen = _showFeedback;
        ResultBar.Message = _connections.Message;
        ResultBar.Severity = _connections.LastResult is { } result
            ? result.IsReady ? InfoBarSeverity.Success : result.Outcome == ConnectionOutcome.Cancelled ? InfoBarSeverity.Informational : InfoBarSeverity.Error
            : InfoBarSeverity.Informational;
    }

    private void RenderProfiles(bool busy)
    {
        ServerList.Children.Clear();
        foreach (ServerProfile profile in _connections.Profiles)
        {
            bool active = _connections.Active?.Profile.Id == profile.Id;
            bool connecting = _connections.ConnectingId == profile.Id;
            var card = (Border)((DataTemplate)Resources["ServerProfileTemplate"]).LoadContent();
            ((TextBlock)card.FindName("ProfileName")).Text = profile.Name;
            ((TextBlock)card.FindName("Address")).Text = profile.Address.Display;
            var state = (TextBlock)card.FindName("State");
            state.Text = active ? "✓ Active · OpenCode " + _connections.Active!.Version
                : connecting ? "Connecting…" : "Not connected";
            var select = (Button)card.FindName("Select");
            select.Content = active ? "View" : "Connect";
            select.IsEnabled = !busy;
            select.Click += (_, _) => Edit(profile);
            AutomationProperties.SetName(select, (active ? "View " : "Connect to ") + profile.Name);
            var remove = (Button)card.FindName("Remove");
            remove.Visibility = active ? Visibility.Collapsed : Visibility.Visible;
            remove.IsEnabled = !busy;
            AutomationProperties.SetName(remove, "Remove " + profile.Name);
            remove.Click += (_, _) =>
            {
                if (_connections.Remove(profile.Id) && _editingId == profile.Id)
                {
                    Edit(null);
                }
            };

            if (active)
            {
                card.BorderBrush = (Brush)Application.Current.Resources["ShellAccentBrush"];
                state.Foreground = (Brush)Application.Current.Resources["ShellConnectedBrush"];
            }

            ServerList.Children.Add(card);
        }
    }

    private ServerProfile? Save()
    {
        _showFeedback = true;
        ServerProfile? profile = _connections.Save(_editingId, ServerName.Text, ServerOrigin.Text);
        if (profile is not null)
        {
            _editingId = profile.Id;
            ServerOrigin.Text = profile.Address.Display;
        }

        Refresh();
        return profile;
    }

    private void Add_Click(object sender, RoutedEventArgs args) => Edit(null);
    private void Save_Click(object sender, RoutedEventArgs args)
    {
        Save();
        ClearPassword();
    }

    private async void Connect_ClickAsync(object sender, RoutedEventArgs args)
    {
        if (_connections.IsConnecting)
        {
            return;
        }

        ServerProfile? profile = Save();
        if (profile is null)
        {
            ClearPassword();
            return;
        }

        string password = ServerPassword.Password;
        ClearPassword();
        await _connections.ConnectAsync(profile.Id, password).ConfigureAwait(true);
        if (IsLoaded)
        {
            (ConnectButton.IsEnabled ? ConnectButton : AddServerButton).Focus(FocusState.Programmatic);
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs args)
    {
        ClearPassword();
        if (_connections.IsConnecting)
        {
            _connections.Cancel();
        }
        else
        {
            Edit(null);
        }
    }

    private void Disconnect_Click(object sender, RoutedEventArgs args)
    {
        ClearPassword();
        _connections.Disconnect();
        _showFeedback = true;
        Refresh();
    }
}
