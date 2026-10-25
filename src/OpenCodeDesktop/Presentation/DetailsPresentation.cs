using System;
using System.Collections.Generic;
using System.Linq;

using OpenCodeDesktop.Core.Workspaces;

namespace OpenCodeDesktop.Presentation;

internal sealed record DetailFieldPresentation(string Label, string Value);

internal sealed record DetailsPresentation(string Title, string Subtitle, IReadOnlyList<DetailFieldPresentation> Fields, string UnavailableMessage)
{
    internal const string Unavailable = "Changes and terminal output are not available in this release. Open project Files from the project menu. Conversation activity and pending requests stay in the workspace.";
}

internal static class DetailsPresentationFactory
{
    internal static DetailsPresentation From(LiveWorkspace workspace, SessionTaskState? state)
    {
        string title = workspace.Selected?.Title ?? (workspace.Location is null ? "No session selected" : "New-session draft");
        string subtitle = workspace.Server is { } server
            ? server.Profile.Name + " · OpenCode " + server.Version
            : workspace.HasConnected ? "Disconnected from the server" : "No server connected";
        List<DetailFieldPresentation> fields = [];
        if (workspace.Server is { } active)
        {
            fields.Add(new("Server", active.Profile.Name));
            fields.Add(new("Address", active.Profile.Address.Display));
            fields.Add(new("Version", "OpenCode " + active.Version));
        }

        fields.Add(new("Project", workspace.Location?.Directory ?? "No project selected"));
        fields.Add(new("Session", workspace.SelectedSessionId ?? "New-session draft"));
        fields.Add(new("Execution", state?.Status ?? "No execution state confirmed"));
        if (state?.Model is { } model)
        {
            fields.Add(new("Model", model.Name));
        }

        if (state?.Agent is { Length: > 0 } agent)
        {
            fields.Add(new("Agent", agent));
        }

        fields.Add(new("Messages", workspace.HistoryLoaded
            ? workspace.History.Count + (workspace.History.Count == 1 ? " recorded message" : " recorded messages")
            : "History is not loaded"));
        return new(title, subtitle, fields, DetailsPresentation.Unavailable);
    }
}
