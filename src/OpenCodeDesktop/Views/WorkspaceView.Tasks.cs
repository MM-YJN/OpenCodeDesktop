using System;
using System.Collections.Generic;
using System.Linq;
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
    private const string PermissionActionStyle = "ShellPrimaryButtonStyle";
    private string _catalogKey = "";
    private string _attentionKey = "";
    private readonly List<Button> _requestActions = [];
    private readonly List<Control> _requestInputs = [];
    private SessionTaskState? CurrentTask => _live?.Tasks?.Find(_live.SelectedSessionId);

    private void RenderTaskControls()
    {
        SessionTaskState? state = CurrentTask;
        var presentation = LiveTaskPresentation.From(state, DraftBox.Text, _live?.Tasks?.AttentionError, _live?.Tasks);
        ShellStatusPresentation status = ShellPresentation.Task(state);
        string previousStatus = TaskStatus.Text;
        TaskStatusBadge.Background = ShellUi.ToneSurface(status.Tone);
        TaskStatusBadge.BorderBrush = status.Tone == ShellTone.Neutral ? ShellUi.Brush("ShellBorderBrush") : ShellUi.ToneBrush(status.Tone);
        TaskStatusIcon.Glyph = ShellUi.Glyph(status.Glyph);
        TaskStatusIcon.Foreground = status.Tone == ShellTone.Neutral ? ShellUi.Brush("ShellTextSecondaryBrush") : ShellUi.ToneBrush(status.Tone);
        TaskStatus.Text = status.Label;
        TaskStatus.Foreground = status.Tone == ShellTone.Neutral ? ShellUi.Brush("ShellTextPrimaryBrush") : ShellUi.ToneBrush(status.Tone);
        ShellUi.Tooltip(TaskStatusBadge, status.Detail);
        AutomationProperties.SetName(TaskStatusBadge, "Task status: " + status.Label + ". " + status.Detail);
        if (previousStatus != TaskStatus.Text)
        {
            FrameworkElementAutomationPeer.FromElement(TaskStatus)?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
        }

        string previousError = TaskError.Text;
        TaskError.Text = presentation.Error;
        TaskErrorBanner.Visibility = presentation.Error.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        RefreshTask.IsEnabled = presentation.CanRefresh;
        if (previousError != TaskError.Text)
        {
            FrameworkElementAutomationPeer.FromElement(TaskError)?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
        }

        TaskLoading.IsActive = presentation.IsBusy;
        TaskLoading.Visibility = presentation.IsBusy ? Visibility.Visible : Visibility.Collapsed;
        string previousRecovery = RecoveryStatus.Text;
        RecoveryStatus.Text = presentation.RecoveryStatus;
        RecoveryBanner.Visibility = presentation.ShowRecovery ? Visibility.Visible : Visibility.Collapsed;
        RetryRecovery.Visibility = presentation.CanRetryRecovery ? Visibility.Visible : Visibility.Collapsed;
        if (previousRecovery != RecoveryStatus.Text)
        {
            FrameworkElementAutomationPeer.FromElement(RecoveryStatus)?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
        }

        UpdateTaskCatalog(state, presentation.CanSelect);
        CatalogNotice.Text = presentation.CatalogNotice;
        CatalogNotice.Visibility = presentation.CatalogNotice.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        UpdateSend();
        RenderRequests(state);
        bool requestsEnabled = state is { Busy: false, Stale: false };
        foreach (Button button in _requestActions)
        {
            button.IsEnabled = requestsEnabled;
        }

        foreach (Control input in _requestInputs)
        {
            input.IsEnabled = requestsEnabled;
        }
    }

    private void UpdateTaskCatalog(SessionTaskState? state, bool canSelect)
    {
        ModelSelector.IsEnabled = canSelect;
        AgentSelector.IsEnabled = canSelect;
        string catalogKey = (_live?.SelectedSessionId ?? "") + "|" +
            string.Join('|', state?.Catalog.Models.Select(model => model.ProviderId + "/" + model.Id) ?? []) + ":" +
            string.Join('|', state?.Catalog.Agents.Select(agent => agent.Id) ?? []);
        if (_catalogKey != catalogKey)
        {
            _catalogKey = catalogKey;
            ModelSelector.ItemsSource = state?.Catalog.Models;
            ModelSelector.DisplayMemberPath = nameof(TaskModel.Name);
            AgentSelector.ItemsSource = state?.Catalog.Agents;
            AgentSelector.DisplayMemberPath = nameof(TaskAgent.Name);
        }

        ModelSelector.SelectedItem = state?.Model;
        AgentSelector.SelectedItem = state?.Catalog.Agents.FirstOrDefault(agent => agent.Id == state.Agent);
    }

    private void UpdateSend()
    {
        SendButton.IsEnabled = CurrentTask?.CanSend == true && !string.IsNullOrWhiteSpace(DraftBox.Text);
    }

    private async Task SendTaskAsync()
    {
        if (_live is not { } live || CurrentTask is not { CanSend: true })
        {
            return;
        }

        string? owner = live.SelectedSessionId;
        await live.SendTaskAsync().ConfigureAwait(true);
        if (live.SelectedSessionId == owner && DraftBox.Text != live.Draft)
        {
            DraftBox.Text = live.Draft;
        }
    }

    private async void SendTask_ClickAsync(object sender, RoutedEventArgs args) => await SendTaskAsync().ConfigureAwait(true);
    private async void StopTask_ClickAsync(object sender, RoutedEventArgs args) => await StopTaskAsync().ConfigureAwait(true);
    private Task StopTaskAsync() => _live?.Tasks is { } workflow && CurrentTask is { } state ? workflow.StopAsync(state) : Task.CompletedTask;

    private async Task RetryRecoveryAsync()
    {
        if (_live?.Tasks is { } workflow)
        {
            await workflow.RetryRecoveryAsync().ConfigureAwait(true);
        }
    }

    private async Task RefreshCurrentTaskAsync()
    {
        if (_live?.Tasks is not { } workflow || CurrentTask is not { } state)
        {
            return;
        }

        if (workflow.Recovery != WorkspaceRecovery.Current)
        {
            await workflow.RetryRecoveryAsync().ConfigureAwait(true);
        }
        else
        {
            await workflow.RefreshAsync(state).ConfigureAwait(true);
        }
    }

    private async void RetryRecovery_ClickAsync(object sender, RoutedEventArgs args) => await RetryRecoveryAsync().ConfigureAwait(true);
    private async void RefreshTask_ClickAsync(object sender, RoutedEventArgs args) => await RefreshCurrentTaskAsync().ConfigureAwait(true);

    private void RenderRequests(SessionTaskState? state)
    {
        int count = state?.Permissions.Count + state?.Forms.Count ?? 0;
        PendingRequestsSection.Visibility = count == 0 ? Visibility.Collapsed : Visibility.Visible;
        PendingRequestsCount.Text = count == 0 ? "" : count + (count == 1 ? " request" : " requests");
        string key = (_live?.SelectedSessionId ?? "") + "|" + string.Join('|', state?.Permissions.Select(permission => permission.Id) ?? []) +
            ":" + string.Join('|', state?.Forms.Select(form => form.Id) ?? []);
        if (_attentionKey == key)
        {
            return;
        }

        _attentionKey = key;
        PendingRequests.Children.Clear();
        _requestActions.Clear();
        _requestInputs.Clear();
        if (state is null || _live?.Tasks is not { } workflow)
        {
            return;
        }

        foreach (TaskPermission permission in state.Permissions)
        {
            PendingRequests.Children.Add(BuildPermissionCard(state, permission, workflow));
        }

        TaskFormPresenter forms = new(_requestActions, _requestInputs);
        foreach (TaskForm form in state.Forms)
        {
            PendingRequests.Children.Add(forms.Build(state, form, workflow));
        }
    }

    private FrameworkElement BuildPermissionCard(SessionTaskState state, TaskPermission permission, LiveTaskWorkflow workflow)
    {
        StackPanel content = ShellUi.Stack(ShellUi.Dimension("ShellSpaceSmall"));
        StackPanel header = ShellUi.Stack(ShellUi.Dimension("ShellSpaceSmall"), Orientation.Horizontal);
        header.Children.Add(ShellUi.Icon("ShellIconLock", ShellUi.Dimension("ShellIconSmall"), ShellUi.Brush("ShellWarningBrush")));
        header.Children.Add(ShellUi.Text("Permission request", "ShellBodyStrongTextStyle"));
        header.Children.Add(ShellUi.Badge(permission.Action, ShellTone.Warning));
        content.Children.Add(header);
        content.Children.Add(ShellUi.Text("The server is asking to perform this action.", "ShellSecondaryTextStyle"));
        if (permission.Resources.Count > 0)
        {
            content.Children.Add(ShellUi.Text("Resources", "ShellTertiaryTextStyle"));
            content.Children.Add(CodeBlock(string.Join("\n", permission.Resources), wrap: true));
        }

        if (permission.Save.Count > 0)
        {
            content.Children.Add(ShellUi.Text("Proposed project allow rule", "ShellTertiaryTextStyle"));
            content.Children.Add(ShellUi.Text(permission.Action + " · " + string.Join(", ", permission.Save), "ShellSecondaryTextStyle", TextWrapping.Wrap));
        }

        StackPanel actions = ShellUi.Stack(ShellUi.Dimension("ShellSpaceSmall"), Orientation.Horizontal);
        Button allow = RequestAction("Allow once", () => workflow.PermissionAsync(state, permission, "once"), PermissionActionStyle);
        actions.Children.Add(allow);
        if (permission.Save.Count > 0)
        {
            actions.Children.Add(RequestAction("Save project allow rule", () => workflow.PermissionAsync(state, permission, "always"), "ShellSecondaryButtonStyle"));
        }

        actions.Children.Add(RequestAction("Reject", () => workflow.PermissionAsync(state, permission, "reject"), "ShellQuietButtonStyle"));
        content.Children.Add(actions);

        Border card = new()
        {
            Style = ShellUi.Resource<Style>("ShellCardStyle"),
            Background = ShellUi.Brush("ShellWarningSurfaceBrush"),
            BorderBrush = ShellUi.Brush("ShellWarningBrush"),
            Child = content,
        };
        AutomationProperties.SetName(card, "Permission request: " + permission.Action);
        return card;
    }

    private Button RequestAction(string label, Func<Task> action, string style)
    {
        Button button = ShellUi.LabelButton(label, null, async (_, _) => await action().ConfigureAwait(true), style);
        _requestActions.Add(button);
        return button;
    }
}
