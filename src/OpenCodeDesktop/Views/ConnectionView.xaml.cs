using System;
using System.Linq;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

using OpenCodeDesktop.Core.Connections;
using OpenCodeDesktop.Presentation;

namespace OpenCodeDesktop.Views;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001:Types that own disposable fields should be disposable", Justification = "The owning window invokes Shutdown to cancel, unbind and dispose the page's local environment workflow.")]
public sealed partial class ConnectionView : UserControl
{
    private readonly ServerConnections _connections;
    private readonly LocalEnvironment _local;
    private Guid? _editingId;
    private bool _showFeedback;
    private long _activation;

    public ConnectionView(ServerConnections connections, ILocalServerDiscovery localDiscovery)
    {
        _connections = connections;
        _local = new LocalEnvironment(connections, localDiscovery);
        InitializeComponent();
        ServerOrigin.PlaceholderText = ServerAddress.DefaultOrigin;
        ServerOrigin.Text = ServerAddress.DefaultOrigin;
        DefaultAddressHint.Text = "Leave the address blank to use " + ServerAddress.DefaultOrigin + ", the Windows OpenCode v2 default.";
        _connections.Changed += ConnectionsChanged;
        _local.Changed += LocalChanged;
        ActualThemeChanged += (_, _) => Refresh();
        Refresh();
    }

    public void ClearPassword() => ServerPassword.Password = "";

    public void Activate() => _ = _local.DetectAsync();
    public void Deactivate()
    {
        _activation++;
        ClearPassword();
        _local.Deactivate();
    }

    public void Shutdown()
    {
        _activation++;
        ClearPassword();
        _local.Changed -= LocalChanged;
        _local.Dispose();
        _connections.Changed -= ConnectionsChanged;
    }

    private void ConnectionsChanged(object? sender, EventArgs args) => Refresh();
    private void LocalChanged(object? sender, EventArgs args) => Refresh();

    private async void DetectLocal_ClickAsync(object sender, RoutedEventArgs args) => await _local.DetectAsync().ConfigureAwait(true);

    private async void AddLocal_ClickAsync(object sender, RoutedEventArgs args)
    {
        ClearPassword();
        long activation = _activation;
        await _local.AddAsync(RememberCredentials.IsChecked == true).ConfigureAwait(true);
        if (activation == _activation && IsLoaded && _local.IsAlreadyConnected && _connections.Active is { } active)
        {
            Edit(active.Profile);
        }
    }

    public void Edit(ServerProfile? profile)
    {
        if (_connections.IsConnecting)
        {
            _connections.Cancel();
        }

        ClearPassword();
        _editingId = profile?.Id;
        RememberCredentials.IsChecked = profile?.RememberCredentials ?? true;
        ServerName.Text = profile?.Name ?? "";
        ServerOrigin.Text = profile?.Address.Display ?? ServerAddress.DefaultOrigin;
        _showFeedback = false;
        Refresh();
        EditorSection.StartBringIntoView();
        (profile is null ? (Control)ServerName : ServerPassword).Focus(FocusState.Programmatic);
    }

