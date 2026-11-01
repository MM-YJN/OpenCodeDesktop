using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;

using OpenCodeDesktop.Core.Workspaces;
using OpenCodeDesktop.Presentation;

using Windows.System;

namespace OpenCodeDesktop.Views;

public sealed partial class WorkspaceView
{
    private const string PermissionActionStyle = "ShellPrimaryButtonStyle";
    private const string VariantDefault = "Default";
    private string _catalogKey = "";
    private string _attentionKey = "";
    // The last model/agent/variant rendered into the selectors. SelectionChanged echoes of
    // programmatic assignments compare equal; only a real user choice differs.
    private TaskModel? _expectedModel;
    private string? _expectedAgent;
    private string? _expectedVariant;
    // Grouped model flyout state: the last catalog contents, the filtered groups behind the
    // CollectionViewSource, and whether the flyout is currently open.
    private IReadOnlyList<TaskModel> _catalogModels = [];
    private IReadOnlyList<TaskProvider> _catalogProviders = [];
    private IReadOnlyList<TaskModelGroup> _modelGroups = [];
    private readonly CollectionViewSource _modelView = new() { IsSourceGrouped = true, ItemsPath = new PropertyPath(nameof(TaskModelGroup.Models)) };
    private bool _modelFlyoutOpen;
    private bool _catalogSelectable;
    private string _variantKey = "";
    // Set for the duration of a draft send (session creation + submission) so transient
    // navigation states do not flash the conversation area.
    private bool _sendingDraft;
    private bool _draftSendExpectsNavigation;
    private readonly List<Button> _requestActions = [];
    private readonly Dictionary<(TaskNotificationKind Kind, string Id), Button> _notificationRequestActions = [];
    private readonly List<Control> _requestInputs = [];
    private SessionTaskState? CurrentTask => _live?.Tasks?.Find(_live.SelectedSessionId);

    private void RenderTaskControls()
    {
        SessionTaskState? state = CurrentTask;
        var presentation = LiveTaskPresentation.From(state, DraftBox.Text, _live?.Tasks?.AttentionError, _live?.Tasks, _live?.CurrentDraft?.Files.Count ?? 0);
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
        RenderAttachments();
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
        LiveWorkspace? live = _live;
        // A sessionless draft offers the directory catalog directly; a tracked session offers its own snapshot.
        bool draft = state is null && live is { IsConnected: true, Location: not null, SelectedSessionId: null };
        TaskCatalog? catalog = state?.Catalog ?? (draft ? live!.DraftCatalog : null);
        IReadOnlyList<TaskModel> models = catalog?.Models ?? [];
        IReadOnlyList<TaskAgent> agents = catalog?.Agents ?? [];
        bool selectable = state is not null ? canSelect : draft && catalog is not null;
        _catalogSelectable = selectable;
        ModelSelector.IsEnabled = selectable;
        AgentSelector.IsEnabled = selectable;
        string catalogKey = (live?.SelectedSessionId ?? "draft:" + live?.Location?.Directory) + "|" +
            string.Join('|', models.Select(model => model.ProviderId + "/" + model.Id + "#" + string.Join(',', model.Variants))) + ":" +
            string.Join('|', agents.Select(agent => agent.Id)) + ":" +
            string.Join('|', (catalog?.Providers ?? []).Select(provider => provider.Id + "=" + provider.Name));
        if (_catalogKey != catalogKey)
        {
            RebindCatalog(catalogKey, catalog, models, agents);
        }

        TaskModel? modelSelection = state?.Model ??
            (draft ? models.FirstOrDefault(model => model.Id == live!.PreferredModel?.Id && model.ProviderId == live.PreferredModel.ProviderId) : null);
        TaskAgent? agentSelection = state is not null
            ? agents.FirstOrDefault(agent => agent.Id == state.Agent)
            : draft
                ? agents.FirstOrDefault(agent => agent.Id == live!.PreferredAgent) ?? (agents is [TaskAgent only] ? only : null)
                : null;
        _expectedModel = modelSelection;
        _expectedAgent = agentSelection?.Id;
        // Restore the list highlight without treating it as a user choice; content stands in for the removed placeholder.
        ModelList.SelectedItem = modelSelection;
        ModelSelector.Content = modelSelection?.Name ?? "Model";
        AgentSelector.SelectedItem = agentSelection;
        UpdateVariantSelector(state, modelSelection);
    }

    private void UpdateVariantSelector(SessionTaskState? state, TaskModel? modelSelection)
    {
        LiveWorkspace? live = _live;
        bool draft = state is null && live is { IsConnected: true, Location: not null, SelectedSessionId: null };
        IReadOnlyList<string> variants = modelSelection?.Variants ?? [];
        string[] variantItems = variants.Count > 0 ? [VariantDefault, .. variants] : [];
        string? variantSelection = state?.Variant ?? (draft ? live!.PreferredVariant : null);
        if (variantSelection is not null && !variants.Contains(variantSelection, StringComparer.Ordinal))
        {
            variantSelection = null;
        }

        _expectedVariant = variantSelection;
        // Like the model list, the choices rebind only when their contents change; streaming
        // renders must not invalidate the ComboBox layout on every refresh.
        string variantKey = (live?.SelectedSessionId ?? "draft:" + live?.Location?.Directory) + "|" + (modelSelection?.ProviderId ?? "") + "/" +
            (modelSelection?.Id ?? "") + "|" + string.Join(',', variantItems) + "|" + _catalogSelectable;
        if (_variantKey != variantKey)
        {
            _variantKey = variantKey;
            VariantSelector.Visibility = variantItems.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            VariantSelector.IsEnabled = _catalogSelectable && variantItems.Length > 0;
            VariantSelector.ItemsSource = variantItems;
        }

        VariantSelector.SelectedItem = variantItems.Length > 0 ? variantSelection ?? VariantDefault : null;
    }

