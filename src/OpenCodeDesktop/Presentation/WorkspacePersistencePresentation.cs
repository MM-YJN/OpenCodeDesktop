using OpenCodeDesktop.Core.Workspaces;

namespace OpenCodeDesktop.Presentation;

internal sealed record WorkspacePersistencePresentation(string? Message, bool IsRestoring, bool IsError, bool CanRetry, bool CanSave, bool CanReset)
{
    public static WorkspacePersistencePresentation From(WorkspacePersistence persistence) => new(
        persistence.Error ?? persistence.Status, persistence.IsRestoring, persistence.Error is not null,
        persistence.Error is not null && persistence.CanRetry,
        persistence.CanRetrySave,
        persistence.NeedsReset && !persistence.IsRestoring);
}
