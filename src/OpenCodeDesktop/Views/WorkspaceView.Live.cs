using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;

using OpenCodeDesktop.Core.Workspaces;
using OpenCodeDesktop.Presentation;

namespace OpenCodeDesktop.Views;

public sealed partial class WorkspaceView
{
    private LiveWorkspace? _live;
    private bool _renderQueued;
    private string _composerProjection = "";

    internal void Bind(LiveWorkspace workspace)
    {
        _live = workspace;
        FilesContent.Bind(workspace.Files);
        DetailsContent.Bind(workspace.Review);
        DetailsContent.PreviewRequested += ReviewPreviewRequestedAsync;
        workspace.Changed += LiveChanged;
        workspace.UserNavigation += ProjectSessionNavigationRequested;
        workspace.UserNavigation += TreeNavigationRequested;
        _treeConnectionKey = null;
        _treeActiveKey = null;
        _treeRevealPending = true;
        _expandedProjects.Clear();
        ClearProjectTree();
        LiveChanged(this, EventArgs.Empty);
    }

    internal void Unbind()
    {
        _live?.Changed -= LiveChanged;
        _live?.UserNavigation -= ProjectSessionNavigationRequested;
        FilesContent.Unbind();
        DetailsContent.PreviewRequested -= ReviewPreviewRequestedAsync;
        DetailsContent.Unbind();
        _live?.UserNavigation -= TreeNavigationRequested;
        _live = null;
        ClearProjectTree();
    }

    private void LiveChanged(object? sender, EventArgs args)
    {
        if (_renderQueued)
        {
            return;
        }

        _renderQueued = true;
        if (!DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            _renderQueued = false;
            RenderLive();
        }))
        {
            _renderQueued = false;
        }
    }

    private void RenderLive()
    {
        if (_live is not { } live || ProjectTree is null)
        {
            return;
        }

        var presentation = LiveWorkspacePresentation.From(live);
        ProjectWorkspaceTabs(live);
        OpenProjectState(live);
        SessionTitle.Text = presentation.Title;
        SessionPath.Text = presentation.Path.Replace("\n", " · ");
        ShellUi.Tooltip(SessionTitle, presentation.Title);
        ShellUi.Tooltip(SessionPath, presentation.Path);
        AutomationProperties.SetName(ConversationScroll, "Conversation history for " + presentation.Title);
        RenderLiveProjects(live);
        if (_tabs.Active is { IsFiles: false } active && !_tabNavigation &&
            active.Directory == live.Location?.Directory && active.SessionId == live.SelectedSessionId)
        {
            RenderLiveHistory(live, presentation);
            RenderLiveComposer(live, presentation.DraftOwner);
            RenderTaskControls();
            live.Review.Open(live.Location, live.SelectedSessionId);
            IReadOnlyList<HistoryMessage> reviewHistory = CurrentTask is { Hydrated: true } task
                ? live.History.Where(item => !task.Conversation.Any(message => message.Id == item.Id)).Concat(task.Conversation).ToArray()
                : live.History;
            DetailsContent.UpdateReview(reviewHistory);
            DetailsContent.Update(DetailsPresentationFactory.From(live, CurrentTask));
        }
        TerminalToggle.IsEnabled = false;
    }

    private async void ReviewPreviewRequestedAsync(object? sender, string path)
    {
        if (_live?.Location is { } location)
        {
            await OpenProjectFilesAsync(location, path).ConfigureAwait(true);
        }
    }

    private void OpenProjectState(LiveWorkspace live)
    {
        bool connected = live.IsConnected;
        NewTaskButton.IsEnabled = live.Location is not null;
        AddProjectButton.IsEnabled = connected && !live.IsLoading;
        ProjectsCount.Text = live.Projects.Count == 0 ? "" : "· " + live.Projects.Count;
        WorkspaceLoading.Visibility = live.IsLoading ? Visibility.Visible : Visibility.Collapsed;
        WorkspaceStatus.Text = live.Status;
        WorkspaceStatus.Visibility = live.IsLoading ? Visibility.Visible : Visibility.Collapsed;
        bool error = live.Error is not null;
        WorkspaceError.Text = live.Error ?? "";
        WorkspaceErrorBanner.Visibility = error ? Visibility.Visible : Visibility.Collapsed;
        WorkspaceRetry.Visibility = live.CanRetry ? Visibility.Visible : Visibility.Collapsed;
        WorkspaceRetry.IsEnabled = live.CanRetry;
        if (error)
        {
            ShellUi.Announce(WorkspaceError);
        }
    }

    private void RenderLiveComposer(LiveWorkspace live, string owner)
    {
        ComposerScroll.Visibility = live.Location is null ? Visibility.Collapsed : Visibility.Visible;
        if (live.Location is null)
        {
            return;
        }

        if (!Equals(Composer.Tag, owner))
        {
            Composer.Tag = owner;
            DraftBox.Text = live.Draft;
        }

        ComposerPresentation composer = ShellPresentation.Composer(live, CurrentTask, DraftBox.Text);
        string projection = composer.IsAvailable + "|" + composer.CanSend + "|" + composer.CanStop + "|" + composer.IsBusy + "|" +
            composer.Hint + "|" + composer.QueueLabel + "|" + composer.DeliveryLabel + "|" + composer.AttentionLabel;
        if (_composerProjection != projection)
        {
            _composerProjection = projection;
            ComposerHint.Text = composer.Hint;
            ComposerHint.Visibility = Visibility.Visible;
            ComposerHint.Foreground = composer.IsBusy ? ShellUi.Brush("ShellAccentBrush") : ShellUi.Brush("ShellTextTertiaryBrush");
            ComposerChips.Children.Clear();
            if (composer.QueueLabel is { } queue)
            {
                ComposerChips.Children.Add(ShellUi.Badge(queue, ShellTone.Info, "ShellIconClock"));
            }

            if (composer.DeliveryLabel is { } delivery)
            {
                ComposerChips.Children.Add(ShellUi.Badge(delivery, ShellTone.Danger, "ShellIconWarning"));
            }

            if (composer.AttentionLabel is { } attention)
            {
                ComposerChips.Children.Add(ShellUi.Badge(attention, ShellTone.Warning, "ShellIconWarning"));
            }

            ComposerChips.Visibility = ComposerChips.Children.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        }

        ComposerHint.Visibility = _compactConversation ? Visibility.Collapsed : Visibility.Visible;
        SendButton.IsEnabled = composer.CanSend;
        bool showStop = CurrentTask is { Active: true } or { Busy: true } or { Stopping: true };
        StopButton.Visibility = showStop ? Visibility.Visible : Visibility.Collapsed;
        StopButton.IsEnabled = composer.CanStop;
        DraftBox.IsEnabled = live.IsConnected;
    }

    private async void RetryWorkspace_ClickAsync(object sender, RoutedEventArgs args)
    {
        if (_live is not null)
        {
            await _live.RetryAsync().ConfigureAwait(true);
        }
    }
}
