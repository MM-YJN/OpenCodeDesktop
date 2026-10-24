using System;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

using OpenCodeDesktop.Core.Connections;

using static OpenCodeDesktop.Views.WorkspaceControls;

namespace OpenCodeDesktop.Views;

internal sealed class SettingsView : UserControl
{
    private readonly Grid _pages = new();
    private readonly TextBlock _feedback = Text("Connection checks contact the selected server. Profiles and preferences last for this app session only.");
    private readonly ConnectionView _connectionsView;
    private readonly ComboBox _sections;
    public event EventHandler? ReturnRequested;

    public SettingsView(ServerConnections connections)
    {
        _connectionsView = new ConnectionView(connections);
        Grid root = new() { Padding = new Thickness(16) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        StackPanel header = Stack();
        header.Children.Add(Button("Return to Workspace", () => ReturnRequested?.Invoke(this, EventArgs.Empty)));
        header.Children.Add(Text("Settings", true));
        header.Children.Add(_feedback);
        _sections = Choice("Section", ["Connections", "Models & Providers", "Extensions", "Permissions", "Appearance", "Notifications"], 0, SelectSection);
        header.Children.Add(_sections);
        root.Children.Add(header);
        Grid.SetRow(_pages, 1);
        root.Children.Add(_pages);
        _pages.Children.Add(_connectionsView);
        AddPage(Models());
        AddPage(Extensions());
        AddPage(Permissions());
        AddPage(Appearance());
        AddPage(Notifications());
        Content = root;
        SelectSection(0);
    }

    private void AddPage(UIElement content) => _pages.Children.Add(Scroll(content));

    public void ClearPassword() => _connectionsView.ClearPassword();

    public void ShowConnections(ServerProfile? profile)
    {
        _sections.SelectedIndex = 0;
        _connectionsView.Edit(profile);
    }

    private void SelectSection(int selected)
    {
        if (selected != 0)
        {
            ClearPassword();
        }

        for (int index = 0; index < _pages.Children.Count; index++)
        {
            _pages.Children[index].Visibility = index == selected ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private void Feedback(string message) => _feedback.Text = "Sample feedback: " + message + " Nothing was saved to disk or sent to a service.";

    private StackPanel Models()
    {
        StackPanel panel = Stack();
        panel.Children.Add(Text("Models & Providers", true));
        panel.Children.Add(Choice("Default sample model", ["Sample reasoning model", "Sample fast model"], 0,
            index => Feedback(index == 0 ? "Reasoning model selected." : "Fast model selected.")));
        panel.Children.Add(Choice("Sample provider", ["Embedded examples", "Illustrative remote provider"], 0,
            _ => Feedback("Provider selection changed locally.")));
        panel.Children.Add(Text("Provider sign-in is unavailable until live integration. No authentication or model discovery is performed."));
        return panel;
    }

    private static StackPanel Extensions()
    {
        StackPanel panel = Stack();
        panel.Children.Add(Text("Extensions", true));
        TextBlock detail = Text("Select a sample MCP server or plugin to inspect its status.");
        panel.Children.Add(Choice("MCP servers (samples)", ["Documentation · available sample", "Repository · unavailable sample"], 0,
            index => detail.Text = index == 0 ? "Documentation MCP · sample status: available\nTools: search_docs, read_page. No connection is made."
                : "Repository MCP · sample status: unavailable\nReason: live service integration is deferred."));
        panel.Children.Add(Choice("Plugins (samples)", ["Code review · enabled sample", "Issue tracker · disabled sample"], 0,
            index => detail.Text = index == 0 ? "Code review plugin · enabled sample\nCommands: /review. No plugin is loaded."
                : "Issue tracker plugin · disabled sample\nAuthentication is unavailable in this sample workspace."));
        panel.Children.Add(detail);
        panel.Children.Add(Text("Install, update, authentication, and service refresh are unavailable until live integration."));
        return panel;
    }

    private StackPanel Permissions()
    {
        StackPanel panel = Stack();
        panel.Children.Add(Text("Permissions", true));
        panel.Children.Add(Choice("Sample command policy", ["Ask each time", "Reject by default"], 0,
            _ => Feedback("Sample policy changed; pending sample cards remain individually actionable.")));
        ToggleSwitch scope = new() { Header = "Show permission scope", IsOn = true };
        scope.Toggled += (_, _) => Feedback("Sample scope preference: " + (scope.IsOn ? "shown." : "compact."));
        panel.Children.Add(scope);
        panel.Children.Add(Text("These preferences do not authorize commands. Real permission decisions require a connected session."));
        return panel;
    }

    private StackPanel Appearance()
    {
        StackPanel panel = Stack();
        panel.Children.Add(Text("Appearance", true));
        panel.Children.Add(Text("The app follows the Windows system theme, including high contrast. Theme persistence is deferred."));
        panel.Children.Add(Choice("Sample density preference", ["Comfortable", "Compact"], 0,
            index => Feedback("Sample density preference: " + (index == 0 ? "Comfortable." : "Compact.") + " This selector previews preference feedback.")));
        ToggleSwitch animation = new() { Header = "Sample motion preference", IsOn = true };
        animation.Toggled += (_, _) => Feedback("Sample motion preference changed locally.");
        panel.Children.Add(animation);
        return panel;
    }

    private StackPanel Notifications()
    {
        StackPanel panel = Stack();
        panel.Children.Add(Text("Notifications", true));
        foreach (string label in new[] { "Task completion (sample)", "Needs attention (sample)" })
        {
            ToggleSwitch toggle = new() { Header = label, IsOn = true };
            toggle.Toggled += (_, _) => Feedback(label + (toggle.IsOn ? " on." : " off."));
            panel.Children.Add(toggle);
        }

        panel.Children.Add(Button("Show notification example", () => Feedback("A sample task needs attention. No Windows notification was sent.")));
        return panel;
    }
}
