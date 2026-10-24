namespace OpenCodeDesktop.Core.Workspaces;

public sealed record SavedProject(string Directory, string ProjectId, string? SessionId);
public sealed record SavedConnection(Guid Id, string Name, string Address, bool RememberCredentials,
    string? CredentialReference, string? SelectedDirectory, IReadOnlyList<SavedProject> Projects);
public sealed record WorkspaceSettings(int Version, Guid? ActiveConnectionId, IReadOnlyList<SavedConnection> Connections)
{
    public const int CurrentVersion = 1;
}

public interface IWorkspaceSettingsStore
{
    Task<WorkspaceSettings?> LoadAsync(CancellationToken cancellationToken);
    Task SaveAsync(WorkspaceSettings settings, CancellationToken cancellationToken);
    /// <summary>Preserves the original file for recovery before enabling a new store.</summary>
    Task ResetAsync(CancellationToken cancellationToken);
}

public interface ICredentialStore
{
    Task<string> SaveAsync(string password, CancellationToken cancellationToken);
    Task<string> ReadAsync(string reference, CancellationToken cancellationToken);
    Task DeleteAsync(string reference, CancellationToken cancellationToken);
}
