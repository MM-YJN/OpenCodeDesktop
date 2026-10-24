using System;

using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

using OpenCodeDesktop.Views;

using Windows.Graphics;

namespace OpenCodeDesktop;

/// <summary>The single-window presentation shell. It has no server dependency.</summary>
public sealed partial class MainWindow : Window
{
    private readonly WorkspaceView _workspaceView = new();
    private readonly ConnectionView _connectionView = new();
    private double _rasterizationScale;

    public MainWindow()
    {
        InitializeComponent();
        ShellNavigation.SelectedItem = WorkspaceDestination;
        Closed += MainWindow_Closed;
    }

    private void ShellNavigation_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        ShellContentHost.Content = ReferenceEquals(args.SelectedItem, ConnectionDestination) ? _connectionView : _workspaceView;

        // Release the content area after selecting a destination in compact/overlay modes.
        if (ShellRoot.IsLoaded && sender.DisplayMode != NavigationViewDisplayMode.Expanded)
        {
            sender.IsPaneOpen = false;
        }
    }

    private void ShellRoot_Loaded(object sender, RoutedEventArgs args)
    {
        ShellRoot.Loaded -= ShellRoot_Loaded;
        ShellRoot.XamlRoot.Changed += XamlRoot_Changed;
        UpdateWindowMinimumSize();
        AppWindow.ResizeClient(new SizeInt32(
            ScaleDimension("ShellWindowInitialWidth"),
            ScaleDimension("ShellWindowInitialHeight")));
        WorkspaceDestination.Focus(FocusState.Programmatic);
    }

    private void XamlRoot_Changed(XamlRoot sender, XamlRootChangedEventArgs args)
    {
        if (sender.RasterizationScale != _rasterizationScale)
        {
            UpdateWindowMinimumSize();
        }
    }

    private void UpdateWindowMinimumSize()
    {
        _rasterizationScale = ShellRoot.XamlRoot.RasterizationScale;
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.PreferredMinimumWidth = ScaleDimension("ShellWindowMinimumWidth");
            presenter.PreferredMinimumHeight = ScaleDimension("ShellWindowMinimumHeight");
        }
    }

    private int ScaleDimension(string key) => (int)Math.Ceiling((double)Application.Current.Resources[key] * _rasterizationScale);

    private void MainWindow_Closed(object sender, WindowEventArgs args)
    {
        if (ShellRoot.XamlRoot is { } root)
        {
            root.Changed -= XamlRoot_Changed;
        }
    }
}
