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
    private readonly SettingsView _settingsView = new();
    private double _rasterizationScale;

    public MainWindow()
    {
        InitializeComponent();
        ShellContentHost.Children.Add(_workspaceView);
        ShellContentHost.Children.Add(_settingsView);
        _settingsView.Visibility = Visibility.Collapsed;
        _workspaceView.SettingsRequested += (_, _) => ShowSettings(true);
        _settingsView.ReturnRequested += (_, _) => ShowSettings(false);
        _workspaceView.AttentionChanged += (_, _) =>
        {
            AttentionButton.Content = "Needs attention: " + _workspaceView.AttentionCount;
        };
        AttentionButton.Content = "Needs attention: " + _workspaceView.AttentionCount;
        Closed += MainWindow_Closed;
    }

    private void ShowSettings(bool show)
    {
        _workspaceView.Visibility = show ? Visibility.Collapsed : Visibility.Visible;
        _settingsView.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        (show ? (Control)_settingsView : _workspaceView).Focus(FocusState.Programmatic);
    }

    private void Attention_Click(object sender, RoutedEventArgs args)
    {
        ShowSettings(false);
        _workspaceView.ShowAttentionAsync();
    }

    private void ShellRoot_Loaded(object sender, RoutedEventArgs args)
    {
        ShellRoot.Loaded -= ShellRoot_Loaded;
        ShellRoot.XamlRoot.Changed += XamlRoot_Changed;
        UpdateWindowMinimumSize();
        AppWindow.ResizeClient(new SizeInt32(
            ScaleDimension("ShellWindowInitialWidth"),
            ScaleDimension("ShellWindowInitialHeight")));
        _workspaceView.Focus(FocusState.Programmatic);
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
