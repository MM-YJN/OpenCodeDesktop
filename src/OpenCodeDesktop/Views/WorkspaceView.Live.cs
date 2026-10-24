using System;
using System.Threading.Tasks;

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

    internal void Bind(LiveWorkspace workspace)
    {
        _live = workspace;
        workspace.Changed += LiveChanged;
        LiveChanged(this, EventArgs.Empty);
    }

    internal void Unbind()
    {
        _live?.Changed -= LiveChanged;
    }

    private void LiveChanged(object? sender, EventArgs args)
    {
        RenderLive();
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
        OpenDirectoryButton.IsEnabled = live.IsConnected && !live.IsLoading;
        ServerDirectory.IsEnabled = live.IsConnected;
        NewTaskButton.Content = "New-session draft";
        NewTaskButton.IsEnabled = live.Location is not null && !live.IsLoading;
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
        LiveDetails.Text = LiveWorkspacePresentation.UnavailableDetails;
        LiveDetails.Visibility = Visibility.Visible;
        TerminalToggle.IsEnabled = false;
    }

    private void RenderLiveProjects(LiveWorkspace live)
    {
        ProjectList.Children.Clear();
        if (live.IsConnected)
        {
            foreach (string directory in live.OpenedDirectories)
            {
                string path = directory;
                ProjectList.Children.Add(LiveAction(path, () => live.OpenProjectAsync(path), !live.IsLoading));
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
        Conversation.Children.Clear();
        if (presentation.ConversationNotice.Length > 0)
        {
            Conversation.Children.Add(BodyText(presentation.ConversationNotice));
        }

        if (live.CanLoadHistory)
        {
            Conversation.Children.Add(LiveAction("Load older messages", live.LoadOlderHistoryAsync));
        }

        foreach (HistoryMessage message in live.History)
        {
            Conversation.Children.Add(Message("Recorded " + message.Kind + "\n" + message.Text));
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
                TextBox draft = new() { Text = live.Draft, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 80, Header = "Unsent draft (sending unavailable)" };
                AutomationProperties.SetName(draft, "Unsent session draft. Sending is unavailable.");
                draft.TextChanged += (_, _) => live.Draft = draft.Text;
                _draft = draft;
                Composer.Children.Add(draft);
                Composer.Children.Add(new Button { Content = "Send unavailable", IsEnabled = false });
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