    private void Refresh()
    {
        bool busy = _connections.IsConnecting || _local.IsAdding;
        bool localStatusChanged = LocalDetectionStatus.Text != _local.Message;
        LocalDetectionStatus.Text = _local.Message;
        LocalDetectionBadge.Text = _local.Detected is { } detected
            ? "OpenCode " + detected.Version
            : _local.IsBusy ? "Checking" : "";
        LocalDetectionBadge.Foreground = _local.Detected is null ? ShellUi.Brush("ShellTextTertiaryBrush") : ShellUi.Brush("ShellConnectedBrush");
        LocalDetectionProgress.Visibility = _local.IsBusy ? Visibility.Visible : Visibility.Collapsed;
        DetectLocalButton.IsEnabled = !_local.IsBusy && !_connections.IsConnecting;
        AddLocalButton.Visibility = _local.Detected is null ? Visibility.Collapsed : Visibility.Visible;
        AddLocalButton.IsEnabled = _local.CanAdd;
        AddLocalButton.Content = _local.IsAlreadyConnected ? "Local environment connected" : "Add local environment";
        if (localStatusChanged)
        {
            FrameworkElementAutomationPeer.FromElement(LocalDetectionStatus)?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
        }

        bool editingActive = _editingId.HasValue && _connections.Active?.Profile.Id == _editingId;
        EmptyState.Visibility = _connections.Profiles.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        RenderProfiles(busy);

        EditorHeading.Text = editingActive ? "Active server" : _editingId.HasValue ? "Edit server" : "Add server";
        ServerName.IsEnabled = ServerOrigin.IsEnabled = ServerPassword.IsEnabled = !busy && !editingActive;
        RememberCredentials.IsEnabled = !busy && !editingActive;
        AddServerButton.IsEnabled = !busy;
        SaveButton.IsEnabled = ConnectButton.IsEnabled = !busy && !editingActive;
        ConnectButton.Content = _connections.Active is null ? "Connect" : "Connect & switch";
        CancelButton.Content = _local.IsBusy ? "Cancel local operation" : busy ? "Cancel connection" : "Close editor";
        SwitchInformation.Message = editingActive ? "Disconnect this server before editing or removing it."
            : _connections.Active is null ? "Enter an existing local server address and its password to check readiness."
            : "Your current server stays active until the new connection is ready.";
        ConnectingProgress.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        RefreshResult();
    }

    private void RefreshResult()
    {
        ResultBar.IsOpen = _showFeedback || _connections.CredentialError is not null;
        ResultBar.Message = _connections.CredentialError ?? _connections.Message;
        ResultBar.Severity = _connections.CredentialError is not null ? InfoBarSeverity.Warning : _connections.LastResult is { } result
            ? result.IsReady ? InfoBarSeverity.Success : result.Outcome == ConnectionOutcome.Cancelled ? InfoBarSeverity.Informational : InfoBarSeverity.Error
            : InfoBarSeverity.Informational;
    }

