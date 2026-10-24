using System;

using OpenCodeDesktop.Core.Workspaces;

namespace OpenCodeDesktop.Presentation;

/// <summary>Honest labels shared by the live view and UI-independent presentation tests.</summary>
internal sealed record LiveWorkspacePresentation(string Title, string Path, string ConversationNotice, string DraftOwner)
{
    internal const string UnavailableDetails = "Details, files, changes, terminal and attention requests are unavailable in this increment. Conversation entries show recorded history only.";

    internal static LiveWorkspacePresentation From(LiveWorkspace workspace)
    {
        string notice = workspace.IsLoading && !workspace.HistoryLoaded ? "Loading… Your draft is retained."
            : !workspace.IsConnected ? (workspace.HasConnected ? "Disconnected. Reconnect explicitly in Settings; drafts remain in memory." : "Disconnected. Open Settings > Connections to connect to your OpenCode server.")
            : workspace.Selected is null ? (workspace.Location is null ? "Open a server-side directory to see its sessions." : "Choose a session or create one. Sending will be available in a later increment.")
            : !workspace.HistoryLoaded ? "History is unavailable. Retry loading or choose another session."
            : workspace.History.Count == 0 ? "This session has no recorded messages. You can keep an unsent draft below." : "";
        string path = workspace.Server is { } server
            ? server.Profile.Name + " · " + server.Profile.Address.Display + "\n" + (workspace.Location?.Directory ?? "No directory selected")
            : workspace.HasConnected ? "Disconnected · Reconnect in Settings to restore your workspace" : "Disconnected · No server connected";
        return new(workspace.Selected?.Title ?? (workspace.Location is null ? "Server workspace" : "New-session draft"), path, notice,
            (workspace.Server?.Profile.Id.ToString() ?? "") + "\n" + workspace.Location?.Directory + "\n" + workspace.SelectedSessionId);
    }
}
