using System;

using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

using OpenCodeDesktop.Core.Connections;

namespace OpenCodeDesktop.Views;

internal static class ServerSelectorPresenter
{
    public static void Update(Button selector, ServerConnections connections, Action<ServerProfile?> openSettings)
    {
        ActiveServer? active = connections.Active;
        var label = (TextBlock)selector.Content;
        label.Text = active is null ? "Select server ▾" : active.Profile.Name + " ▾\n" + active.Profile.Address.Display;
        AutomationProperties.SetName(selector, active is null ? "Switch server: disconnected"
            : "Switch server: " + active.Profile.Name + ", " + active.Profile.Address.Display);
        var menu = (MenuFlyout)selector.Flyout;
        menu.Items.Clear();
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
