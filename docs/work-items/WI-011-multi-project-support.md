# WI-011 Manage a project list for each connection

## Details

- Type: Implementation
- Related requirements: [PRJ-01, CON-01, SES-01](../requirements/overview.md#functional-requirements)
- Priority: P0
- Dependencies: [WI-006](WI-006-implement-live-projects-sessions.md), [WI-009](WI-009-implement-session-recovery.md)
- Status: See the [active work item index](README.md)

## Goal

A connected user can keep several projects in a visible list and switch between them without entering their directories again. Each connection owns its own project list and navigation context.

## Current baseline

`LiveWorkspace` already keeps opened directories, selected sessions and drafts in memory per backend; `WorkspaceView.Live.cs` renders directory buttons above the active project's sessions. Extend this foundation into an explicit project-list workflow rather than introducing a second workspace catalog. The server's project ID is not a unique workspace key: separate directories can share `global`.

## Scope and decisions

- Show the active connection's project list in the Projects panel, with readable project names, accessible full server paths, and an unambiguous active project. Keep the active connection visible.
- Provide Add project using the existing server-directory entry and validated open flow. A successful add selects the project; a failed add preserves the previous selection and does not add a broken entry. Adding an already listed canonical directory selects the existing entry.
- Use stable connection identity plus the server-returned directory for ownership and deduplication. Preserve server path semantics; do not resolve paths using the client's filesystem or merge entries solely by project ID or display name.
- Selecting a listed project reloads its authoritative sessions and restores its last selected session or new-session draft context. Preserve each project's unsent drafts during navigation for the app lifetime.
- Provide Remove from list. This only forgets the navigation entry; it does not delete server files, sessions or worktrees, interrupt execution, or implicitly answer pending requests. Preserve in-memory drafts for reopening during this app lifetime and keep pending-attention access available through the existing attention workflow.
- Removing the active entry leaves no active project and offers Add/select project; removing another entry retains the active workspace. Re-adding a removed directory uses the same validated opening path.
- Keep lists separate across connection switches and explicit reconnects within the app lifetime. Failed/cancelled project or connection switching retains the previous usable workspace. Profile removal clears its list; endpoint changes invalidate old ownership. Renaming a connection retains its list.
- Handle empty lists, unavailable directories, loading, retry and late results honestly. An unavailable listed path stays visible with an actionable error so the user can retry or remove it.

## Exclusions

Disk persistence and restart restoration (WI-012), concurrent active backend connections, project creation on disk, automatic server-project discovery, worktree management, custom project aliases, ordering controls, and persisted drafts. This item retains the existing single active project and connection model.

## Deliverables

Core-owned project-list/navigation behavior and models suitable for later persistence, App controls integrated with the current Projects panel, and meaningful tests. Reuse Infrastructure's verified location/session contract. Keep filesystem and transport operations outside Core and UI code.

## Acceptance criteria

- [ ] Add two valid directories under one connection; both remain listed and either can be selected without retyping a path.
- [ ] Each project restores its own selected session or new-session context and unsent draft; sessions and history belong to the active directory.
- [ ] Re-adding the same server-returned directory produces one entry. Two directories sharing `global` and paths with spaces/Unicode remain distinct and usable.
- [ ] Two connections maintain independent lists and selection. A connection rename preserves its list; endpoint replacement and profile removal cannot reuse stale workspace ownership.
- [ ] Failed add/switch and cancelled or obsolete loads preserve the prior usable selection and drafts. An unavailable listed directory can be retried or removed.
- [ ] Removing active and inactive entries follows the defined selection rules without server deletion, execution interruption, or loss of pending-attention access; re-adding restores retained in-memory drafts.
- [ ] Project names/paths and active selection are understandable; keyboard users can add, select and remove projects, with accessible loading/error feedback.
- [ ] Relevant automated tests pass with nonzero discovery, the x64 solution builds, and native project-list acceptance is recorded.

## Verification requirements

Use controlled Core tests for list ownership, canonical deduplication, navigation/drafts, removal, profile edits, unavailable paths and superseded operations. Cover presentation of multiple projects, selection, empty/error states and full paths. Extend Infrastructure tests only if request behavior changes.

```powershell
dotnet test --project tests/OpenCodeDesktop.Core.Tests/OpenCodeDesktop.Core.Tests.csproj
dotnet test --project tests/OpenCodeDesktop.Infrastructure.Tests/OpenCodeDesktop.Infrastructure.Tests.csproj
dotnet test --project tests/OpenCodeDesktop.Presentation.Tests/OpenCodeDesktop.Presentation.Tests.csproj
dotnet build OpenCodeDesktop.slnx -p:Platform=x64
```

Manually exercise two connections and multiple directories, per-project sessions/drafts, duplicate addition, missing paths, removal while work or attention remains, and keyboard navigation against a compatible isolated service. Record actual commands, results and remaining limitations here; leave unsupported criteria unchecked.

## Planning record (2026-10-24)

Definition only; implementation has not started. Existing code and WI-006/WI-009 establish the in-memory baseline. Persistent storage follows separately in WI-012. Documentation validation: `git diff --check` passed; local link-target and whitespace checks for the new work items passed. No application build or behavior tests were run for this documentation-only change.
