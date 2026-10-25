using System;
using System.Collections.Generic;
using System.Linq;

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
            StackPanel empty = ShellUi.Stack(ShellUi.Dimension("ShellSpaceSmall"), Orientation.Horizontal);
            empty.Children.Add(ShellUi.Icon("ShellIconServer", ShellUi.Dimension("ShellIconSmall"), ShellUi.Brush("ShellTextSecondaryBrush")));
            empty.Children.Add(ShellUi.Text("Select server", "ShellBodyTextStyle"));
            return empty;
        }

        Grid content = new() { ColumnSpacing = 8 };
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        content.Children.Add(ShellUi.StatusIndicator(ShellUi.Brush("ShellConnectedBrush")));
        StackPanel details = ShellUi.Stack(1);
        Grid.SetColumn(details, 1);
        details.Children.Add(ShellUi.Text(active.Profile.Name, "ShellBodyTextStyle", TextWrapping.NoWrap));
        TextBlock address = ShellUi.Text(active.Profile.Address.Display, "ShellTertiaryTextStyle", TextWrapping.NoWrap);
        address.TextTrimming = TextTrimming.CharacterEllipsis;
        details.Children.Add(address);
        content.Children.Add(details);
        FontIcon chevron = ShellUi.Icon("ShellIconChevronDown", ShellUi.Dimension("ShellIconSmall"), ShellUi.Brush("ShellTextSecondaryBrush"));
        Grid.SetColumn(chevron, 2);
        content.Children.Add(chevron);
        return content;
    }

    private static Flyout BuildFlyout(ServerConnections connections, Action<ServerProfile?> openSettings)
    {
        Flyout flyout = new() { Placement = FlyoutPlacementMode.BottomEdgeAlignedLeft, ShowMode = FlyoutShowMode.Standard };
        StackPanel content = ShellUi.Stack(ShellUi.Dimension("ShellSpaceTiny"));
        content.MinWidth = 300;
        content.Padding = new Thickness(8, 6, 8, 6);
        content.Children.Add(ShellUi.Text("SWITCH SERVER", "ShellEyebrowTextStyle"));
        if (connections.Profiles.Count == 0)
        {
            content.Children.Add(ShellUi.Text("No saved servers. Add one in Connections.", "ShellSecondaryTextStyle"));
        }

        foreach (ServerProfile profile in connections.Profiles)
        {
            ConnectionCardPresentation card = ShellPresentation.ConnectionCard(profile, connections.Active, connections.ConnectingId, connections.IsConnecting);
            Grid row = new() { ColumnSpacing = 10 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(ShellUi.Icon(card.IsActive ? "ShellIconCheck" : "ShellIconServer", ShellUi.Dimension("ShellIconSmall"),
                card.IsActive ? ShellUi.Brush("ShellConnectedBrush") : ShellUi.Brush("ShellTextSecondaryBrush")));
            StackPanel details = ShellUi.Stack(1);
            Grid.SetColumn(details, 1);
            details.Children.Add(ShellUi.Text(profile.Name, "ShellBodyTextStyle", TextWrapping.NoWrap));
            details.Children.Add(ShellUi.Text(profile.Address.Display, "ShellTertiaryTextStyle", TextWrapping.NoWrap));
            row.Children.Add(details);
            Border state = ShellUi.Badge(card.StateLabel, card.IsActive ? ShellTone.Success : card.IsConnecting ? ShellTone.Info : ShellTone.Neutral,
                card.IsActive ? "ShellIconCheck" : null);
            Grid.SetColumn(state, 2);
            row.Children.Add(state);
            Button item = new()
            {
                Content = row,
                Style = ShellUi.Resource<Style>(card.IsActive ? "ShellSelectedNavigationRowStyle" : "ShellNavigationRowStyle"),
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                IsEnabled = !connections.IsConnecting,
            };
            ServerProfile target = profile;
            item.Click += (_, _) =>
            {
                flyout.Hide();
                openSettings(target);
            };
            AutomationProperties.SetName(item, "Switch to " + profile.Name + ", " + profile.Address.Display + ", " + card.StateLabel);
            content.Children.Add(item);
        }

        content.Children.Add(ShellUi.Divider());
        Button manage = ShellUi.LabelButton("Manage connections", "ShellIconSettings", (_, _) =>
        {
            flyout.Hide();
            openSettings(null);
        }, "ShellQuietButtonStyle");
        manage.HorizontalAlignment = HorizontalAlignment.Stretch;
        manage.HorizontalContentAlignment = HorizontalAlignment.Left;
        content.Children.Add(manage);
        flyout.Content = content;
        return flyout;
    }
}
