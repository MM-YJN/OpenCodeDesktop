using System;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace OpenCodeDesktop.Views;

/// <summary>The workspace empty state before server connection is available.</summary>
public sealed partial class WorkspaceView : UserControl
{
    private bool? _isInspectorInline;
    private bool? _isProjectsInline;

    public WorkspaceView()
    {
        InitializeComponent();
    }

    private void ProjectsLayout_SizeChanged(object sender, SizeChangedEventArgs args)
    {
        bool isInline = args.NewSize.Width >= (double)Application.Current.Resources["ShellProjectsInlineThreshold"];
        if (_isProjectsInline != isInline)
        {
            _isProjectsInline = isInline;
            ProjectsLayout.DisplayMode = isInline ? SplitViewDisplayMode.Inline : SplitViewDisplayMode.Overlay;
            ProjectsLayout.IsPaneOpen = isInline;
        }
    }

    private void CloseProjects_Click(object sender, RoutedEventArgs args)
    {
        ProjectsLayout.IsPaneOpen = false;
        ProjectsToggle.Focus(FocusState.Programmatic);
    }

    private void WorkspaceLayout_SizeChanged(object sender, SizeChangedEventArgs args)
    {
        double inspectorWidth = (double)Application.Current.Resources["ShellInspectorWidth"];
        double compactWidth = (double)Application.Current.Resources["ShellNavigationCompactWidth"];
        WorkspaceLayout.OpenPaneLength = Math.Min(inspectorWidth, Math.Max(0, args.NewSize.Width - compactWidth));

        bool isInline = args.NewSize.Width >= (double)Application.Current.Resources["ShellInspectorInlineThreshold"];
        if (_isInspectorInline != isInline)
        {
            _isInspectorInline = isInline;
            WorkspaceLayout.DisplayMode = isInline ? SplitViewDisplayMode.Inline : SplitViewDisplayMode.Overlay;
            WorkspaceLayout.IsPaneOpen = isInline;
        }
    }

    private void CloseSessionInfo_Click(object sender, RoutedEventArgs args)
    {
        WorkspaceLayout.IsPaneOpen = false;
        SessionInfoToggle.Focus(FocusState.Programmatic);
    }
}
