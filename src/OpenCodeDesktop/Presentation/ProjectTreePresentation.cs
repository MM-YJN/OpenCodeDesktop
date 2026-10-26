using System;
using System.Collections.Generic;
using System.Linq;

using OpenCodeDesktop.Core.Workspaces;

namespace OpenCodeDesktop.Presentation;

internal enum ProjectTreeItemKind
{
    Project,
    Session,
    Draft,
    LoadMore,
}

/// <summary>One immutable tree node shared by the native view and presentation tests.</summary>
internal sealed record ProjectTreeNodePresentation(
    ProjectTreeItemKind Kind,
    string Key,
    string Directory,
    string? SessionId,
    string Label,
    string? Path,
    string? Detail,
    bool IsErrorDetail,
    bool IsActive,
    bool IsSelected,
    bool IsLoading,
    bool HasUnrealizedChildren,
    IReadOnlyList<ProjectTreeNodePresentation> Children)
{
    internal static ProjectTreeNodePresentation Project(WorkspaceProject project, string? selectedSessionId, bool active)
    {
        string directory = project.Location.Directory;
        List<ProjectTreeNodePresentation> children = [];
        if (active && selectedSessionId is null)
        {
            children.Add(Draft(directory, true));
        }

        foreach (WorkspaceSession session in project.Sessions)
        {
            children.Add(Session(directory, session, active && selectedSessionId == session.Id));
        }

        if (project.CanLoadMoreSessions)
        {
            children.Add(LoadMore(directory));
        }

        string? detail = project.Error;
        if (detail is null && project.SessionsLoading)
        {
            detail = "Loading sessions…";
        }
        else if (detail is null && project.SessionsError is not null)
        {
            detail = project.SessionsError;
        }
        else if (detail is null && project.SessionsLoaded && project.Sessions.Count == 0)
        {
            detail = project.Filter.Search.Length > 0 ? "No matching sessions." : project.Filter.Archived ? "No archived sessions." : "No sessions yet.";
        }

        if (project.Filter.Search.Length > 0 || project.Filter.Archived)
        {
            detail = (project.Filter.Archived ? "Archived history" : "Active history") +
                (project.Filter.Search.Length > 0 ? " · Search: " + project.Filter.Search : "") +
                (detail is null ? "" : " · " + detail);
        }
        bool isError = project.Error is not null || project.SessionsError is not null;
        return new(
            ProjectTreeItemKind.Project,
            ProjectKey(directory),
            directory,
            null,
            DisplayName(directory),
            directory,
            detail,
            isError,
            active,
            false,
            project.SessionsLoading,
            !project.SessionsLoaded || children.Count > 0,
            children);
    }

    internal static ProjectTreeNodePresentation Session(string directory, WorkspaceSession session, bool selected) => new(
        ProjectTreeItemKind.Session,
        SessionKey(directory, session.Id),
        directory,
        session.Id,
        LiveWorkspacePresentation.SessionTitle(session),
        null,
        null,
        false,
        false,
        selected,
        false,
        false,
        []);

    internal static ProjectTreeNodePresentation Draft(string directory, bool selected) => new(
        ProjectTreeItemKind.Draft,
        DraftKey(directory),
        directory,
        null,
        "New session draft",
        null,
        null,
        false,
        false,
        selected,
        false,
        false,
        []);

    internal static ProjectTreeNodePresentation LoadMore(string directory) => new(
        ProjectTreeItemKind.LoadMore,
        LoadMoreKey(directory),
        directory,
        null,
        "Load more sessions",
        null,
        null,
        false,
        false,
        false,
        false,
        false,
        []);

    internal static string ProjectKey(string directory) => "project:" + directory;
    internal static string SessionKey(string directory, string id) => "session:" + directory + ":" + id;
    internal static string DraftKey(string directory) => "draft:" + directory;
    internal static string LoadMoreKey(string directory) => "more:" + directory;

    internal static string DisplayName(string path)
    {
        string trimmed = path.TrimEnd('/', '\\');
        int separator = trimmed.LastIndexOfAny(['/', '\\']);
        string name = trimmed[(separator + 1)..];
        return name.Length == 0 ? path : name;
    }
}

internal sealed record ProjectTreePresentation(
    IReadOnlyList<ProjectTreeNodePresentation> Projects,
    string? SelectedKey)
{
    internal static ProjectTreePresentation From(LiveWorkspace workspace)
    {
        string? activeDirectory = workspace.Projects.FirstOrDefault(project => project.IsSelected)?.Location.Directory;
        string? selectedKey = activeDirectory is null
            ? null
            : workspace.SelectedSessionId is { } sessionId
                ? ProjectTreeNodePresentation.SessionKey(activeDirectory, sessionId)
                : ProjectTreeNodePresentation.DraftKey(activeDirectory);
        return new(
            workspace.Projects.Select(project => ProjectTreeNodePresentation.Project(
                project,
                project.IsSelected ? workspace.SelectedSessionId : null,
                project.IsSelected)).ToArray(),
            selectedKey);
    }
}
