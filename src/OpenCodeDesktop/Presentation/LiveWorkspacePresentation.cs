using System;
using System.Collections.Generic;
using System.Linq;

using OpenCodeDesktop.Core.Workspaces;

namespace OpenCodeDesktop.Presentation;

/// <summary>Honest labels shared by the live view and UI-independent presentation tests.</summary>
internal sealed record LiveWorkspacePresentation(string Title, string Path, string ConversationNotice, string DraftOwner)
{
    internal const string EmptyProjects = "No projects listed. Add a server-side directory to get started.";
    internal static IReadOnlyList<ProjectListPresentation> ProjectList(LiveWorkspace workspace) => workspace.Projects.Select(project =>
    {
        string path = project.Location.Directory;
        string trimmed = path.TrimEnd('/', '\\');
        int separator = trimmed.LastIndexOfAny(['/', '\\']);
        string name = trimmed[(separator + 1)..];
        return new ProjectListPresentation(name.Length == 0 ? path : name, path, project.IsSelected, project.Error);
    }).ToArray();
    internal const string UnavailableDetails = "Changes and terminal are unavailable in this release. Open project Files from the project menu. Task activity and pending requests appear in the workspace.";

    internal static string SessionTitle(WorkspaceSession? session) =>
        session is null ? "New-session draft" : string.IsNullOrWhiteSpace(session.Title) ? "Untitled session" : session.Title;

    internal static LiveWorkspacePresentation From(LiveWorkspace workspace)
    {
        string notice = workspace.IsLoading && !workspace.HistoryLoaded ? "Loading… Your draft is retained."
            : !workspace.IsConnected ? (workspace.HasConnected ? "Disconnected. Reconnect explicitly in Settings; drafts remain in memory." : "Disconnected. Open Settings > Connections to connect to your OpenCode server.")
            : workspace.Selected is null ? (workspace.Location is null ? "Open a server-side directory to see its sessions." : "Choose a session, or send a task below to create one.")
            : !workspace.HistoryLoaded ? "History is unavailable. Retry loading or choose another session."
            : workspace.History.Count == 0 ? "This session has no recorded messages. You can keep an unsent draft below." : "";
        string path = workspace.Server is { } server
            ? server.Profile.Name + " · " + server.Profile.Address.Display + "\n" + (workspace.Location?.Directory ?? "No directory selected")
            : workspace.HasConnected ? "Disconnected · Reconnect in Settings to restore your workspace" : "Disconnected · No server connected";
        return new(workspace.Selected is null ? (workspace.Location is null ? "Server workspace" : "New-session draft") : SessionTitle(workspace.Selected), path, notice,
            (workspace.Server?.Profile.Id.ToString() ?? "") + "\n" + workspace.Location?.Directory + "\n" + workspace.SelectedSessionId);
    }
}

internal sealed record ProjectListPresentation(string Name, string Path, bool IsSelected, string? Error)
{
    internal string Label => (IsSelected ? "Selected project: " : "Project: ") + Name + "\n" + Path +
        (Error is null ? "" : "\n" + Error);
}
