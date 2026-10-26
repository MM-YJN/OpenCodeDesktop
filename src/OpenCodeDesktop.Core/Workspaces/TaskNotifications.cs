namespace OpenCodeDesktop.Core.Workspaces;

public sealed record NotificationPreferences(bool Completion = true, bool Attention = true);
public enum TaskNotificationKind { Completion, Permission, Form }
public sealed record NotificationObservation(string SessionId, string Directory, string ProjectId,
    bool Confirmed, bool Completed, double? CompletionBoundary, IReadOnlyList<string> Permissions, IReadOnlyList<string> Forms,
    bool AttentionBaselineKnown = false);
public sealed record TaskNotificationTarget(Guid Token, Guid ConnectionId, string Address, string Directory,
    string ProjectId, string SessionId, TaskNotificationKind Kind, string? RequestId, DateTimeOffset Created);
public sealed record NotificationCandidate(TaskNotificationKind Kind, string? RequestId);

/// <summary>In-memory observation boundaries, retained across reconnects but deliberately baselined on restart.
/// Call only on the workspace synchronization context. Suppression consumes an identity too.</summary>
public sealed class TaskNotificationPolicy
{
    private sealed class Observed
    {
        public double? Boundary { get; set; }
        public HashSet<string> Permissions { get; } = new(StringComparer.Ordinal);
        public HashSet<string> Forms { get; } = new(StringComparer.Ordinal);
    }
    private readonly Dictionary<(Guid Connection, string Address, string Directory, string Project, string Session), Observed> _observed = [];

    public IReadOnlyList<NotificationCandidate> Observe(Guid connection, string address, NotificationObservation observation,
        NotificationPreferences preferences, bool foregroundOwnerVisible)
    {
        ArgumentNullException.ThrowIfNull(observation);
        ArgumentNullException.ThrowIfNull(preferences);
        if (!observation.Confirmed) { return []; }
        (Guid, string, string, string, string) key = (connection, address, observation.Directory, observation.ProjectId, observation.SessionId);
        bool baseline = !_observed.TryGetValue(key, out Observed? previous);
        previous ??= new();
        _observed[key] = previous;
        List<NotificationCandidate> candidates = [];
        if (observation.CompletionBoundary is { } boundary &&
            (previous.Boundary is null || boundary > previous.Boundary))
        {
            if (!baseline && observation.Completed && preferences.Completion)
            {
                candidates.Add(new(TaskNotificationKind.Completion, null));
            }
            previous.Boundary = boundary;
        }
        foreach (string id in observation.Permissions)
        {
            if (previous.Permissions.Add(id) && (!baseline || observation.AttentionBaselineKnown) && preferences.Attention)
            {
                candidates.Add(new(TaskNotificationKind.Permission, id));
            }
        }
        foreach (string id in observation.Forms)
        {
            if (previous.Forms.Add(id) && (!baseline || observation.AttentionBaselineKnown) && preferences.Attention)
            {
                candidates.Add(new(TaskNotificationKind.Form, id));
            }
        }
        return foregroundOwnerVisible ? [] : candidates;
    }
}
