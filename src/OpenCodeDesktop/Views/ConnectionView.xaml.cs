using System;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

using OpenCodeDesktop.Core.Connections;

using static OpenCodeDesktop.Views.WorkspaceControls;

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
            Grid row = new() { ColumnSpacing = 16 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            StackPanel description = Stack();
            description.Spacing = 4;
            description.Children.Add(Text(profile.Name));
            description.Children.Add(Text(profile.Address.Display));
            TextBlock state = Text(active ? "✓ Active · OpenCode " + _connections.Active!.Version
                : connecting ? "Connecting…" : "Not connected");
            description.Children.Add(state);
            row.Children.Add(description);
            StackPanel actions = Stack();
            Button select = Button(active ? "View" : "Connect", () => Edit(profile));
            select.IsEnabled = !busy;
            AutomationProperties.SetName(select, (active ? "View " : "Connect to ") + profile.Name);
            actions.Children.Add(select);
            if (!active)
            {
                Button remove = Button("Remove", () =>
                {
                    if (_connections.Remove(profile.Id) && _editingId == profile.Id)
                    {
                        Edit(null);
                    }
                });
                remove.IsEnabled = !busy;
                AutomationProperties.SetName(remove, "Remove " + profile.Name);
                actions.Children.Add(remove);
            }

            Grid.SetColumn(actions, 1);
            row.Children.Add(actions);
            Border card = Card(row);
            card.Padding = new Thickness(16);
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
