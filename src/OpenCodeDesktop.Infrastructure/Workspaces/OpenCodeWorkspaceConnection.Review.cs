using System.Text.Json;

using OpenCodeDesktop.Core.Workspaces;

namespace OpenCodeDesktop.Infrastructure.Workspaces;

public sealed partial class OpenCodeWorkspaceConnection : IChangeReviewConnection
{
    public async Task<ReviewResult> ReviewAsync(ProjectLocation location, string? sessionId, ReviewComparison comparison, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(location);
        ArgumentNullException.ThrowIfNull(comparison);
        string query = LocationQuery(location.Directory);
        string path;
        string basis;
        if (comparison.Mode == ReviewMode.Turns)
        {
            if (sessionId is null || string.IsNullOrEmpty(comparison.From)) { throw Protocol(); }
            // Session endpoints derive location from the session. Validate ownership before reading its snapshots.
            WorkspaceSession session = await SessionAsync(sessionId, cancellationToken).ConfigureAwait(false);
            if (session.Id != sessionId || session.Directory != location.Directory || session.ProjectId != location.ProjectId) { throw Protocol(); }
            path = "/api/session/" + Escape(sessionId) + "/diff?from=" + Escape(comparison.From) +
                (comparison.To is null ? "" : "&to=" + Escape(comparison.To)) + "&context=3";
            basis = "Session " + sessionId + " · turn " + comparison.From +
                (comparison.To is null ? "" : " through " + comparison.To) +
                " · first recorded snapshot → last snapshot (working copy while active). Busy prompts share a turn; ranges across locations are unavailable.";
        }
        else
        {
            string info = await RequestAsync(HttpMethod.Get, "/api/vcs?" + query, null, 65536, root =>
            {
                ValidateReviewLocation(root, location.Directory);
                JsonElement data = root.GetProperty("data");
                return (Optional(data, "provider") ?? "Server VCS") + " · " + (Optional(data.GetProperty("branch"), "current") ?? "branch metadata unavailable");
            }, cancellationToken).ConfigureAwait(false);
            string? reference = comparison.Base;
            string baseLabel = "HEAD";
            if (comparison.Mode != ReviewMode.Working)
            {
                if (string.IsNullOrWhiteSpace(reference))
                {
                    (string? Ref, string Label) resolved = await RequestAsync(HttpMethod.Get, "/api/vcs/base?" + query, null, 65536, root =>
                    {
                        ValidateReviewLocation(root, location.Directory);
                        JsonElement data = root.GetProperty("data");
                        return data.ValueKind == JsonValueKind.Null ? ((string?)null, "No review base available")
                            : ((string?)Required(data, "ref"), Required(data, "name") + " [" + Required(data, "ref") + "] · " + Required(data, "source"));
                    }, cancellationToken).ConfigureAwait(false);
                    reference = resolved.Ref;
                    baseLabel = resolved.Label;
                    if (reference is null) { return new(info + " · " + baseLabel + ". Enter an explicit base.", [], false); }
                }
                else { baseLabel = reference; }
            }
            string mode = comparison.Mode switch { ReviewMode.Working => "working", ReviewMode.Branch => "branch", ReviewMode.Committed => "committed", _ => throw Protocol() };
            path = "/api/vcs/diff?" + query + "&mode=" + mode + "&context=3" +
                (comparison.Mode == ReviewMode.Working ? "" : "&base=" + Escape(reference!));
            basis = info + " · " + (comparison.Mode == ReviewMode.Working ? "HEAD → working copy"
                : "merge-base(" + baseLabel + ", HEAD) → " + (comparison.Mode == ReviewMode.Committed ? "HEAD" : "working copy")) +
                " · Repository changes may include work outside this session.";
        }
        return await RequestAsync(HttpMethod.Get, path, null, ReviewLimits.ResponseBytes, root =>
        {
            if (comparison.Mode != ReviewMode.Turns) { ValidateReviewLocation(root, location.Directory); }
            JsonElement data = root.GetProperty("data");
            List<ReviewFile> files = [];
            foreach (JsonElement value in data.EnumerateArray().Take(ReviewLimits.Files))
            {
                string status = Required(value, "status");
                if (status is not ("added" or "deleted" or "modified")) { throw Protocol(); }
                string patch = Required(value, "patch", allowEmpty: true);
                int length = Math.Min(patch.Length, ReviewLimits.PatchCharacters);
                if (length > 0 && char.IsHighSurrogate(patch[length - 1])) { length--; }
                int additions = value.GetProperty("additions").GetInt32();
                int deletions = value.GetProperty("deletions").GetInt32();
                if (additions < 0 || deletions < 0) { throw Protocol(); }
                files.Add(new(Required(value, "file"), status, additions, deletions, patch[..length], length < patch.Length));
            }
            return new ReviewResult(basis, files, data.GetArrayLength() > ReviewLimits.Files);
        }, cancellationToken).ConfigureAwait(false);
    }

    private static void ValidateReviewLocation(JsonElement root, string directory)
    {
        if (Required(root.GetProperty("location"), "directory") != directory) { throw Protocol(); }
    }
}
