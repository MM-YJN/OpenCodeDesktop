using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

using OpenCodeDesktop.Core.Workspaces;
using OpenCodeDesktop.Presentation;

namespace OpenCodeDesktop.Views;

public sealed partial class WorkspaceView
{
    private readonly Dictionary<TreeViewNode, IProjectTreeRow> _treeRows = [];
    private readonly Dictionary<TreeViewNode, ProjectTreeNodePresentation> _treeItems = [];
    private readonly Dictionary<string, TreeViewNode> _treeNodes = new(StringComparer.Ordinal);
    private readonly HashSet<string> _expandedProjects = new(StringComparer.Ordinal);
    private bool _treeRendering;
    private bool _treeRevealPending;
    private string? _treeActiveKey;
    private string? _treeConnectionKey;
    private long _suppressTreeInvokeUntil;

    private void RenderLiveProjects(LiveWorkspace live) => RenderProjectTree(live);

    private void RenderProjectTree(LiveWorkspace live)
    {
        if (ProjectTree is null || _treeRendering)
        {
            return;
        }

        _treeRendering = true;
        try
        {
            if (!live.IsConnected)
            {
                ClearProjectTree();
                ShowProjectTreeMessage("Connect to see saved projects and sessions. Settings > Connections manages profiles.");
                return;
            }

            var presentation = ProjectTreePresentation.From(live);
            if (presentation.Projects.Count == 0)
            {
                ClearProjectTree();
                ShowProjectTreeMessage(LiveWorkspacePresentation.EmptyProjects);
                return;
            }

            ProjectTree.Visibility = Visibility.Visible;
            ProjectTreeEmptyState.Visibility = Visibility.Collapsed;
            string? connectionKey = live.Server?.Profile.Id.ToString();
            if (_treeConnectionKey != connectionKey)
            {
                _treeConnectionKey = connectionKey;
                _treeActiveKey = null;
                _treeRevealPending = true;
                _expandedProjects.Clear();
            }

            SyncRootNodes(live, presentation);
            foreach (ProjectTreeNodePresentation project in presentation.Projects)
            {
                TreeViewNode node = _treeNodes[project.Key];
                if (_treeRevealPending && project.IsActive)
                {
                    _expandedProjects.Add(project.Directory);
                }

                bool expanded = _expandedProjects.Contains(project.Directory);
                if (node.IsExpanded != expanded)
                {
                    node.IsExpanded = expanded;
                }

                SyncProjectChildren(node, project, live.IsConnected);
            }

            if (_treeRevealPending || presentation.SelectedKey != _treeActiveKey)
            {
                _treeActiveKey = presentation.SelectedKey;
                RevealTreeSelection(live, presentation.SelectedKey);
            }
        }
        finally
        {
            _treeRendering = false;
            _treeRevealPending = false;
        }
    }

    private void SyncRootNodes(LiveWorkspace live, ProjectTreePresentation presentation)
    {
        var desired = presentation.Projects.Select(project => project.Key).ToHashSet(StringComparer.Ordinal);
        for (int index = ProjectTree.RootNodes.Count - 1; index >= 0; index--)
        {
            TreeViewNode node = ProjectTree.RootNodes[index];
            if (!_treeItems.TryGetValue(node, out ProjectTreeNodePresentation? item) || !desired.Contains(item.Key))
            {
                ProjectTree.RootNodes.RemoveAt(index);
                RemoveTreeNode(node);
            }
        }

        for (int index = 0; index < presentation.Projects.Count; index++)
        {
            ProjectTreeNodePresentation model = presentation.Projects[index];
            if (!_treeNodes.TryGetValue(model.Key, out TreeViewNode? node) || !_treeItems.ContainsKey(node))
            {
                node = CreateTreeNode(model);
                ProjectTree.RootNodes.Insert(Math.Min(index, ProjectTree.RootNodes.Count), node);
                continue;
            }

            UpdateTreeNode(node, model, live.IsConnected);
            int current = RootIndexOf(node);
            if (current < 0)
            {
                ProjectTree.RootNodes.Insert(Math.Min(index, ProjectTree.RootNodes.Count), node);
            }
            else if (current != index)
            {
                ProjectTree.RootNodes.RemoveAt(current);
                ProjectTree.RootNodes.Insert(Math.Min(index, ProjectTree.RootNodes.Count), node);
            }
        }
    }

