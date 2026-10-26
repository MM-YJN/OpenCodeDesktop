namespace OpenCodeDesktop.Core.Workspaces;

public sealed record SessionHistoryFilter(string Search = "", bool Archived = false);

public interface ISessionOrganizationConnection
{
    Task<WorkspacePage<WorkspaceSession>> SearchSessionsAsync(string directory, SessionHistoryFilter filter, string? cursor, CancellationToken cancellationToken);
    Task RenameSessionAsync(string id, string title, CancellationToken cancellationToken);
    Task ArchiveSessionAsync(string id, bool archived, CancellationToken cancellationToken);
}

public static class SessionTitles
{
    public const int MaximumLength = 500;
    public static bool IsValid(string title) => !string.IsNullOrWhiteSpace(title) && title.Length <= MaximumLength && !title.Any(char.IsControl);
    public const string Guidance = "Enter a title of 1–500 characters without line breaks or control characters.";
}
