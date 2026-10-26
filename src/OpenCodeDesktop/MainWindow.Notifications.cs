using System;
using System.Linq;
using System.Threading.Tasks;

using Microsoft.UI.Xaml;

using OpenCodeDesktop.Core.Workspaces;
using OpenCodeDesktop.Notifications;

namespace OpenCodeDesktop;

public sealed partial class MainWindow
{
    private readonly WindowsTaskNotifications _notifications = new();
    private readonly TaskNotificationPolicy _notificationPolicy = new();
    private readonly TaskCompletionSource _notificationReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool _foreground;

    private void InitializeNotifications()
    {
        Activated += (_, args) => _foreground = args.WindowActivationState != WindowActivationState.Deactivated;
        _notifications.Invoked += NotificationInvoked;
        _notifications.Register();
    }

    private async void ObserveNotificationsAsync(object? sender, EventArgs args)
    {
        if (_closing || _liveWorkspace.Server is not { } server || !_liveWorkspace.IsConnected) { return; }
        foreach (SessionTaskState state in (_liveWorkspace.Tasks?.Sessions ?? []).ToArray())
        {
            bool visible = _foreground && _workspaceView.Visibility == Visibility.Visible && _workspaceView.IsSessionVisible(state);
            var candidates = _notificationPolicy.Observe(server.Profile.Id, server.Profile.Address.Display,
                state.NotificationObservation, _liveWorkspace.NotificationPreferences, visible);
            foreach (NotificationCandidate candidate in candidates)
            {
                if (_closing || _liveWorkspace.Server != server || _liveWorkspace.Tasks?.Find(state.Id) != state) { return; }
                TaskNotificationTarget target = _liveWorkspace.RememberNotification(state, candidate);
                // Persist opaque activation ownership before making the Windows notification actionable.
                await _persistence.FlushAsync().ConfigureAwait(true);
                if (_closing || _liveWorkspace.Server != server || !_liveWorkspace.IsConnected) { return; }
                if (!StillEligible(state, candidate))
                {
                    continue;
                }
                if (_persistence.Error is null && !_persistence.NeedsReset)
                {
                    _notifications.Show(target);
                }
                else
                {
                    _settingsView.NotificationStatus = "Notification was not sent because its destination could not be saved. Review workspace storage guidance.";
                    continue;
                }
                _settingsView.NotificationStatus = _notifications.Status;
            }
        }
    }

    private bool StillEligible(SessionTaskState state, NotificationCandidate candidate)
    {
        if (state.Stale || !state.Hydrated || _foreground && _workspaceView.Visibility == Visibility.Visible && _workspaceView.IsSessionVisible(state)) { return false; }
        return candidate.Kind switch
        {
            TaskNotificationKind.Completion => _liveWorkspace.NotificationPreferences.Completion && state.Outcome == "Completed" && !state.Active,
            TaskNotificationKind.Permission => _liveWorkspace.NotificationPreferences.Attention && state.Permissions.Any(p => p.Id == candidate.RequestId),
            TaskNotificationKind.Form => _liveWorkspace.NotificationPreferences.Attention && state.Forms.Any(f => f.Id == candidate.RequestId),
            _ => false,
        };
    }

    private void NotificationInvoked(object? sender, Guid token)
    {
        DispatcherQueue.TryEnqueue(async () =>
        {
            if (_closing) { return; }
            Activate();
            await _notificationReady.Task.ConfigureAwait(true);
            if (_closing) { return; }
            ShowSettings(false);
            string? guidance;
            try { guidance = await _liveWorkspace.ActivateNotificationAsync(token).ConfigureAwait(true); }
            catch (Exception) { guidance = "The notification destination could not be opened. Review the session from Projects."; }
            if (_closing) { return; }
            NotificationGuidance.Message = guidance ?? "Session opened. Review its current state before responding.";
            NotificationGuidance.IsOpen = true;
            if (guidance is null && _liveWorkspace.FindNotification(token) is { } target) { _workspaceView.FocusNotificationRequest(target); }
        });
    }
}
