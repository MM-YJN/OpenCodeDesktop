using System;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

using OpenCodeDesktop.Core.Connections;
using OpenCodeDesktop.Presentation;

namespace OpenCodeDesktop.Views;

internal static class ServerSelectorPresenter
{
    public static void Update(Button selector, ServerConnections connections, Action<ServerProfile?> openSettings)
    {
        ActiveServer? active = connections.Active;
        selector.Content = SelectorContent(active);
        AutomationProperties.SetName(selector, active is null
            ? "Switch server: disconnected"
            : "Switch server: " + active.Profile.Name + ", " + active.Profile.Address.Display);
        ShellUi.Tooltip(selector, active is null ? "Select or manage a connection." : "Switch server. The active server stays connected until another is ready.");
        selector.Flyout = BuildFlyout(connections, openSettings);
    }

    private static FrameworkElement SelectorContent(ActiveServer? active)
    {
        if (active is null)
        {
            return ShellUi.Template<StackPanel>("DisconnectedSelectorTemplate");
        }

        Grid content = ShellUi.Template<Grid>("ConnectedSelectorTemplate");
        ShellUi.Part<TextBlock>(content, "Name").Text = active.Profile.Name;
        ShellUi.Part<TextBlock>(content, "Address").Text = active.Profile.Address.Display;
        return content;
    }

    private static Flyout BuildFlyout(ServerConnections connections, Action<ServerProfile?> openSettings)
    {
        Flyout flyout = new() { Placement = FlyoutPlacementMode.BottomEdgeAlignedLeft, ShowMode = FlyoutShowMode.Standard };
        StackPanel content = ShellUi.Template<StackPanel>("ServerFlyoutContentTemplate");
        ShellUi.Part<TextBlock>(content, "Empty").Visibility = connections.Profiles.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        StackPanel profiles = ShellUi.Part<StackPanel>(content, "Profiles");
        foreach (ServerProfile profile in connections.Profiles)
        {
            ConnectionCardPresentation card = ShellPresentation.ConnectionCard(profile, connections.Active, connections.ConnectingId, connections.IsConnecting);
            Button item = ShellUi.Template<Button>("ServerFlyoutRowTemplate");
            item.Style = ShellUi.Resource<Style>(card.IsActive ? "ShellSelectedNavigationRowStyle" : "ShellNavigationRowStyle");
            item.IsEnabled = !connections.IsConnecting;
            FontIcon icon = ShellUi.Part<FontIcon>(item, "Icon");
            icon.Glyph = ShellUi.Glyph(card.IsActive ? "ShellIconCheck" : "ShellIconServer");
            icon.Foreground = ShellUi.Brush(card.IsActive ? "ShellConnectedBrush" : "ShellTextSecondaryBrush");
            ShellUi.Part<TextBlock>(item, "Name").Text = profile.Name;
            ShellUi.Part<TextBlock>(item, "Address").Text = profile.Address.Display;
            ShellUi.Part<ContentControl>(item, "State").Content = ShellUi.Badge(card.StateLabel,
                card.IsActive ? ShellTone.Success : card.IsConnecting ? ShellTone.Info : ShellTone.Neutral,
                card.IsActive ? "ShellIconCheck" : null);
            ServerProfile target = profile;
            item.Click += (_, _) =>
            {
                flyout.Hide();
                openSettings(target);
            };
            AutomationProperties.SetName(item, "Switch to " + profile.Name + ", " + profile.Address.Display + ", " + card.StateLabel);
            profiles.Children.Add(item);
        }

        ShellUi.Part<Button>(content, "Manage").Click += (_, _) =>
        {
            flyout.Hide();
            openSettings(null);
        };
        flyout.Content = content;
        return flyout;
    }
}
