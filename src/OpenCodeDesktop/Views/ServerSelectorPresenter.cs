using System;

using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

using OpenCodeDesktop.Core.Connections;

using static OpenCodeDesktop.Views.WorkspaceControls;

namespace OpenCodeDesktop.Views;

internal static class ServerSelectorPresenter
{
    public static void Update(Button selector, ServerConnections connections, Action<ServerProfile?> openSettings)
    {
        ActiveServer? active = connections.Active;
        TextBlock label = Text(active is null ? "Select server ▾" : active.Profile.Name + " ▾\n" + active.Profile.Address.Display);
        label.IsTextSelectionEnabled = false;
        selector.Content = label;
        AutomationProperties.SetName(selector, active is null ? "Switch server: disconnected"
            : "Switch server: " + active.Profile.Name + ", " + active.Profile.Address.Display);
        MenuFlyout menu = new() { Placement = FlyoutPlacementMode.BottomEdgeAlignedLeft };
        foreach (ServerProfile profile in connections.Profiles)
        {
            MenuFlyoutItem item = new()
            {
                Text = (profile.Id == active?.Profile.Id ? "✓ " : "") + profile.Name + "\n" + profile.Address.Display
                    + (profile.Id == active?.Profile.Id ? " · Active" : " · Not connected"),
                IsEnabled = !connections.IsConnecting,
            };
            item.Click += (_, _) => openSettings(profile);
            menu.Items.Add(item);
        }

        if (menu.Items.Count > 0)
        {
            menu.Items.Add(new MenuFlyoutSeparator());
        }

        MenuFlyoutItem manage = new() { Text = "Manage connections" };
        manage.Click += (_, _) => openSettings(null);
        menu.Items.Add(manage);
        selector.Flyout = menu;
    }
}
