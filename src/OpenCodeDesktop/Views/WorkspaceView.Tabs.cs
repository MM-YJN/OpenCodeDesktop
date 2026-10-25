using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

using OpenCodeDesktop.Core.Workspaces;

using Windows.System;
using Windows.UI.Core;

namespace OpenCodeDesktop.Views;

public sealed partial class WorkspaceView
{
    private readonly WorkspaceTabs _tabs = new();
    private bool _tabRendering;
    private bool _tabNavigation;
    private bool _sessionNavigationPending;
    private long _tabGeneration;
    private bool _sessionDetailsOpen;
    private readonly Dictionary<Guid, string> _tabOrigins = [];

    private void InitializeTabs()
    {
        KeyboardAccelerator close = new() { Key = VirtualKey.W, Modifiers = VirtualKeyModifiers.Control };
        close.Invoked += async (_, args) =>
        {
            args.Handled = true;
            if (_tabs.Active is { } key)
            {
                await CloseWorkspaceTabAsync(key).ConfigureAwait(true);
            }
        };
        KeyboardAccelerators.Add(close);
        PreviewKeyDown += Workspace_PreviewKeyDownAsync;
    }

    private async void Workspace_PreviewKeyDownAsync(object sender, KeyRoutedEventArgs args)
    {
        if (args.Key != VirtualKey.Tab ||
            (InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control) & CoreVirtualKeyStates.Down) == 0 ||
            (InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Menu) & CoreVirtualKeyStates.Down) != 0)
        {
            return;
        }
        args.Handled = true;
        bool previous = (InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift) & CoreVirtualKeyStates.Down) != 0;
        await CycleWorkspaceTabAsync(previous ? -1 : 1).ConfigureAwait(true);
    }
    private Task CycleWorkspaceTabAsync(int direction)
    {
        WorkspaceTab[] tabs = _tabs.OpenTabs.ToArray();
        if (tabs.Length == 0) { return Task.CompletedTask; }
        int index = Array.FindIndex(tabs, tab => tab.Key == _tabs.Active);
        int next = index < 0 ? (direction > 0 ? 0 : tabs.Length - 1) : (index + direction + tabs.Length) % tabs.Length;
        return ActivateWorkspaceTabAsync(tabs[next].Key);
    }

    private void ProjectSessionNavigationRequested(object? sender, EventArgs args)
    {
        SaveWorkspacePosition();
        _live?.Review.Suspend();
        if (_tabs.Active?.IsFiles == true)
        {
            _live?.Files.Suspend();
        }
        if (!_tabNavigation)
        {
            _tabGeneration++;
        }
        _sessionNavigationPending = true;
    }

    private void ProjectWorkspaceTabs(LiveWorkspace live)
    {
        Guid? connection = live.IsConnected ? live.Server?.Profile.Id : null;
        if (connection is { } backend && live.Server is { } server)
        {
            string origin = server.Profile.Address.Display;
            if (_tabOrigins.TryGetValue(backend, out string? previous) && previous != origin)
            {
                _tabs.ForgetConnection(backend);
                foreach (WorkspaceTabKey key in _fileTabLocations.Keys.Where(key => key.ConnectionId == backend).ToArray())
                {
                    _fileTabLocations.Remove(key);
                }
            }
            _tabOrigins[backend] = origin;
        }
        if (_tabs.ConnectionId != connection)
        {
            SaveWorkspacePosition();
            _tabs.SetConnection(connection);
            _sessionNavigationPending = connection is not null;
            _tabGeneration++;
        }
        if (connection is { } id && live.Location is { } location)
        {
            WorkspaceTabKey key = new(id, location.Directory, live.SelectedSessionId, false);
            if (!_tabNavigation && !live.IsLoading && _sessionNavigationPending)
            {
                _sessionNavigationPending = false;
                if (_tabs.Active?.IsFiles == true)
                {
                    FilesContent.SavePosition();
                    live.Files.Suspend();
                }
                _tabs.Open(new(key, (live.Selected?.Title ?? "New-session draft") + " · " + ProjectName(location.Directory)));
            }
        }
        RenderTabStrip();
        ApplyWorkspaceMode();
    }

    private static string ProjectName(string path)
    {
        string trimmed = path.TrimEnd('/', '\\');
        int separator = trimmed.LastIndexOfAny(['/', '\\']);
        return trimmed.Length == 0 ? path : trimmed[(separator + 1)..];
    }

    private void RenderTabStrip()
    {
        _tabRendering = true;
        try
        {
            WorkspaceTab[] tabs = _tabs.OpenTabs.ToArray();
            foreach (TabViewItem item in WorkspaceTabStrip.TabItems.Cast<TabViewItem>().ToArray())
            {
                if (!tabs.Any(t => Equals(item.Tag, t.Key)))
                {
                    WorkspaceTabStrip.TabItems.Remove(item);
                }
            }
            foreach (WorkspaceTab tab in tabs)
            {
                TabViewItem? item = WorkspaceTabStrip.TabItems.Cast<TabViewItem>().FirstOrDefault(i => Equals(i.Tag, tab.Key));
                if (item is null)
                {
                    item = new TabViewItem
                    {
                        Tag = tab.Key,
                        IsClosable = true,
                        IconSource = new FontIconSource { Glyph = ShellUi.Glyph(tab.Key.IsFiles ? "ShellIconFolder" : "ShellIconMessage") },
                    };
                    WorkspaceTabStrip.TabItems.Add(item);
                }
                item.Header = tab.Title;
                string name = (tab.Key.IsFiles ? "Files: " : "Session: ") + tab.Title + ", " + tab.Key.Directory;
                AutomationProperties.SetName(item, name);
                ShellUi.Tooltip(item, name);
            }
            WorkspaceTabStrip.SelectedItem = WorkspaceTabStrip.TabItems.Cast<TabViewItem>().FirstOrDefault(i => Equals(i.Tag, _tabs.Active));
        }
        finally
        {
            _tabRendering = false;
        }
    }

    private void SaveWorkspacePosition()
    {
        DetailsContent.SavePosition();
        if (_tabs.Active?.IsFiles == true)
        {
            FilesContent.SavePosition();
        }
        else if (_conversationOwner is { } owner && !_restoreConversationPosition)
        {
            _conversationPositions[owner] = (ConversationScroll.VerticalOffset, ConversationScroll.ScrollableHeight - ConversationScroll.VerticalOffset < 48);
            _restoreConversationPosition = true;
        }
    }

    private void ApplyWorkspaceMode()
    {
        bool session = !_tabNavigation && _tabs.Active is { IsFiles: false } active &&
            _live?.Location?.Directory == active.Directory && _live.SelectedSessionId == active.SessionId;
        bool files = _tabs.Active is { IsFiles: true };
        DetailsLayout.Visibility = session ? Visibility.Visible : Visibility.Collapsed;
        FilesContent.Visibility = files ? Visibility.Visible : Visibility.Collapsed;
        NeutralState.Visibility = !session && !files ? Visibility.Visible : Visibility.Collapsed;
        bool failedSession = !session && !files && _tabs.Active is { IsFiles: false } && !_tabNavigation && _live?.IsLoading == false;
        TabRetry.Visibility = failedSession ? Visibility.Visible : Visibility.Collapsed;
        if (failedSession) { NeutralWorkspace.Text = _live?.Error ?? "This session could not be opened. Retry or choose another workspace tab."; }
        else if (_tabs.Active is null) { NeutralWorkspace.Text = "Choose a session or open Files from a project's menu."; }
        if (!session)
        {
            if (DetailsLayout.IsPaneOpen)
            {
                _sessionDetailsOpen = true;
            }
            DetailsLayout.IsPaneOpen = false;
            _expanded = false;
            UpdateDetailsLayout();
        }
        else if (_sessionDetailsOpen)
        {
            DetailsLayout.IsPaneOpen = true;
            _sessionDetailsOpen = false;
        }
    }

    private async Task OpenProjectFilesAsync(ProjectLocation location, string? path = null)
    {
        if (_live is not { IsConnected: true, Server: { } server } live)
        {
            return;
        }
        SaveWorkspacePosition();
        _sessionNavigationPending = false;
        live.Review.Suspend();
        _tabNavigation = false;
        long generation = ++_tabGeneration;
        WorkspaceTabKey key = new(server.Profile.Id, location.Directory, null, true);
        _fileTabLocations[key] = location;
        _tabs.Open(new(key, "Files · " + ProjectName(location.Directory)));
        ProjectFilesState? state = live.Files.Open(location);
        RenderTabStrip();
        ApplyWorkspaceMode();
        FilesContent.Show(server.Profile.Name + " · " + server.Profile.Address.Display);
        if (state is null)
        {
            return;
        }
        if (!state.Loaded)
        {
            await live.Files.BrowseAsync(state.DirectoryPath, state.Query).ConfigureAwait(true);
        }
        if (generation != _tabGeneration || live.Files.Current != state || _tabs.Active != key)
        {
            return;
        }
        if (path is not null)
        {
            await live.Files.SelectAsync(path).ConfigureAwait(true);
        }
        else if (state.SelectedPath is { } selected && state.Preview is null)
        {
            await live.Files.SelectAsync(selected).ConfigureAwait(true);
        }
        if (generation == _tabGeneration)
        {
            FilesContent.FocusInitial();
        }
    }

    private async void WorkspaceTabs_SelectionChangedAsync(object sender, SelectionChangedEventArgs args)
    {
        if (!_tabRendering && WorkspaceTabStrip.SelectedItem is TabViewItem { Tag: WorkspaceTabKey key })
        {
            await ActivateWorkspaceTabAsync(key).ConfigureAwait(true);
        }
    }

    private async Task ActivateWorkspaceTabAsync(WorkspaceTabKey key)
    {
        if (_live is not { IsConnected: true } live || _tabs.ConnectionId != key.ConnectionId)
        {
            return;
        }
        if (key.IsFiles)
        {
            ProjectFilesState? retained = live.Files.Current;
            ProjectLocation? location = live.Projects.FirstOrDefault(p => p.Location.Directory == key.Directory)?.Location;
            if (location is null && retained?.Location.Directory == key.Directory)
            {
                location = retained.Location;
            }
            // Tabs remain usable when their project is removed from the sidebar.
            if (location is null)
            {
                location = _fileTabLocations.GetValueOrDefault(key);
            }
            if (location is not null)
            {
                await OpenProjectFilesAsync(location).ConfigureAwait(true);
            }
            return;
        }
        SaveWorkspacePosition();
        live.Files.Suspend();
        _tabs.Activate(key);
        _sessionNavigationPending = false;
        long generation = ++_tabGeneration;
        _tabNavigation = true;
        RenderTabStrip();
        // Do not present the previous session as the target while server ownership is validated.
        DetailsLayout.Visibility = Visibility.Collapsed;
        FilesContent.Visibility = Visibility.Collapsed;
        NeutralWorkspace.Text = "Opening session…";
        NeutralState.Visibility = Visibility.Visible;
        try
        {
            if (key.SessionId is { } id)
            {
                await live.OpenProjectSessionAsync(key.Directory, id).ConfigureAwait(true);
            }
            else
            {
                await live.OpenProjectDraftAsync(key.Directory).ConfigureAwait(true);
            }
        }
        finally
        {
            if (generation == _tabGeneration)
            {
                _tabNavigation = false;
                _sessionNavigationPending = false;
                NeutralWorkspace.Text = "Choose a session or open Files from a project's menu.";
                RenderLive();
                if (DraftBox.IsEnabled && DetailsLayout.Visibility == Visibility.Visible)
                {
                    DraftBox.Focus(FocusState.Programmatic);
                }
                else
                {
                    ProjectsToggle.Focus(FocusState.Programmatic);
                }
            }
        }
    }

    private readonly Dictionary<WorkspaceTabKey, ProjectLocation> _fileTabLocations = [];

    private async void TabRetry_ClickAsync(object sender, RoutedEventArgs args)
    {
        if (_tabs.Active is { } key) { await ActivateWorkspaceTabAsync(key).ConfigureAwait(true); }
    }

    private async void WorkspaceTabs_CloseRequestedAsync(TabView sender, TabViewTabCloseRequestedEventArgs args)
    {
        if (args.Tab.Tag is WorkspaceTabKey key)
        {
            await CloseWorkspaceTabAsync(key).ConfigureAwait(true);
        }
    }

    private async Task CloseWorkspaceTabAsync(WorkspaceTabKey key)
    {
        bool active = _tabs.Active == key;
        if (active)
        {
            SaveWorkspacePosition();
            _live?.Files.Suspend();
            _tabGeneration++;
            _tabNavigation = false;
            _sessionNavigationPending = false;
        }
        _tabs.Close(key);
        RenderTabStrip();
        if (active && _tabs.Active is { } next)
        {
            await ActivateWorkspaceTabAsync(next).ConfigureAwait(true);
        }
        else
        {
            ApplyWorkspaceMode();
        }
        if (_tabs.Active is null)
        {
            ProjectsToggle.Focus(FocusState.Programmatic);
        }
    }
}