    private void SyncProjectChildren(TreeViewNode node, ProjectTreeNodePresentation model, bool connected)
    {
        if (!node.IsExpanded)
        {
            ClearChildren(node);
            node.HasUnrealizedChildren = model.HasUnrealizedChildren;
            return;
        }

        var desired = model.Children.Select(child => child.Key).ToHashSet(StringComparer.Ordinal);
        for (int index = node.Children.Count - 1; index >= 0; index--)
        {
            TreeViewNode child = node.Children[index];
            if (!_treeItems.TryGetValue(child, out ProjectTreeNodePresentation? item) || !desired.Contains(item.Key))
            {
                node.Children.RemoveAt(index);
                RemoveTreeNode(child);
            }
        }

        for (int index = 0; index < model.Children.Count; index++)
        {
            ProjectTreeNodePresentation childModel = model.Children[index];
            if (!_treeNodes.TryGetValue(childModel.Key, out TreeViewNode? child) || !_treeItems.ContainsKey(child))
            {
                child = CreateTreeNode(childModel);
                node.Children.Insert(Math.Min(index, node.Children.Count), child);
                continue;
            }

            UpdateTreeNode(child, childModel, connected);
            int current = ChildIndexOf(node, child);
            if (current < 0)
            {
                node.Children.Insert(Math.Min(index, node.Children.Count), child);
            }
            else if (current != index)
            {
                node.Children.RemoveAt(current);
                node.Children.Insert(Math.Min(index, node.Children.Count), child);
            }
        }

        node.HasUnrealizedChildren = model.Children.Count == 0 && model.HasUnrealizedChildren;
    }

    private TreeViewNode CreateTreeNode(ProjectTreeNodePresentation model)
    {
        IProjectTreeRow row = model.Kind switch
        {
            ProjectTreeItemKind.Project => new ProjectRowView(model, () => StartNewDraftAsync(model.Directory)),
            ProjectTreeItemKind.Session => new SessionRowView(model),
            ProjectTreeItemKind.Draft => new DraftRowView(model),
            _ => new LoadMoreRowView(model),
        };
        TreeViewNode node = new() { Content = row.Root };
        _treeRows[node] = row;
        _treeItems[node] = model;
        _treeNodes[model.Key] = node;
        // Containers are realized after node creation (and again after virtualization).
        // Naming only the selected container leaves every other row with the native
        // TreeViewNode type name in the accessibility tree.
        row.Root.Loaded += (_, _) => UpdateTreeAutomationName(node);
        if (row is SessionRowView sessionRow && _live is { } workspace && model.SessionId is { } sessionId)
        {
            sessionRow.MoreButton.Flyout = SessionMenu(workspace, model.Directory, sessionId);
            sessionRow.Root.ContextFlyout = SessionMenu(workspace, model.Directory, sessionId);
            sessionRow.MoreButton.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler((_, _) => SuppressTreeInvoke()), true);
            sessionRow.MoreButton.Click += (_, _) => SuppressTreeInvoke();
        }
        if (row is ProjectRowView projectRow)
        {
            projectRow.NewSessionButton.AddHandler(
                UIElement.PointerPressedEvent,
                new PointerEventHandler((_, _) => SuppressTreeInvoke()),
                true);
            projectRow.MoreButton.AddHandler(
                UIElement.PointerPressedEvent,
                new PointerEventHandler((_, _) => SuppressTreeInvoke()),
                true);
            projectRow.NewSessionButton.Click += (_, _) => SuppressTreeInvoke();
            projectRow.MoreButton.Click += (_, _) => SuppressTreeInvoke();
            if (_live is { } live)
            {
                projectRow.MoreButton.Flyout = ProjectMenu(live, model.Directory);
            }
        }

