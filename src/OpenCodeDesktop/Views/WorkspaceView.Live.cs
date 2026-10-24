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
    private string _projectsProjection = "";
    private string _historyOwner = "";
    private string _historyNotice = "";
    private bool _historyMore;
    private IReadOnlyList<HistoryMessage> _renderedHistory = [];

    internal void Bind(LiveWorkspace workspace)
    {
        _live = workspace;
        workspace.Changed += LiveChanged;
        LiveChanged(this, EventArgs.Empty);
    }

    internal void Unbind()
    {
        _live?.Changed -= LiveChanged;
        _live = null;
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

    private Button LiveAction(string label, Func<Task> action, bool enabled = true)
    {
        Button button = LoadTemplate<Button>("ActionTemplate");
        button.Content = new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap };
        button.HorizontalAlignment = HorizontalAlignment.Stretch;
        button.HorizontalContentAlignment = HorizontalAlignment.Left;
        button.IsEnabled = enabled;
        AutomationProperties.SetName(button, label);
        button.Click += async (_, _) => await action().ConfigureAwait(true);
        return button;
    }

    private void RenderLive()
    {
        if (_live is not { } live || ProjectList is null)
        {
            return;
        }

        var presentation = LiveWorkspacePresentation.From(live);
        LiveProjectControls.Visibility = Visibility.Visible;
        OpenDirectoryButton.IsEnabled = live.IsConnected;
        ServerDirectory.IsEnabled = live.IsConnected;
        NewTaskButton.Content = "New-session draft";
        NewTaskButton.IsEnabled = live.Location is not null;
        WorkspaceLoading.Visibility = live.IsLoading ? Visibility.Visible : Visibility.Collapsed;
        bool statusChanged = WorkspaceStatus.Text != live.Status;
        bool errorChanged = WorkspaceError.Text != (live.Error ?? "");
        WorkspaceStatus.Text = live.Status;
        WorkspaceError.Text = live.Error ?? "";
        WorkspaceError.Visibility = live.Error is null ? Visibility.Collapsed : Visibility.Visible;
        WorkspaceRetry.Visibility = live.CanRetry ? Visibility.Visible : Visibility.Collapsed;
        WorkspaceRetry.IsEnabled = live.CanRetry;
        if (statusChanged)
        {
            FrameworkElementAutomationPeer.FromElement(WorkspaceStatus)?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
        }

        if (errorChanged)
        {
            FrameworkElementAutomationPeer.FromElement(WorkspaceError)?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
        }

        SessionTitle.Text = presentation.Title;
        SessionPath.Text = presentation.Path;
        RenderLiveProjects(live);
        RenderLiveHistory(live, presentation);
        RenderLiveComposer(live, presentation.DraftOwner);
        RenderTaskControls();
        LiveDetails.Text = LiveWorkspacePresentation.UnavailableDetails;
        LiveDetails.Visibility = Visibility.Visible;
        TerminalToggle.IsEnabled = false;
    }

    private void RenderLiveProjects(LiveWorkspace live)
    {
        string projection = live.Server?.Profile.Id + "|" + live.Location?.Directory + "|" + live.SelectedSessionId + "|" +
            live.IsLoading + "|" + live.CanCreate + "|" + live.CreationUncertain + "|" + live.CanLoadSessions + "|" +
            string.Join('|', LiveWorkspacePresentation.ProjectList(live).Select(p => p.Label)) + "|" + string.Join('|', live.Sessions.Select(s => s.Id + ":" + s.Title));
        if (_projectsProjection == projection)
        {
            return;
        }

        _projectsProjection = projection;
        ProjectList.Children.Clear();
        if (live.IsConnected)
        {
            if (live.Projects.Count == 0)
            {
                ProjectList.Children.Add(BodyText(LiveWorkspacePresentation.EmptyProjects));
            }
            foreach (ProjectListPresentation project in LiveWorkspacePresentation.ProjectList(live))
            {
                string path = project.Path;
                Button select = LiveAction(project.Label, () => live.OpenProjectAsync(path), live.IsConnected);
                ToolTipService.SetToolTip(select, path);
                AutomationProperties.SetItemStatus(select, project.IsSelected ? "Selected project" : "Project");
                ProjectList.Children.Add(select);
                Button remove = LiveAction("Remove from list: " + project.Name, () =>
                {
                    live.RemoveProject(path);
                    return Task.CompletedTask;
                }, live.IsConnected);
                AutomationProperties.SetName(remove, "Remove from list: " + path);
                ToolTipService.SetToolTip(remove, "Forget " + path + ". Server work and drafts are retained.");
                ProjectList.Children.Add(remove);
            }
            if (live.Location is not null)
            {
                ProjectList.Children.Add(LiveAction("Create session", live.CreateSessionAsync, live.CanCreate));
                ProjectList.Children.Add(LiveAction(live.CreationUncertain ? "Reconcile creation: refresh sessions" : "Refresh sessions", live.RefreshSessionsAsync, !live.IsLoading));
                if (live.Sessions.Count == 0)
                {
                    ProjectList.Children.Add(BodyText("No sessions. Choose Create session to start a conversation."));
                }

                foreach (WorkspaceSession session in live.Sessions)
                {
                    string id = session.Id;
                    ProjectList.Children.Add(LiveAction((live.Selected?.Id == id ? "Selected: " : "") + session.Title,
                        () => live.SelectSessionAsync(id), !live.IsLoading));
                }
                if (live.CanLoadSessions)
                {
                    ProjectList.Children.Add(LiveAction("Load more sessions", live.LoadMoreSessionsAsync));
                }
            }
        }
    }

    private void RenderLiveHistory(LiveWorkspace live, LiveWorkspacePresentation presentation)
    {
        IReadOnlyList<HistoryMessage> history = CurrentTask is { Hydrated: true } state
            ? live.History.Where(h => !state.Conversation.Any(m => m.Id == h.Id)).Concat(state.Conversation).ToArray() : live.History;
        if (_historyOwner == presentation.DraftOwner && _historyNotice == presentation.ConversationNotice &&
            _historyMore == live.CanLoadHistory && _renderedHistory.SequenceEqual(history))
        {
            return;
        }

        _historyOwner = presentation.DraftOwner;
        _historyNotice = presentation.ConversationNotice;
        _historyMore = live.CanLoadHistory;
        _renderedHistory = history;
        Conversation.Children.Clear();
        if (presentation.ConversationNotice.Length > 0)
        {
            Conversation.Children.Add(BodyText(presentation.ConversationNotice));
        }

        if (live.CanLoadHistory)
        {
            Conversation.Children.Add(LiveAction("Load older messages", live.LoadOlderHistoryAsync));
        }

        foreach (HistoryMessage message in history)
        {
            AddMessage(message);
        }
    }

    private void RenderLiveComposer(LiveWorkspace live, string owner)
    {
        // Preserve the editor and keyboard focus during loading/status updates for the same draft owner.
        if (!Equals(Composer.Tag, owner))
        {
            Composer.Tag = owner;
            Composer.Children.Clear();
            _draft = null;
            if (live.Location is not null)
            {
                TextBox draft = new() { Text = live.Draft, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 80, Header = "Task request (Ctrl+Enter to send)" };
                AutomationProperties.SetName(draft, "Task request. Control Enter to send. Draft retained.");
                draft.TextChanged += (_, _) =>
                {
                    live.Draft = draft.Text;
                    UpdateSend();
                };
                _draft = draft;
                Composer.Children.Add(draft);
                AddTaskComposer(Composer);
            }
        }
    }

    private async void OpenDirectory_ClickAsync(object sender, RoutedEventArgs args)
    {
        if (_live is not null)
        {
            await _live.OpenProjectAsync(ServerDirectory.Text).ConfigureAwait(true);
        }
    }

    private async void RetryWorkspace_ClickAsync(object sender, RoutedEventArgs args)
    {
        if (_live is not null)
        {
            await _live.RetryAsync().ConfigureAwait(true);
        }
    }
}