    private void RenderProfiles(bool busy)
    {
        ServerList.Children.Clear();
        foreach (ServerProfile profile in _connections.Profiles)
        {
            ConnectionCardPresentation presentation = ShellPresentation.ConnectionCard(profile, _connections.Active, _connections.ConnectingId, busy);
            Border card = new()
            {
                Style = ShellUi.Resource<Style>("ShellCardStyle"),
                Padding = new Thickness(14, 12, 12, 12),
                Background = presentation.IsActive ? ShellUi.Brush("ShellAccentSurfaceBrush") : ShellUi.Brush("ShellCardSurfaceBrush"),
                BorderBrush = presentation.IsActive ? ShellUi.Brush("ShellAccentBrush") : ShellUi.Brush("ShellBorderBrush"),
            };

            Grid layout = new() { ColumnSpacing = 12 };
            layout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            layout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            Border iconSurface = new()
            {
                Width = 32,
                Height = 32,
                CornerRadius = ShellUi.Resource<CornerRadius>("ShellCornerRadiusMedium"),
                Background = presentation.IsActive ? ShellUi.Brush("ShellConnectedSurfaceBrush") : ShellUi.Brush("ShellSubtleSurfaceBrush"),
                BorderBrush = presentation.IsActive ? ShellUi.Brush("ShellConnectedBrush") : ShellUi.Brush("ShellBorderBrush"),
                BorderThickness = ShellUi.Resource<Thickness>("ShellHairlineThickness"),
                Child = ShellUi.Icon(presentation.IsActive ? "ShellIconCheck" : "ShellIconServer", ShellUi.Dimension("ShellIconMedium"),
                    presentation.IsActive ? ShellUi.Brush("ShellConnectedBrush") : ShellUi.Brush("ShellTextSecondaryBrush")),
                VerticalAlignment = VerticalAlignment.Top,
            };
            layout.Children.Add(iconSurface);

            StackPanel details = ShellUi.Stack(4);
            Grid.SetColumn(details, 1);
            TextBlock name = ShellUi.Text(presentation.Name, "ShellBodyStrongTextStyle");
            TextBlock address = ShellUi.Text(presentation.Address, "ShellTertiaryTextStyle", TextWrapping.NoWrap);
            address.TextTrimming = TextTrimming.CharacterEllipsis;
            ShellUi.Tooltip(address, presentation.Address);
            StackPanel stateRow = ShellUi.Stack(8, Orientation.Horizontal);
            stateRow.Children.Add(ShellUi.Badge(presentation.StateLabel, presentation.IsActive ? ShellTone.Success : presentation.IsConnecting ? ShellTone.Info : ShellTone.Neutral,
                presentation.IsActive ? "ShellIconCheck" : presentation.IsConnecting ? "ShellIconClock" : null));
            if (presentation.IsActive && _connections.Active is { } active)
            {
                stateRow.Children.Add(ShellUi.Text("OpenCode " + active.Version, "ShellCaptionTextStyle", TextWrapping.NoWrap));
            }

            details.Children.Add(name);
            details.Children.Add(address);
            details.Children.Add(stateRow);
            layout.Children.Add(details);

            StackPanel actions = ShellUi.Stack(8, Orientation.Horizontal);
            Grid.SetColumn(actions, 2);
            actions.VerticalAlignment = VerticalAlignment.Center;
            Button select = ShellUi.LabelButton(presentation.PrimaryActionLabel, null, (_, _) => Edit(profile),
                presentation.IsActive ? "ShellSecondaryButtonStyle" : "ShellPrimaryButtonStyle", presentation.AutomationName);
            actions.Children.Add(select);
            if (presentation.IsActive)
            {
                Button disconnect = ShellUi.LabelButton("Disconnect", null, (_, _) =>
                {
                    ClearPassword();
                    _connections.Disconnect();
                    _showFeedback = true;
                    Refresh();
                }, "ShellQuietButtonStyle", "Disconnect active server");
                actions.Children.Add(disconnect);
            }
            else if (presentation.CanRemove)
            {
                Button more = ShellUi.IconButton("ShellIconMore", "More actions for " + profile.Name, (_, _) => { });
                more.Flyout = BuildProfileMenu(profile);
                actions.Children.Add(more);
            }

            layout.Children.Add(actions);
            card.Child = layout;
            AutomationProperties.SetName(card, presentation.AutomationName);
            ServerList.Children.Add(card);
        }
    }

    private MenuFlyout BuildProfileMenu(ServerProfile profile)
    {
        MenuFlyout menu = new();
        MenuFlyoutItem edit = new() { Text = "Edit server", Icon = new FontIcon { Glyph = ShellUi.Glyph("ShellIconEdit") } };
        edit.Click += (_, _) => Edit(profile);
        menu.Items.Add(edit);
        MenuFlyoutItem remove = new() { Text = "Remove server", Icon = new FontIcon { Glyph = ShellUi.Glyph("ShellIconDelete") } };
        remove.Click += (_, _) =>
        {
            if (_connections.Remove(profile.Id) && _editingId == profile.Id)
            {
                Edit(null);
            }
        };
        menu.Items.Add(remove);
        return menu;
    }

    private ServerProfile? Save()
    {
        _showFeedback = true;
        ServerProfile? profile = _connections.Save(_editingId, ServerName.Text, ServerOrigin.Text, RememberCredentials.IsChecked == true);
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
        if (_local.IsBusy)
        {
            _local.Deactivate();
        }

        if (_connections.IsConnecting)
        {
            _connections.Cancel();
        }
        else
        {
            Edit(null);
        }
    }

    internal void FocusInitial() => AddServerButton.Focus(FocusState.Programmatic);
}
