using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

using OpenCodeDesktop.Core.Workspaces;
using OpenCodeDesktop.Presentation;

using Windows.System;

namespace OpenCodeDesktop.Views;

public sealed partial class WorkspaceView
{
    private Button? _send;
    private Button? _stop;
    private ComboBox? _model;
    private ComboBox? _agent;
    private string _catalogKey = "";
    private string _attentionKey = "";
    private readonly List<Button> _requestActions = [];
    private readonly List<Control> _requestInputs = [];
    private SessionTaskState? CurrentTask => _live?.Tasks?.Find(_live.SelectedSessionId);

    private void RenderTaskControls()
    {
        SessionTaskState? state = CurrentTask;
        var presentation = LiveTaskPresentation.From(state, _draft?.Text, _live?.Tasks?.AttentionError);
        string previousStatus = TaskStatus.Text;
        string previousError = TaskError.Text;
        TaskStatus.Text = presentation.Status;
        TaskError.Text = presentation.Error;
        TaskError.Visibility = TaskError.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        if (previousStatus != TaskStatus.Text)
        {
            FrameworkElementAutomationPeer.FromElement(TaskStatus)?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
        }

        if (previousError != TaskError.Text)
        {
            FrameworkElementAutomationPeer.FromElement(TaskError)?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
        }

        TaskLoading.Visibility = presentation.IsBusy ? Visibility.Visible : Visibility.Collapsed;
        RefreshTask.IsEnabled = presentation.CanRefresh;
        _send?.IsEnabled = presentation.CanSend;

        _stop?.IsEnabled = presentation.CanStop;

        UpdateTaskCatalog(state, presentation.CanSelect);
        CatalogNotice.Text = presentation.CatalogNotice;
        int count = _live?.Tasks?.Sessions.Sum(s => s.Permissions.Count + s.Forms.Count) ?? 0;
        AttentionButton.Content = "Attention (" + count + ")";
        AttentionButton.IsEnabled = count > 0;
        RenderRequests(state);
        foreach (Button button in _requestActions)
        {
            button.IsEnabled = state is { Busy: false, Stale: false };
        }

        foreach (Control input in _requestInputs)
        {
            input.IsEnabled = state is { Busy: false, Stale: false };
        }
    }

    private void UpdateTaskCatalog(SessionTaskState? state, bool canSelect)
    {
        bool select = canSelect;
        if (_model is not null && _agent is not null)
        {
            _model.IsEnabled = select;
            _agent.IsEnabled = select;
            string catalogKey = (_live?.SelectedSessionId ?? "") + "|" + string.Join('|', state?.Catalog.Models.Select(m => m.ProviderId + "/" + m.Id) ?? []) + ":" + string.Join('|', state?.Catalog.Agents.Select(a => a.Id) ?? []);
            if (_catalogKey != catalogKey)
            {
                _catalogKey = catalogKey;
                _model.ItemsSource = state?.Catalog.Models;
                _model.DisplayMemberPath = nameof(TaskModel.Name);
                _agent.ItemsSource = state?.Catalog.Agents;
                _agent.DisplayMemberPath = nameof(TaskAgent.Name);
            }
            _model.SelectedItem = state?.Model;
            _agent.SelectedItem = state?.Catalog.Agents.FirstOrDefault(a => a.Id == state.Agent);
        }
    }

