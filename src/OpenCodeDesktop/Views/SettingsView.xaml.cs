using System;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

using OpenCodeDesktop.Core.Connections;
using OpenCodeDesktop.Core.Workspaces;
using OpenCodeDesktop.Presentation;

using Windows.Foundation;

namespace OpenCodeDesktop.Views;

public sealed partial class SettingsView : UserControl
{
    private const double ScrollSpySlack = 8;

    private readonly ConnectionView _connectionsView;
    private readonly LiveWorkspace _workspace;
    private readonly FrameworkElement[] _sections;
    private readonly Button[] _sidebarItems;
    private bool _refreshingNotifications;
    private bool _navigating;
    private int _selectedSection = -1;

    internal string NotificationStatus { set => NotificationStatusText.Text = value; }
    public event EventHandler? ReturnRequested;

    public SettingsView(ServerConnections connections, ILocalServerDiscovery localDiscovery, LiveWorkspace workspace)
    {
        _workspace = workspace;
        InitializeComponent();
        _connectionsView = new ConnectionView(connections, localDiscovery);
        ConnectionsSection.Children.Add(_connectionsView);
        _sections = [ConnectionsSection, ModelsSection, ExtensionsSection, PermissionsSection, AppearanceSection, NotificationsSection];
        _sidebarItems = [NavConnections, NavModels, NavExtensions, NavPermissions, NavAppearance, NavNotifications];
        BuildUnavailableSections();
        SelectSection(0);
        ActualThemeChanged += (_, _) => BuildUnavailableSections();
    }

    public void ClearPassword() => _connectionsView.Deactivate();
    public void Shutdown() => _connectionsView.Shutdown();

    public void Activate()
    {
        _connectionsView.Activate();
        RefreshNotificationPreferences();
    }

    public void FocusInitial() => ReturnButton.Focus(FocusState.Programmatic);

    public void ShowConnections(ServerProfile? profile)
    {
        SelectSection(0);
        _connectionsView.ConnectOrPrompt(profile);
        DispatcherQueue.TryEnqueue(() => _connectionsView.FocusInitial());
    }

    public void ShowConnectionsSection()
    {
        NavigateToSection(0, animated: false);
        DispatcherQueue.TryEnqueue(() => _connectionsView.FocusInitial());
    }

    private void SidebarItem_Click(object sender, RoutedEventArgs args)
    {
        if (sender is Button { Tag: string tag } && int.TryParse(tag, out int index))
        {
            NavigateToSection(index, animated: true);
        }
    }

    private void NavigateToSection(int index, bool animated)
    {
        SelectSection(index);
        _navigating = true;
        DispatcherQueue.TryEnqueue(() =>
            SectionsScroll.ChangeView(null, SectionTop(_sections[index]), null, disableAnimation: !animated));
    }

    private void SectionsScroll_ViewChanged(object? sender, ScrollViewerViewChangedEventArgs args)
    {
        if (_navigating)
        {
            if (!args.IsIntermediate)
            {
                _navigating = false;
                SyncSelectionWithScroll();
            }

            return;
        }

        SyncSelectionWithScroll();
    }

    private void SectionsScroll_SizeChanged(object sender, SizeChangedEventArgs args) => SyncSelectionWithScroll();

    private void SyncSelectionWithScroll()
    {
        double offset = SectionsScroll.VerticalOffset;
        int active = 0;
        for (int i = 1; i < _sections.Length; i++)
        {
            if (SectionTop(_sections[i]) <= offset + ScrollSpySlack)
            {
                active = i;
            }
            else
            {
                break;
            }
        }

        if (SectionsScroll.ScrollableHeight > 0 && offset >= SectionsScroll.ScrollableHeight - ScrollSpySlack)
        {
            active = _sections.Length - 1;
        }

        SelectSection(active);
    }

    private double SectionTop(FrameworkElement section)
    {
        return section.TransformToVisual(SectionsContent).TransformPoint(new Point(0, 0)).Y;
    }

    private void SelectSection(int index)
    {
        if (index == _selectedSection)
        {
            return;
        }

        _selectedSection = index;
        for (int i = 0; i < _sidebarItems.Length; i++)
        {
            bool selected = i == index;
            _sidebarItems[i].Style = ShellUi.Resource<Style>(selected ? "ShellSelectedNavigationRowStyle" : "ShellNavigationRowStyle");
            AutomationProperties.SetItemStatus(_sidebarItems[i], selected ? "Selected" : "");
        }
    }

    private void BuildUnavailableSections()
    {
        BuildUnavailableSection(ModelsSection, 1);
        BuildUnavailableSection(ExtensionsSection, 2);
        BuildUnavailableSection(PermissionsSection, 3);
        BuildUnavailableSection(AppearanceSection, 4);
    }

    private static void BuildUnavailableSection(Panel host, int index)
    {
        SettingsSectionPresentation section = ShellPresentation.Section(index);
        host.Children.Clear();

        TextBlock heading = ShellUi.Text(section.Title, "ShellTitleTextStyle");
        ShellUi.Heading(heading);
        host.Children.Add(heading);

        StackPanel body = ShellUi.Stack(8);
        body.Children.Add(ShellUi.Icon("ShellIconInfo", null, ShellUi.Brush("ShellTextSecondaryBrush")));
        body.Children.Add(ShellUi.Text(section.Description, "ShellSecondaryTextStyle", TextWrapping.Wrap));
        host.Children.Add(ShellUi.Card(body, "ShellSubtleCardStyle"));
    }

    private void Return_Click(object sender, RoutedEventArgs args) => ReturnRequested?.Invoke(this, EventArgs.Empty);

    internal void RefreshNotificationPreferences()
    {
        _refreshingNotifications = true;
        CompletionNotifications.IsOn = _workspace.NotificationPreferences.Completion;
        AttentionNotifications.IsOn = _workspace.NotificationPreferences.Attention;
        _refreshingNotifications = false;
    }

    private void Notifications_Toggled(object sender, RoutedEventArgs args)
    {
        if (!_refreshingNotifications && CompletionNotifications is not null && AttentionNotifications is not null)
        {
            _workspace.SetNotificationPreferences(new(CompletionNotifications.IsOn, AttentionNotifications.IsOn));
        }
    }
}
