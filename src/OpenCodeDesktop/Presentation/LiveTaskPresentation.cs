using OpenCodeDesktop.Core.Workspaces;

namespace OpenCodeDesktop.Presentation;

internal sealed record LiveTaskPresentation(string Status, string Error, string CatalogNotice, bool IsBusy, bool CanRefresh, bool CanSend, bool CanStop, bool CanSelect)
{
    internal static LiveTaskPresentation From(SessionTaskState? state, string? draft, string? attentionError) => new(
        state?.Status ?? "Choose a session to run a task.",
        state?.Error ?? attentionError ?? "",
        state is { Hydrated: true } && (state.Catalog.Models.Count == 0 || state.Catalog.Agents.Count == 0)
            ? "No usable model or primary agent is available. Enable a model and its provider access on the server, then refresh." : "",
        state is { Loading: true } or { Busy: true },
        state is { Loading: false, Busy: false },
        state?.CanSend == true && !string.IsNullOrWhiteSpace(draft),
        state is { Active: true, Busy: false, Stale: false },
        state is { Busy: false, Active: false, Loading: false, Stale: false, Submission: null });
}