        return node;
    }

    private void UpdateTreeNode(TreeViewNode node, ProjectTreeNodePresentation model, bool connected)
    {
        if (_treeRows.TryGetValue(node, out IProjectTreeRow? row))
        {
            row.Update(model, connected);
        }

        _treeItems[node] = model;
        _treeNodes[model.Key] = node;
        UpdateTreeAutomationName(node);
    }

    private void RevealTreeSelection(LiveWorkspace live, string? selectedKey)
    {
        if (string.IsNullOrEmpty(selectedKey))
        {
            ProjectTree.SelectedNode = null;
            return;
        }

        WorkspaceProject? active = live.Projects.FirstOrDefault(project => project.IsSelected);
        if (active is not null)
        {
            _expandedProjects.Add(active.Location.Directory);
            if (_treeNodes.TryGetValue(ProjectTreeNodePresentation.ProjectKey(active.Location.Directory), out TreeViewNode? projectNode))
            {
                if (!projectNode.IsExpanded)
                {
                    projectNode.IsExpanded = true;
                }

                if (_treeItems.TryGetValue(projectNode, out ProjectTreeNodePresentation? projectModel))
                {
                    SyncProjectChildren(projectNode, projectModel, live.IsConnected);
                }
            }
        }

        if (!_treeNodes.TryGetValue(selectedKey, out TreeViewNode? node) || !_treeItems.ContainsKey(node))
        {
            return;
        }

        if (!ReferenceEquals(ProjectTree.SelectedNode, node))
        {
            ProjectTree.SelectedNode = node;
        }

        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            if (ProjectTree.ContainerFromNode(node) is FrameworkElement container)
            {
                container.StartBringIntoView();
            }
        });
    }

    private async void ProjectTree_ExpandingAsync(TreeView sender, TreeViewExpandingEventArgs args)
    {
        if (!_treeItems.TryGetValue(args.Node, out ProjectTreeNodePresentation? model) || model.Kind != ProjectTreeItemKind.Project)
        {
            return;
        }

        _expandedProjects.Add(model.Directory);
        if (model.IsActive)
        {
            _treeRevealPending = true;
        }

        if (_live is not { } live)
        {
            return;
        }

        if (live.Projects.FirstOrDefault(project => project.Location.Directory == model.Directory) is { SessionsLoaded: false, SessionsLoading: false })
        {
            await live.LoadProjectSessionsAsync(model.Directory).ConfigureAwait(true);
        }

        if (_live == live)
        {
            DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
            {
                if (_live == live)
                {
                    RenderProjectTree(live);
                }
            });
        }
    }

    private void ProjectTree_Collapsed(TreeView sender, TreeViewCollapsedEventArgs args)
    {
        if (!_treeItems.TryGetValue(args.Node, out ProjectTreeNodePresentation? model) || model.Kind != ProjectTreeItemKind.Project)
        {
            return;
        }

        _expandedProjects.Remove(model.Directory);
        ClearChildren(args.Node);
        args.Node.HasUnrealizedChildren = model.HasUnrealizedChildren;
    }

    private async void ProjectTree_ItemInvokedAsync(TreeView sender, TreeViewItemInvokedEventArgs args)
    {
        if (Environment.TickCount64 < _suppressTreeInvokeUntil)
        {
            _suppressTreeInvokeUntil = 0;
            args.Handled = true;
            return;
        }

        if (args.InvokedItem is not TreeViewNode node ||
            !_treeItems.TryGetValue(node, out ProjectTreeNodePresentation? model) ||
            _live is not { } live)
        {
            return;
        }

        args.Handled = true;
        switch (model.Kind)
        {
            case ProjectTreeItemKind.Project:
                if (live.Location?.Directory == model.Directory)
                {
                    node.IsExpanded = !node.IsExpanded;
                    return;
                }

                _treeRevealPending = true;
                await live.OpenProjectAsync(model.Directory).ConfigureAwait(true);
                return;
            case ProjectTreeItemKind.Session when model.SessionId is { } sessionId:
                {
                    _treeRevealPending = true;
                    Task load = live.Location?.Directory == model.Directory
                        ? live.SelectSessionAsync(sessionId)
                        : live.OpenProjectSessionAsync(model.Directory, sessionId);
                    OpenSessionTab(live, model.Directory, sessionId, model.Label);
                    await load.ConfigureAwait(true);
                    return;
                }
            case ProjectTreeItemKind.Draft:
                {
                    _treeRevealPending = true;
                    Task load = live.Location?.Directory == model.Directory
                        ? live.SelectSessionAsync(null)
                        : live.OpenProjectDraftAsync(model.Directory);
                    OpenSessionTab(live, model.Directory, null, "New-session draft");
                    await load.ConfigureAwait(true);
                    return;
                }
            case ProjectTreeItemKind.LoadMore:
                _expandedProjects.Add(model.Directory);
                await live.LoadProjectSessionsAsync(model.Directory, true).ConfigureAwait(true);
                return;
        }
    }

    private async void StartNewDraftAsync(string directory)
    {
        if (_live is not { } live)
        {
            return;
        }

        _treeRevealPending = true;
        Task load = live.Location?.Directory == directory
            ? live.SelectSessionAsync(null)
            : live.OpenProjectDraftAsync(directory);
        OpenSessionTab(live, directory, null, "New-session draft");
        await load.ConfigureAwait(true);
    }

    private MenuFlyout ProjectMenu(LiveWorkspace live, string directory)
    {
        MenuFlyout menu = new();
        menu.Opening += (_, _) =>
        {
            menu.Items.Clear();
            WorkspaceProject? project = live.Projects.FirstOrDefault(candidate => candidate.Location.Directory == directory);
            bool active = live.Location?.Directory == directory;

            MenuFlyoutItem search = new() { Text = "Search sessions…" };
            search.Click += async (_, _) => await SearchProjectSessionsAsync(live, directory).ConfigureAwait(true);
            menu.Items.Add(search);
            MenuFlyoutItem history = new() { Text = project?.Filter.Archived == true ? "Show active history" : "Show archived history" };
            history.Click += async (_, _) =>
            {
                ExpandProject(directory);
                await live.FilterProjectSessionsAsync(directory, (project?.Filter ?? new()) with { Archived = project?.Filter.Archived != true }).ConfigureAwait(true);
                ExpandProject(directory);
                RenderProjectTree(live);
                FocusOrganizationRow(directory, null);
            };
            menu.Items.Add(history);

            MenuFlyoutItem files = MenuItem("Files", "ShellIconFolder");
            files.IsEnabled = live.IsConnected && project is not null;
            files.Click += async (_, _) =>
            {
                if (project is not null) { await OpenProjectFilesAsync(project.Location).ConfigureAwait(true); }
            };
            menu.Items.Add(files);

            MenuFlyoutItem refresh = MenuItem("Refresh sessions", "ShellIconRefresh");
            refresh.IsEnabled = project is { SessionsLoading: false };
            refresh.Click += async (_, _) =>
            {
                ExpandProject(directory);
                await live.RefreshProjectSessionsAsync(directory).ConfigureAwait(true);
            };
            menu.Items.Add(refresh);

            if (project is { CanLoadMoreSessions: true })
            {
                MenuFlyoutItem more = MenuItem("Load more sessions", "ShellIconHistory");
                more.Click += async (_, _) =>
                {
                    ExpandProject(directory);
                    await live.LoadProjectSessionsAsync(directory, true).ConfigureAwait(true);
                };
                menu.Items.Add(more);
            }

            MenuFlyoutItem create = MenuItem("Create session", "ShellIconAdd");
            create.IsEnabled = active && live.CanCreate;
            create.Click += async (_, _) => await live.CreateSessionAsync().ConfigureAwait(true);
            menu.Items.Add(create);

            menu.Items.Add(new MenuFlyoutSeparator());
            MenuFlyoutItem add = MenuItem("Add project…", "ShellIconAdd");
            add.Click += async (_, _) => await ShowAddProjectDialogAsync().ConfigureAwait(true);
            menu.Items.Add(add);

            MenuFlyoutItem remove = MenuItem("Remove from list", "ShellIconRemove");
            remove.Click += (_, _) => RemoveProjectAndRestoreFocus(live, directory);
            menu.Items.Add(remove);
        };
        return menu;
    }

    private void RemoveProjectAndRestoreFocus(LiveWorkspace live, string directory)
    {
        int index = _treeNodes.TryGetValue(ProjectTreeNodePresentation.ProjectKey(directory), out TreeViewNode? removed)
            ? RootIndexOf(removed) : 0;
        live.RemoveProject(directory);
        // RenderLive is queued by the workspace change. Restore focus after that
        // render and menu dismissal, when the removed button no longer exists.
        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            if (_live != live)
            {
                return;
            }

            if (ProjectTree.RootNodes.Count > 0 &&
                ProjectTree.ContainerFromNode(ProjectTree.RootNodes[Math.Clamp(index, 0, ProjectTree.RootNodes.Count - 1)]) is TreeViewItem next)
            {
                next.Focus(FocusState.Programmatic);
            }
            else
            {
                AddProjectButton.Focus(FocusState.Programmatic);
            }
        });
    }

    private static MenuFlyoutItem MenuItem(string text, string glyphKey) => new()
    {
        Text = text,
        Icon = new FontIcon { Glyph = ShellUi.Glyph(glyphKey) },
    };

    private void ExpandProject(string directory)
    {
        _expandedProjects.Add(directory);
        if (_treeNodes.TryGetValue(ProjectTreeNodePresentation.ProjectKey(directory), out TreeViewNode? node))
        {
            node.IsExpanded = true;
        }
    }

    private void TreeNavigationRequested(object? sender, EventArgs args) => _treeRevealPending = true;

    private void ProjectTree_SelectionChanged(TreeView sender, TreeViewSelectionChangedEventArgs args)
    {
        if (sender.SelectedNode is TreeViewNode node)
        {
            UpdateTreeAutomationName(node);
        }
    }

    private void UpdateTreeAutomationName(TreeViewNode node)
    {
        if (!_treeItems.TryGetValue(node, out ProjectTreeNodePresentation? model) ||
            ProjectTree.ContainerFromNode(node) is not TreeViewItem item)
        {
            return;
        }

        AutomationProperties.SetName(item, TreeAutomationName(model));
    }

    private static string TreeAutomationName(ProjectTreeNodePresentation model) => model.Kind switch
    {
        ProjectTreeItemKind.Project => "Project " + model.Label + ", " + model.Path + (model.Detail is { Length: > 0 } ? ", " + model.Detail : ""),
        ProjectTreeItemKind.Session => "Session " + model.Label + " in " + ProjectTreeNodePresentation.DisplayName(model.Directory),
        ProjectTreeItemKind.Draft => "New session draft in " + ProjectTreeNodePresentation.DisplayName(model.Directory),
        _ => "Load more sessions for " + ProjectTreeNodePresentation.DisplayName(model.Directory),
    };

    private void SuppressTreeInvoke() => _suppressTreeInvokeUntil = Environment.TickCount64 + 300;

    private void ShowProjectTreeMessage(string message)
    {
        ProjectTreeEmptyState.Text = message;
        ProjectTreeEmptyState.Visibility = Visibility.Visible;
        ProjectTree.Visibility = Visibility.Collapsed;
    }

    private void ClearProjectTree()
    {
        if (ProjectTree is null)
        {
            return;
        }

        foreach (TreeViewNode node in ProjectTree.RootNodes.ToArray())
        {
            RemoveTreeNode(node);
        }

        ProjectTree.RootNodes.Clear();
        _treeRows.Clear();
        _treeItems.Clear();
        _treeNodes.Clear();
    }

    private void RemoveTreeNode(TreeViewNode node)
    {
        foreach (TreeViewNode child in node.Children.ToArray())
        {
            RemoveTreeNode(child);
        }

        node.Children.Clear();
        if (_treeItems.TryGetValue(node, out ProjectTreeNodePresentation? model) &&
            _treeNodes.TryGetValue(model.Key, out TreeViewNode? mapped) &&
            ReferenceEquals(mapped, node))
        {
            _treeNodes.Remove(model.Key);
        }

        _treeRows.Remove(node);
        _treeItems.Remove(node);
    }

    private void ClearChildren(TreeViewNode node)
    {
        foreach (TreeViewNode child in node.Children.ToArray())
        {
            RemoveTreeNode(child);
        }

        node.Children.Clear();
    }

    private int RootIndexOf(TreeViewNode node)
    {
        for (int index = 0; index < ProjectTree.RootNodes.Count; index++)
        {
            if (ReferenceEquals(ProjectTree.RootNodes[index], node))
            {
                return index;
            }
        }

        return -1;
    }

    private static int ChildIndexOf(TreeViewNode parent, TreeViewNode child)
    {
        for (int index = 0; index < parent.Children.Count; index++)
        {
            if (ReferenceEquals(parent.Children[index], child))
            {
                return index;
            }
        }

        return -1;
    }
}
