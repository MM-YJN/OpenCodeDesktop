using System;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

using OpenCodeDesktop.Core.Connections;

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
        // Subscribe after XAML initialization so initial values do not generate sample feedback.
        DefaultModel.SelectionChanged += (_, _) => Feedback(DefaultModel.SelectedIndex == 0 ? "Reasoning model selected." : "Fast model selected.");
        Provider.SelectionChanged += (_, _) => Feedback("Provider selection changed locally.");
        McpServers.SelectionChanged += (_, _) => ExtensionDetail.Text = McpServers.SelectedIndex == 0
            ? "Documentation MCP · sample status: available\nTools: search_docs, read_page. No connection is made."
            : "Repository MCP · sample status: unavailable\nReason: live service integration is deferred.";
        Plugins.SelectionChanged += (_, _) => ExtensionDetail.Text = Plugins.SelectedIndex == 0
            ? "Code review plugin · enabled sample\nCommands: /review. No plugin is loaded."
            : "Issue tracker plugin · disabled sample\nAuthentication is unavailable in this sample workspace.";
        CommandPolicy.SelectionChanged += (_, _) => Feedback("Sample policy changed; pending sample cards remain individually actionable.");
        PermissionScope.Toggled += (_, _) => Feedback("Sample scope preference: " + (PermissionScope.IsOn ? "shown." : "compact."));
        Density.SelectionChanged += (_, _) => Feedback("Sample density preference: " + (Density.SelectedIndex == 0 ? "Comfortable." : "Compact.") + " This selector previews preference feedback.");
        Motion.Toggled += (_, _) => Feedback("Sample motion preference changed locally.");
        TaskCompletion.Toggled += (_, _) => Feedback("Task completion (sample)" + (TaskCompletion.IsOn ? " on." : " off."));
        NeedsAttention.Toggled += (_, _) => Feedback("Needs attention (sample)" + (NeedsAttention.IsOn ? " on." : " off."));
        Sections.SelectedIndex = 0;
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

    public void ShowConnections(ServerProfile? profile)
    {
        Sections.SelectedIndex = 0;
        _connectionsView.Edit(profile);
    }

    private void Sections_Changed(object sender, SelectionChangedEventArgs args)
    {
        if (Pages is null)
        {
            return;
        }

        if (Sections.SelectedIndex != 0)
        {
            ClearPassword();
        }
        else if (IsLoaded && Visibility == Visibility.Visible)
        {
            _connectionsView.Activate();
        }

        for (int index = 0; index < Pages.Children.Count; index++)
        {
            Pages.Children[index].Visibility = index == Sections.SelectedIndex ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private void Return_Click(object sender, RoutedEventArgs args) => ReturnRequested?.Invoke(this, EventArgs.Empty);
    private void Notification_Click(object sender, RoutedEventArgs args) => Feedback("A sample task needs attention. No Windows notification was sent.");
    private void Feedback(string message) => FeedbackText.Text = "Sample feedback: " + message + " Nothing was saved to disk or sent to a service.";
}
