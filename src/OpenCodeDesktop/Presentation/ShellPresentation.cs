using System;
using System.Collections.Generic;
using System.Linq;

using OpenCodeDesktop.Core.Connections;
using OpenCodeDesktop.Core.Workspaces;

namespace OpenCodeDesktop.Presentation;

internal enum ShellTone { Neutral, Accent, Success, Warning, Danger, Info }

internal sealed record ShellStatusPresentation(string Label, string Detail, string Glyph, ShellTone Tone)
{
    internal string AutomationName => Detail.Length == 0 ? Label : Label + ": " + Detail;
}

internal sealed record ConnectionCardPresentation(
    Guid Id,
    string Name,
    string Address,
    string StateLabel,
    string PrimaryActionLabel,
    bool IsActive,
    bool IsConnecting,
    bool CanRemove,
    string AutomationName);

internal sealed record AttentionTargetPresentation(string Directory, string SessionId, int Count, string Label);

internal sealed record SettingsSectionPresentation(string Title, string Description, bool IsAvailable);

internal sealed record ComposerPresentation(
    bool IsAvailable,
    bool CanSend,
    bool CanStop,
    bool IsBusy,
    string Hint,
    string? QueueLabel,
    string? DeliveryLabel,
    string? AttentionLabel);

internal static class ShellPresentation
{
    internal const string DisconnectedHint = "Open Connections to select a server.";

    internal static ShellStatusPresentation Connection(ActiveServer? active, ServerProfile? connecting, bool hasConnected)
    {
        if (active is null)
        {
            return connecting is { } pending
                ? new("Connecting", pending.Name + " · " + pending.Address.Display, "ShellIconClock", ShellTone.Info)
                : new("Disconnected", hasConnected ? DisconnectedHint : "No server connected.", "ShellIconServer", ShellTone.Neutral);
        }

        string detail = active.Profile.Name + " · " + active.Profile.Address.Display + " · OpenCode " + active.Version;
        if (connecting is { } target && target.Id != active.Profile.Id)
        {
            detail += " · Switching to " + target.Name + "…";
        }

        return new("Connected", detail, "ShellIconServer", connecting is null ? ShellTone.Success : ShellTone.Warning);
    }

    internal static ShellStatusPresentation Task(SessionTaskState? state)
    {
        if (state is null)
        {
            return new("No session selected", "Choose or create a session to run a task.", "ShellIconMessage", ShellTone.Neutral);
        }

        ShellTone tone = state.Stopping || state.Busy ? ShellTone.Info
            : state.Stale ? ShellTone.Warning
            : state.Permissions.Count + state.Forms.Count > 0 ? ShellTone.Warning
            : state.Active ? ShellTone.Accent
            : state.Loading ? ShellTone.Neutral
            : state.Submission is not null ? (state.DeliveryUncertain ? ShellTone.Danger : ShellTone.Info)
            : OutcomeTone(state.Outcome);
        string label = state.Stopping ? "Stopping"
            : state.Busy ? "Working"
            : state.Stale ? "Live state unavailable"
            : state.Permissions.Count + state.Forms.Count > 0 ? "Waiting for you"
            : state.Active ? "Running"
            : state.Loading ? "Checking state"
            : state.Submission is not null ? (state.DeliveryUncertain ? "Delivery uncertain" : "Queued")
            : OutcomeLabel(state.Outcome);
        return new(label, state.Status, "ShellIconPlay", tone);
    }

    private static ShellTone OutcomeTone(string outcome) =>
        Is(outcome, "Completed") || Is(outcome, "succeeded") ? ShellTone.Success
        : Is(outcome, "Failed") || Is(outcome, "Interrupted") || Is(outcome, "cancelled") ? ShellTone.Danger
        : ShellTone.Neutral;

    private static string OutcomeLabel(string outcome) =>
        Is(outcome, "Completed") ? "Completed"
        : Is(outcome, "succeeded") ? "Succeeded"
        : Is(outcome, "Failed") ? "Failed"
        : Is(outcome, "Interrupted") ? "Interrupted"
        : Is(outcome, "cancelled") ? "Cancelled"
        : outcome;

    private static bool Is(string value, string expected) => string.Equals(value, expected, StringComparison.OrdinalIgnoreCase);

