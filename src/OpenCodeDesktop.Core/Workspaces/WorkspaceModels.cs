namespace OpenCodeDesktop.Core.Workspaces;

public sealed record ProjectLocation(string Directory, string ProjectId);
public sealed record WorkspaceProject(
    ProjectLocation Location,
    bool IsSelected,
    string? Error,
    IReadOnlyList<WorkspaceSession> Sessions,
    bool SessionsLoaded,
    bool SessionsLoading,
    string? SessionsError,
    bool CanLoadMoreSessions);
public sealed record WorkspaceSession(string Id, string Directory, string ProjectId, string Title);
public enum HistoryFailureOutcome { Failed, Interrupted }
public sealed record HistoryContentPart(int Ordinal, string Text, bool IsText);
public sealed record HistoryFileReference(string Uri, string? Name);

public sealed record HistoryMessage(string Id, string Kind, string Text)
{
    public HistoryFailureOutcome? CompletedFailure { get; init; }
    public bool IsComplete { get; init; } = true;
    public IReadOnlyList<HistoryContentPart> Parts { get; init; } = [];
    public IReadOnlyList<HistoryFileReference> FileReferences { get; init; } = [];
}
public sealed record WorkspacePage<T>(IReadOnlyList<T> Data, string? Next);

public enum WorkspaceFailure { Cancelled, Unauthorized, Timeout, Transport, NotFound, Http, Protocol, Size }

/// <summary>Only client-authored text belongs in an operation failure.</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1032:Implement standard exception constructors", Justification = "Failures require a classified outcome and client-authored safe text; arbitrary exception wrapping is intentionally excluded.")]
public sealed class WorkspaceException(WorkspaceFailure failure, string message) : Exception(message)
{
    public WorkspaceFailure Failure { get; } = failure;
}

public interface IWorkspaceConnection : IDisposable
{
    Task<ProjectLocation> ResolveAsync(string directory, CancellationToken cancellationToken);
    Task ValidateDirectoryAsync(string directory, CancellationToken cancellationToken);
    Task<WorkspacePage<WorkspaceSession>> SessionsAsync(string directory, string? cursor, CancellationToken cancellationToken);
    Task<WorkspaceSession> CreateAsync(string directory, CancellationToken cancellationToken);
    Task<WorkspaceSession> SessionAsync(string id, CancellationToken cancellationToken);
    Task<WorkspacePage<HistoryMessage>> HistoryAsync(string id, string? cursor, CancellationToken cancellationToken);
}

public interface IWorkspaceConnectionFactory
{
    IWorkspaceConnection Create(Core.Connections.ServerAddress address, string password);
}