    private void RebindCatalog(string catalogKey, TaskCatalog? catalog, IReadOnlyList<TaskModel> models, IReadOnlyList<TaskAgent> agents)
    {
        _catalogKey = catalogKey;
        _catalogModels = models;
        _catalogProviders = catalog?.Providers ?? [];
        ModelSearch.Text = "";
        RebuildModelView();
        AgentSelector.ItemsSource = agents;
        AgentSelector.DisplayMemberPath = nameof(TaskAgent.Name);
    }

    private void RebuildModelView()
    {
        _modelGroups = TaskModelGrouping.Build(_catalogModels, _catalogProviders, ModelSearch.Text);
        _modelView.Source = _modelGroups;
        // CollectionViewSource replaces its View when Source changes; the stale instance must not be cached.
        ModelList.ItemsSource = _modelView.View;
        // Keep the highlight on the current model while it survives the filter; the SelectionChanged
        // echo matches _expectedModel and is ignored as a user choice.
        ModelList.SelectedItem = _expectedModel is { } expected && _modelGroups.Any(group => group.Models.Contains(expected)) ? expected : null;
        ModelSearchEmpty.Visibility = _modelGroups.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ModelList_SelectionChanged(object sender, SelectionChangedEventArgs args)
    {
        if (ModelList.SelectedItem is not TaskModel model)
        {
            return;
        }

        if (CurrentTask is { Busy: false, Active: false } state)
        {
            state.Model = model;
            if (state.Variant is not null && !model.Variants.Contains(state.Variant, StringComparer.Ordinal))
            {
                state.Variant = null;
            }
        }

        // Echoes of programmatic selection (possibly deferred after a rebind) match the last
        // rendered value; only an actual user choice differs.
        if (Equals(model, _expectedModel))
        {
            return;
        }

        _expectedModel = model;
        _live?.RememberModelPreference(model, CurrentTask?.Variant);
        ModelSelector.Content = model.Name;
        UpdateVariantSelector(CurrentTask, model);
        if (_modelFlyoutOpen)
        {
            ModelFlyout.Hide();
        }

        UpdateSend();
    }

    private void ModelFlyout_Opened(object? sender, object args)
    {
        _modelFlyoutOpen = true;
        ModelSearch.Focus(FocusState.Programmatic);
    }

    private void ModelFlyout_Closed(object? sender, object args)
    {
        _modelFlyoutOpen = false;
        if (ModelSearch.Text.Length > 0)
        {
            ModelSearch.Text = "";
        }
    }

    private void ModelSearch_TextChanged(object sender, TextChangedEventArgs args) => RebuildModelView();

    private void ModelSearch_KeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (args.Key != VirtualKey.Enter || TaskModelGrouping.First(_modelGroups) is not { } model)
        {
            return;
        }

        args.Handled = true;
        if (Equals(model, _expectedModel))
        {
            ModelFlyout.Hide();
        }
        else
        {
            ModelList.SelectedItem = model;
        }
    }

    private void UpdateSend()
    {
        SendButton.IsEnabled = _live is { } active && ShellPresentation.Composer(active, CurrentTask, DraftBox.Text).CanSend;
    }

    private async Task SendTaskAsync()
    {
        if (_live is not { } live)
        {
            return;
        }

        if (live.SelectedSessionId is null)
        {
            // Draft mode: the session is created on the server as part of sending.
            _sendingDraft = true;
            _draftSendExpectsNavigation = true;
            try
            {
                await live.SendDraftTaskAsync().ConfigureAwait(true);
            }
            finally
            {
                _sendingDraft = false;
                _draftSendExpectsNavigation = false;
                LiveChanged(this, EventArgs.Empty);
            }

            if (DraftBox.Text != live.Draft)
            {
                DraftBox.Text = live.Draft;
            }

            return;
        }

        if (CurrentTask is not { CanSend: true })
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
        _notificationRequestActions.Clear();
        _requestInputs.Clear();
        if (state is null || _live?.Tasks is not { } workflow)
        {
            return;
        }

        foreach (TaskPermission permission in state.Permissions)
        {
            int first = _requestActions.Count;
            PendingRequests.Children.Add(BuildPermissionCard(state, permission, workflow));
            _notificationRequestActions[(TaskNotificationKind.Permission, permission.Id)] = _requestActions[first];
        }

        TaskFormPresenter forms = new(_requestActions, _requestInputs);
        foreach (TaskForm form in state.Forms)
        {
            int first = _requestActions.Count;
            PendingRequests.Children.Add(forms.Build(state, form, workflow));
            if (_requestActions.Count > first) { _notificationRequestActions[(TaskNotificationKind.Form, form.Id)] = _requestActions[first]; }
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
