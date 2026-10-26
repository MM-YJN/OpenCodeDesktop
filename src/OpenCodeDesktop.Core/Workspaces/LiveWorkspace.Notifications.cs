namespace OpenCodeDesktop.Core.Workspaces;

public sealed partial class LiveWorkspace
{
    private readonly List<TaskNotificationTarget> _notificationTargets = [];
    public NotificationPreferences NotificationPreferences { get; private set; } = new();
    public TaskNotificationTarget? FindNotification(Guid token) => _notificationTargets.FirstOrDefault(t => t.Token == token);

    public void SetNotificationPreferences(NotificationPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        NotificationPreferences = preferences;
        NavigationChanged?.Invoke(this, EventArgs.Empty);
        Notify();
    }

    public TaskNotificationTarget RememberNotification(SessionTaskState state, NotificationCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(candidate);
        if (!IsConnected || Server is not { } server || Tasks?.Find(state.Id) != state)
        {
            throw new InvalidOperationException("The notification no longer belongs to the connected workspace.");
        }
        TaskNotificationTarget target = new(Guid.NewGuid(), server.Profile.Id, server.Profile.Address.Display,
            state.Directory, state.ProjectId, state.Id, candidate.Kind, candidate.RequestId, DateTimeOffset.UtcNow);
        _notificationTargets.RemoveAll(t => t.Created < DateTimeOffset.UtcNow.AddDays(-7));
        _notificationTargets.Add(target);
        if (_notificationTargets.Count > 128) { _notificationTargets.RemoveAt(0); }
        NavigationChanged?.Invoke(this, EventArgs.Empty);
        return target;
    }

    /// <summary>Uses the ordinary validated navigation transaction. Never connects, switches, or replies.</summary>
    public async Task<string?> ActivateNotificationAsync(Guid token)
    {
        TaskNotificationTarget? target = FindNotification(token);
        if (target is null || target.Created < DateTimeOffset.UtcNow.AddDays(-7))
        {
            return "This notification has expired or its saved destination is unavailable. Open the session from Projects.";
        }
        if (!IsConnected || Server is not { } server || server.Profile.Id != target.ConnectionId || server.Profile.Address.Display != target.Address)
        {
            return "Connect to the notification's original server explicitly in Settings > Connections, then open the notification again.";
        }
        if (!Projects.Any(p => p.Location.Directory == target.Directory && p.Location.ProjectId == target.ProjectId))
        {
            return "The notification's project was removed. Add it from Projects to review the session.";
        }
        await OpenProjectSessionAsync(target.Directory, target.SessionId).ConfigureAwait(true);
        if (!IsConnected || Server?.Profile.Id != target.ConnectionId || Server.Profile.Address.Display != target.Address ||
            Location?.Directory != target.Directory || Location.ProjectId != target.ProjectId ||
            Selected?.Id != target.SessionId || Error is not null)
        {
            return "The notification's session is unavailable or navigation changed. Select the session from Projects.";
        }
        SessionTaskState? state = Tasks?.Find(target.SessionId);
        if (target.Kind != TaskNotificationKind.Completion && (state is not { Stale: false, Hydrated: true } ||
            !(target.Kind == TaskNotificationKind.Permission ? state.Permissions.Any(p => p.Id == target.RequestId) : state.Forms.Any(f => f.Id == target.RequestId))))
        {
            return "This request is no longer pending or could not be confirmed. Review the session's current state.";
        }
        return null;
    }
}
