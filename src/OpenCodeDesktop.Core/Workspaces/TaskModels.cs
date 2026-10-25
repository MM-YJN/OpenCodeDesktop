namespace OpenCodeDesktop.Core.Workspaces;

public sealed record TaskModel(string Id, string ProviderId, string Name);
public sealed record TaskAgent(string Id, string Name);
public sealed record TaskCatalog(IReadOnlyList<TaskModel> Models, IReadOnlyList<TaskAgent> Agents);
public sealed record TaskSubmission(string Id, string Text)
{
    public IReadOnlyList<PromptAttachment> Files { get; init; } = [];
}
public sealed record TaskPermission(string Id, string SessionId, string Action, IReadOnlyList<string> Resources, IReadOnlyList<string> Save);
public sealed record TaskOption(string Value, string Label);
public sealed record TaskCondition(string Key, string Operator, object Value);
public sealed record TaskField(string Key, string Type)
{
    public string? Title { get; init; }
    public string? Description { get; init; }
    public string? Placeholder { get; init; }
    public bool Required { get; init; }
    public bool Hidden { get; init; }
    public bool Custom { get; init; }
    public object? Default { get; init; }
    public double? Minimum { get; init; }
    public double? Maximum { get; init; }
    public int? MinLength { get; init; }
    public int? MaxLength { get; init; }
    public int? MinItems { get; init; }
    public int? MaxItems { get; init; }
    public string? Pattern { get; init; }
    public string? Format { get; init; }
    public string? Url { get; init; }
    public IReadOnlyList<TaskOption> Options { get; init; } = [];
    public IReadOnlyList<TaskCondition> When { get; init; } = [];
}
public sealed record TaskForm(string Id, string SessionId, string Title, IReadOnlyList<TaskField> Fields, string State = "pending");
public sealed record TaskSnapshot(WorkspaceSession Session, bool Active, string? Outcome, double? Idle,
    TaskModel? Model, string? Agent, IReadOnlyList<string> Inbox, IReadOnlyList<TaskPermission> Permissions, IReadOnlyList<TaskForm> Forms);
public sealed record TaskEvent(string Id, string Type, string? SessionId, string? MessageId, int Ordinal, string? Delta, string? ToolId = null, string? ToolName = null);

/// <summary>Task protocol; implementations perform transport and parsing away from the UI thread.</summary>
public interface ITaskConnection
{
    Task<TaskCatalog> CatalogAsync(string directory, CancellationToken cancellationToken);
    Task<IReadOnlyList<string>> AttentionSessionsAsync(string directory, CancellationToken cancellationToken);
    Task<TaskSnapshot> TaskSnapshotAsync(string id, CancellationToken cancellationToken);
    Task SelectModelAsync(string id, TaskModel model, CancellationToken cancellationToken);
    Task SelectAgentAsync(string id, string agent, CancellationToken cancellationToken);
    Task SubmitAsync(string id, TaskSubmission submission, CancellationToken cancellationToken);
    Task InterruptAsync(string id, CancellationToken cancellationToken);
    Task ReplyPermissionAsync(string id, string requestId, string decision, CancellationToken cancellationToken);
    Task<TaskForm> FormAsync(string id, string formId, CancellationToken cancellationToken);
    Task ReplyFormAsync(string id, string formId, IReadOnlyDictionary<string, object> answer, CancellationToken cancellationToken);
    Task CancelFormAsync(string id, string formId, CancellationToken cancellationToken);
    Task WatchAsync(Func<TaskEvent, Task> receive, CancellationToken cancellationToken);
}
