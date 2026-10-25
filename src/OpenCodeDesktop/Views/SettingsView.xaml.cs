using System;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

using OpenCodeDesktop.Core.Connections;
using OpenCodeDesktop.Presentation;

namespace OpenCodeDesktop.Views;

public sealed partial class SettingsView : UserControl
{
    private readonly ConnectionView _connectionsView;
    public event EventHandler? ReturnRequested;

    public SettingsView(ServerConnections connections, ILocalServerDiscovery localDiscovery)
    {
        InitializeComponent();
        _connectionsView = new ConnectionView(connections, localDiscovery);
        ConnectionsHost.Content = _connectionsView;
        Sections.SelectedIndex = 0;
        ActualThemeChanged += (_, _) =>
        {
            if (Sections.SelectedIndex != 0)
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
        UnavailableHost.Visibility = connections ? Visibility.Collapsed : Visibility.Visible;
        if (!connections)
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
}
