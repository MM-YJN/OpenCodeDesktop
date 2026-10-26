using System;
using System.Linq;
using System.Threading.Tasks;

using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

using OpenCodeDesktop.Core.Workspaces;
using OpenCodeDesktop.Presentation;

namespace OpenCodeDesktop.Views;

public sealed partial class WorkspaceView
{
    private bool _organizationDialogOpen;

    private async Task<ContentDialogResult> ShowOrganizationDialogAsync(ContentDialog dialog)
    {
        if (_organizationDialogOpen) { return ContentDialogResult.None; }
        _organizationDialogOpen = true;
        try { return await dialog.ShowAsync(); }
        finally { _organizationDialogOpen = false; }
    }

    private async Task SearchProjectSessionsAsync(LiveWorkspace live, string directory)
    {
        WorkspaceProject? project = live.Projects.FirstOrDefault(p => p.Location.Directory == directory);
        if (project is null) { return; }
        ContentDialog dialog = WorkspaceTemplate<ContentDialog>("SearchSessionsDialogTemplate");
        dialog.Title = "Search sessions in " + ProjectTreeNodePresentation.DisplayName(directory);
        dialog.XamlRoot = XamlRoot;
        AutoSuggestBox query = ShellUi.Part<AutoSuggestBox>(dialog, "Query");
        query.Text = project.Filter.Search;
        ToggleSwitch archived = ShellUi.Part<ToggleSwitch>(dialog, "Archived");
        archived.IsOn = project.Filter.Archived;
        bool submitted = false;
        query.QuerySubmitted += (_, _) => { submitted = true; dialog.Hide(); };
        ContentDialogResult result = await ShowOrganizationDialogAsync(dialog);
        if ((result == ContentDialogResult.Primary || submitted) && _live == live)
        {
            ExpandProject(directory);
            await live.FilterProjectSessionsAsync(directory, new(query.Text, archived.IsOn)).ConfigureAwait(true);
            ExpandProject(directory);
            RenderProjectTree(live);
        }
        FocusOrganizationRow(directory, null);
    }

    private MenuFlyout SessionMenu(LiveWorkspace live, string directory, string id)
    {
        MenuFlyout menu = new();
        menu.Opening += (_, _) =>
        {
            menu.Items.Clear();
            WorkspaceSession? session = live.Projects.FirstOrDefault(p => p.Location.Directory == directory)?.Sessions.FirstOrDefault(s => s.Id == id);
            if (session is null) { return; }
            MenuFlyoutItem rename = new() { Text = "Rename session…", IsEnabled = live.CanOrganizeSession(directory, id) };
            rename.Click += async (_, _) => await RenameSessionDialogAsync(live, session).ConfigureAwait(true);
            menu.Items.Add(rename);
            MenuFlyoutItem archive = new()
            {
                Text = session.Archived ? "Restore session" : "Archive session",
                IsEnabled = session.Archived ? live.CanOrganizeSession(directory, id) : live.CanArchiveSession(directory, id),
            };
            archive.Click += async (_, _) =>
            {
                await live.ArchiveSessionAsync(directory, id, !session.Archived).ConfigureAwait(true);
                FocusOrganizationRow(directory, id);
            };
            ToolTipService.SetToolTip(archive, "Archiving preserves history and files. Running, waiting and unresolved sessions must be checked before archiving.");
            menu.Items.Add(archive);
        };
        return menu;
    }

    private async Task RenameSessionDialogAsync(LiveWorkspace live, WorkspaceSession session)
    {
        ContentDialog dialog = WorkspaceTemplate<ContentDialog>("RenameSessionDialogTemplate");
        dialog.XamlRoot = XamlRoot;
        TextBox title = ShellUi.Part<TextBox>(dialog, "TitleInput");
        title.Text = session.Title;
        title.MaxLength = SessionTitles.MaximumLength;
        ShellUi.Part<TextBlock>(dialog, "Guidance").Text = SessionTitles.Guidance;
        dialog.IsPrimaryButtonEnabled = SessionTitles.IsValid(title.Text);
        title.TextChanged += (_, _) => dialog.IsPrimaryButtonEnabled = SessionTitles.IsValid(title.Text);
        if (await ShowOrganizationDialogAsync(dialog) == ContentDialogResult.Primary && _live == live)
        {
            await live.RenameSessionAsync(session.Directory, session.Id, title.Text).ConfigureAwait(true);
        }
        FocusOrganizationRow(session.Directory, session.Id);
    }

    private void FocusOrganizationRow(string directory, string? id) => DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
    {
        string key = id is null ? ProjectTreeNodePresentation.ProjectKey(directory) : ProjectTreeNodePresentation.SessionKey(directory, id);
        if (!_treeNodes.TryGetValue(key, out TreeViewNode? node)) { _treeNodes.TryGetValue(ProjectTreeNodePresentation.ProjectKey(directory), out node); }
        if (node is not null && ProjectTree.ContainerFromNode(node) is TreeViewItem item) { item.Focus(FocusState.Programmatic); }
    });
}
