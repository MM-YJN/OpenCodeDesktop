using OpenCodeDesktop.Core.Workspaces;

namespace OpenCodeDesktop.Infrastructure.Workspaces;

public sealed partial class OpenCodeWorkspaceConnection : ISessionOrganizationConnection
{
    public Task<WorkspacePage<WorkspaceSession>> SearchSessionsAsync(string directory, SessionHistoryFilter filter, string? cursor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);
        return RequestAsync(HttpMethod.Get, "/api/session?directory=" + Escape(directory) +
            "&archived=" + (filter.Archived ? "true" : "false") + "&search=" + Escape(filter.Search) + PageQuery(cursor),
            null, 1048576, root => Page(root, Session), cancellationToken);
    }

    public Task RenameSessionAsync(string id, string title, CancellationToken cancellationToken)
    {
        if (!SessionTitles.IsValid(title)) { throw new ArgumentException(SessionTitles.Guidance, nameof(title)); }
        return Mutation(HttpMethod.Patch, Path(id), new { title }, cancellationToken);
    }

    public Task ArchiveSessionAsync(string id, bool archived, CancellationToken cancellationToken) =>
        Mutation(HttpMethod.Patch, Path(id), new { archived }, cancellationToken);
}