    private void AddTaskComposer(StackPanel composer)
    {
        _catalogKey = "";
        _model = new ComboBox { Header = "Model", HorizontalAlignment = HorizontalAlignment.Stretch };
        _agent = new ComboBox { Header = "Agent", HorizontalAlignment = HorizontalAlignment.Stretch };
        _model.SelectionChanged += (_, _) =>
        {
            if (CurrentTask is { Busy: false, Active: false } s && _model.SelectedItem is TaskModel m)
            {
                s.Model = m;
            }

            UpdateSend();
        };
        _agent.SelectionChanged += (_, _) =>
        {
            if (CurrentTask is { Busy: false, Active: false } s && _agent.SelectedItem is TaskAgent a)
            {
                s.Agent = a.Id;
            }

            UpdateSend();
        };
        composer.Children.Add(_model);
        composer.Children.Add(_agent);
        _send = LiveAction("Send (Ctrl+Enter)", SendTaskAsync, false);
        _stop = LiveAction("Interrupt task", StopTaskAsync, false);
        composer.Children.Add(_send);
        composer.Children.Add(_stop);
        KeyboardAccelerator sendShortcut = new() { Key = VirtualKey.Enter, Modifiers = VirtualKeyModifiers.Control };
        sendShortcut.Invoked += async (_, args) =>
        {
            args.Handled = true;
            await SendTaskAsync().ConfigureAwait(true);
        };
        _draft?.KeyboardAccelerators.Add(sendShortcut);
    }
    private void UpdateSend()
    {
        _send?.IsEnabled = CurrentTask?.CanSend == true && !string.IsNullOrWhiteSpace(_draft?.Text);
    }
    private async Task SendTaskAsync()
    {
        if (_live is { } live && CurrentTask is { CanSend: true })
        {
            string? owner = Composer.Tag as string;
            await live.SendTaskAsync().ConfigureAwait(true);
            if (Equals(Composer.Tag, owner) && _draft is not null && _draft.Text != live.Draft)
            {
                _draft.Text = live.Draft;
            }
        }
    }
    private Task StopTaskAsync() => _live?.Tasks is { } workflow && CurrentTask is { } state ? workflow.StopAsync(state) : Task.CompletedTask;
    private async void RefreshTask_ClickAsync(object sender, RoutedEventArgs args)
    {
        if (_live?.Tasks is { } workflow && CurrentTask is { } state)
        {
            await workflow.RefreshAsync(state).ConfigureAwait(true);
        }
    }
    private void Attention_Click(object sender, RoutedEventArgs args)
    {
        MenuFlyout flyout = new();
        foreach (SessionTaskState state in _live?.Tasks?.Sessions ?? [])
        {
            if (state.Permissions.Count + state.Forms.Count == 0)
            {
                continue;
            }

            MenuFlyoutItem item = new() { Text = state.Directory + " · " + state.Id + " (" + (state.Permissions.Count + state.Forms.Count) + ")" };
            item.Click += async (_, _) =>
            {
                if (_live is { } live)
                {
                    await live.OpenProjectAsync(state.Directory).ConfigureAwait(true);
                    await live.SelectAttentionSessionAsync(state.Id).ConfigureAwait(true);
                }
            };
            flyout.Items.Add(item);
        }
        flyout.ShowAt(AttentionButton);
    }

    private void RenderRequests(SessionTaskState? state)
    {
        string key = (_live?.SelectedSessionId ?? "") + "|" + string.Join('|', state?.Permissions.Select(p => p.Id) ?? []) + ":" + string.Join('|', state?.Forms.Select(f => f.Id) ?? []);
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

        Button Action(string label, Func<Task> action)
        {
            Button button = LiveAction(label, action);
            _requestActions.Add(button);
            return button;
        }
        foreach (TaskPermission permission in state.Permissions)
        {
            StackPanel panel = new() { Spacing = 8 };
            panel.Children.Add(BodyText("Permission: " + permission.Action + "\nResources: " + string.Join(", ", permission.Resources)));
            panel.Children.Add(Action("Allow once — this request only", () => workflow.PermissionAsync(state, permission, "once")));
            if (permission.Save.Count > 0)
            {
                panel.Children.Add(BodyText("Proposed project allow rule: " + permission.Action + " · " + string.Join(", ", permission.Save)));
                panel.Children.Add(Action("Save project allow rule", () => workflow.PermissionAsync(state, permission, "always")));
            }
            panel.Children.Add(Action("Reject request", () => workflow.PermissionAsync(state, permission, "reject")));
            PendingRequests.Children.Add(panel);
        }
        TaskFormPresenter forms = new(BodyText, Action, _requestInputs);
        foreach (TaskForm form in state.Forms)
        {
            PendingRequests.Children.Add(forms.Build(state, form, workflow));
        }
    }

    private void AddMessage(HistoryMessage message)
    {
        StackPanel parts = new() { Spacing = 8 };
        parts.Children.Add(BodyText(message.Kind));
        string[] segments = message.Text.Split("```", StringSplitOptions.None);
        for (int i = 0; i < segments.Length; i++)
        {
            TextBlock text = BodyText(segments[i]);
            text.IsTextSelectionEnabled = true;
            if (i % 2 == 1)
            {
                text.FontFamily = new FontFamily("Cascadia Mono,Consolas");
            }

            parts.Children.Add(text);
        }
        Conversation.Children.Add(parts);
    }
}