    internal static IReadOnlyList<AttentionTargetPresentation> Attention(LiveWorkspace workspace)
    {
        IReadOnlyList<SessionTaskState> states = workspace.Tasks?.Sessions ?? [];
        return states
            .Select(state => new
            {
                State = state,
                Count = state.Permissions.Count + state.Forms.Count,
            })
            .Where(item => item.Count > 0)
            .Select(item =>
            {
                string title = workspace.Sessions.FirstOrDefault(session => session.Id == item.State.Id)?.Title ?? "Session";
                return new AttentionTargetPresentation(item.State.Directory, item.State.Id, item.Count,
                    title + " · " + item.Count + (item.Count == 1 ? " request" : " requests") + "\n" + item.State.Directory);
            })
            .ToArray();
    }

    internal static ConnectionCardPresentation ConnectionCard(ServerProfile profile, ActiveServer? active, Guid? connectingId, bool busy)
    {
        bool isActive = active?.Profile.Id == profile.Id;
        bool isConnecting = connectingId == profile.Id;
        string state = isActive ? "Active" : isConnecting ? "Connecting…" : "Not connected";
        string action = isActive ? "View" : "Connect";
        return new(profile.Id, profile.Name, profile.Address.Display, state, action, isActive, isConnecting, !busy && !isActive && !isConnecting,
            (isActive ? "Active server " : "Server ") + profile.Name + ", " + profile.Address.Display + ", " + state);
    }

    internal static ComposerPresentation Composer(LiveWorkspace workspace, SessionTaskState? state, string? draft)
    {
        var task = LiveTaskPresentation.From(state, draft, workspace.Tasks?.AttentionError, workspace.Tasks, workspace.CurrentDraft?.Files.Count ?? 0);
        bool available = workspace.Location is not null;
        bool composingDraft = available && workspace.SelectedSessionId is null;
        TaskCatalog? draftCatalog = composingDraft ? workspace.DraftCatalog : null;
        bool draftCatalogUsable = draftCatalog is { Models.Count: > 0, Agents.Count: > 0 };
        bool hasContent = !string.IsNullOrWhiteSpace(draft) || (workspace.CurrentDraft?.Files.Count ?? 0) > 0;
        bool canSendDraft = composingDraft && hasContent && workspace.CanCreate && draftCatalogUsable;
        string? queue = state is { Inbox.Count: > 0 } queued
            ? queued.Inbox.Count + (queued.Inbox.Count == 1 ? " queued input" : " queued inputs")
            : null;
        string? delivery = state is { DeliveryUncertain: true } ? "Delivery unconfirmed — reconcile before resending." : null;
        int requests = state?.Permissions.Count + state?.Forms.Count ?? 0;
        string? attention = requests > 0 ? "Waiting for you: " + requests + (requests == 1 ? " request" : " requests") : null;
        string hint = Hint(available, composingDraft, draftCatalog, draftCatalogUsable, state);
        return new(available, task.CanSend || canSendDraft, task.CanStop, task.IsBusy, hint, queue, delivery, attention);
    }

    private static string Hint(bool available, bool composingDraft, TaskCatalog? draftCatalog, bool draftCatalogUsable, SessionTaskState? state) =>
        !available ? "Open a project to compose a task."
        : composingDraft ? draftCatalog is null ? "Loading the model and agent catalog…"
            : !draftCatalogUsable ? "A usable model and agent are required."
            : "Ctrl+Enter to send. A session is created on the server when you send."
        : state is null ? "Create or select a session to run a task."
        : state.Loading ? "Checking live state…"
        : state.Busy ? "Working. You can interrupt when the server supports it."
        : state.Stale ? "Live state is unavailable. Recover before sending."
        : state.Catalog.Models.Count == 0 || state.Catalog.Agents.Count == 0 ? "A usable model and agent are required."
        : "Ctrl+Enter to send";

    internal static SettingsSectionPresentation Section(int index) => index switch
    {
        0 => new("Connections", "Choose a server for this workspace.", true),
        1 => new("Models & Providers", "Model and agent catalogs are read from the connected server in the composer. Provider sign-in and model management are not available in this release.", false),
        2 => new("Extensions", "MCP servers, plugins, commands and skills are not available in this release.", false),
        3 => new("Permissions", "Permission requests appear in the workspace when the server asks. Global permission policies are not available in this release.", false),
        4 => new("Appearance", "OpenCode Desktop follows the Windows system theme, including high contrast. Theme preference persistence is not available in this release.", false),
        5 => new("Notifications", "Choose task completion and attention notifications for observed sessions on the connected server.", true),
        _ => new("Settings", "Select a section.", true),
    };
}
