using System;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;

using Windows.System;

namespace OpenCodeDesktop.Views;

public sealed partial class WorkspaceView : UserControl
{
    private bool? _projectsInline;
    private bool? _detailsInline;
    private bool _expanded;
    private double _detailsWidth = Dimension("ShellDetailsWidth");
    private TextBox? _draft;

    public event EventHandler? SettingsRequested;
    internal Button ConnectionSelector => ServerSelector;

    public WorkspaceView() => InitializeComponent();

    private async void NewTask_ClickAsync(object sender, RoutedEventArgs args)
    {
        if (_live is not null)
        {
            await _live.SelectSessionAsync(null).ConfigureAwait(true);
            _draft?.Focus(FocusState.Programmatic);
        }
    }

    private T LoadTemplate<T>(string key) where T : FrameworkElement => (T)((DataTemplate)Resources[key]).LoadContent();
    private static T Part<T>(FrameworkElement root, string name) where T : FrameworkElement => (T)root.FindName(name);

    private TextBlock BodyText(string text)
    {
        TextBlock block = LoadTemplate<TextBlock>("BodyTextTemplate");
        block.Text = text;
        return block;
    }

    private Border Message(string text)
    {
        Border card = LoadTemplate<Border>("MessageTemplate");
        Part<TextBlock>(card, "Message").Text = text;
        return card;
    }

    private void Settings_Click(object sender, RoutedEventArgs args) => SettingsRequested?.Invoke(this, EventArgs.Empty);
    private void Projects_Click(object sender, RoutedEventArgs args) => ProjectsLayout.IsPaneOpen = !ProjectsLayout.IsPaneOpen;
    private void Details_Click(object sender, RoutedEventArgs args) => DetailsLayout.IsPaneOpen = !DetailsLayout.IsPaneOpen;
    private void CloseProjects_Click(object sender, RoutedEventArgs args)
    {
        ProjectsLayout.IsPaneOpen = false;
        ProjectsToggle.Focus(FocusState.Programmatic);
    }
    private void CloseDetails_Click(object sender, RoutedEventArgs args)
    {
        _expanded = false;
        UpdateDetailsLayout();
        DetailsLayout.IsPaneOpen = false;
        DetailsToggle.Focus(FocusState.Programmatic);
    }
    private void Expand_Click(object sender, RoutedEventArgs args)
    {
        _expanded = !_expanded;
        UpdateDetailsLayout();
    }
    private void ProjectsLayout_SizeChanged(object sender, SizeChangedEventArgs args)
    {
        bool inline = args.NewSize.Width >= (double)Application.Current.Resources["ShellProjectsInlineThreshold"];
        if (_projectsInline != inline)
        {
            _projectsInline = inline;
            ProjectsLayout.DisplayMode = inline ? SplitViewDisplayMode.Inline : SplitViewDisplayMode.Overlay;
            ProjectsLayout.IsPaneOpen = inline;
        }
    }

    private void DetailsLayout_SizeChanged(object sender, SizeChangedEventArgs args)
    {
        ComposerScroll.MaxHeight = Math.Min(Dimension("ShellComposerMaximumHeight"), args.NewSize.Height * 0.35);
        bool inline = args.NewSize.Width >= (double)Application.Current.Resources["ShellInspectorInlineThreshold"];
        if (_detailsInline != inline)
        {
            _detailsInline = inline;
            DetailsLayout.IsPaneOpen = inline;
        }

        UpdateDetailsLayout();
    }

    private void UpdateDetailsLayout()
    {
        DetailsLayout.DisplayMode = _expanded || _detailsInline == true ? SplitViewDisplayMode.Inline : SplitViewDisplayMode.Overlay;
        double available = Math.Max(0, DetailsLayout.ActualWidth);
        DetailsLayout.OpenPaneLength = _expanded ? available : Math.Min(_detailsWidth, Math.Max(0, available - 48));
        DetailsRegion.Width = DetailsLayout.OpenPaneLength;
        CentralWorkspace.Visibility = _expanded ? Visibility.Collapsed : Visibility.Visible;
        ExpandButton.Content = _expanded ? "Restore" : "Expand";
        DetailsGrip.IsEnabled = !_expanded && _detailsInline == true;
    }

    private void ResizeDetails(double delta)
    {
        double minimum = Dimension("ShellDetailsMinimumWidth");
        _detailsWidth = Math.Clamp(_detailsWidth - delta, minimum, Math.Max(minimum, DetailsLayout.ActualWidth - Dimension("ShellConversationMinimumWidth")));
        UpdateDetailsLayout();
    }

    private void DetailsGrip_DragDelta(object sender, DragDeltaEventArgs args) => ResizeDetails(args.HorizontalChange);
    private static double Dimension(string key) => (double)Application.Current.Resources[key];
    private void DetailsGrip_KeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (args.Key is VirtualKey.Left or VirtualKey.Right)
        {
            ResizeDetails(args.Key == VirtualKey.Left ? -24 : 24);
            args.Handled = true;
        }
    }
}
