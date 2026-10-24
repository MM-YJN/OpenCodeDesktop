# WI-011 Manage a project list for each connection

## Details

- Type: Implementation
- Related requirements: [PRJ-01, CON-01, SES-01](../requirements/overview.md#functional-requirements)
- Priority: P0
- Dependencies: [WI-006](WI-006-implement-live-projects-sessions.md), [WI-009](WI-009-implement-session-recovery.md)
- Status: See the [completed work item index](completed.md)

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

- [x] Add two valid directories under one connection; both remain listed and either can be selected without retyping a path.
- [x] Each project restores its own selected session or new-session context and unsent draft; sessions and history belong to the active directory.
- [x] Re-adding the same server-returned directory produces one entry. Two directories sharing `global` and paths with spaces/Unicode remain distinct and usable.
- [x] Two connections maintain independent lists and selection. A connection rename preserves its list; endpoint replacement and profile removal cannot reuse stale workspace ownership.
- [x] Failed add/switch and cancelled or obsolete loads preserve the prior usable selection and drafts. An unavailable listed directory can be retried or removed.
- [x] Removing active and inactive entries follows the defined selection rules without server deletion, execution interruption, or loss of pending-attention access; re-adding restores retained in-memory drafts.
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

## Implementation decisions (2026-10-24)

- Extended the existing Core `LiveWorkspace` catalog rather than adding a second catalog. Public project-list snapshots expose the server location, active selection and actionable availability error. Ownership uses profile ID and normalized server endpoint; changing a profile name preserves its catalog, while removing the profile or replacing its endpoint discards it. Directory identity remains the exact server-returned string, using ordinal comparison without client filesystem normalization.
- Project opening stages directory validation, authoritative sessions, remembered-session ownership and first-page history before promoting the selection. Failed, cancelled and superseded candidates retain the previous workspace and drafts. An obsolete authorization failure cannot disconnect a newer selection. Listed failed candidates remain visible with retry/remove guidance.
- Removal hides an entry and clears the active selection only when that entry was active. Its session selection, new/session drafts and task states remain in memory. No server mutation is sent. Retained directories stay registered for attention discovery, including after reconnecting with an empty list; attention navigation uses the same validated opening flow and re-adds the directory.
- Projects now displays a readable leaf name, full server path, selected-project label and per-entry Remove from list action. Full paths are available in the button's accessible name/text and tooltip; removal has a path-specific accessible name and explains retention in its tooltip. The existing loading indicator, live-region status/errors and retry control remain in use. Empty connected lists explain Add project.
- No Infrastructure request contract, packages, persistence or product scope changed.

## Verification evidence (2026-10-24)

Commands ran from the repository root:

| Command | Actual result |
| --- | --- |
| `dotnet test --project tests/OpenCodeDesktop.Core.Tests/OpenCodeDesktop.Core.Tests.csproj` | Passed: 105 tests, zero failures/skips. Six new tests cover canonical deduplication, distinct server paths (including spaces/Unicode and shared `global`), active/inactive removal and draft restoration, failed remembered-history switching, unavailable entries/retry, late success/authorization failure, rename/profile removal/backend isolation, and running-task/pending-attention retention plus new attention after reconnecting with an empty list. Existing tests cover endpoint replacement, failed/cancelled backend switching and reconnect generations. |
| `dotnet test --project tests/OpenCodeDesktop.Presentation.Tests/OpenCodeDesktop.Presentation.Tests.csproj` | Passed: 4 tests, zero failures/skips. New project-list scenario verifies names, full POSIX/Windows server paths, Unicode/spaces, active selection, unavailable-row retry text, empty lists and active-server identity. |
| `dotnet test --project tests/OpenCodeDesktop.Infrastructure.Tests/OpenCodeDesktop.Infrastructure.Tests.csproj` | Passed: 83 tests; 4 explicit opt-in skips, zero failures. |
| `$env:OPENCODE_CONNECTION_TEST_EXECUTABLE = (Resolve-Path 'artifacts/wi006/runtime/package/bin/opencode.exe').Path; dotnet test --project tests/OpenCodeDesktop.Infrastructure.Tests/OpenCodeDesktop.Infrastructure.Tests.csproj` | Passed: 85 tests; 2 explicit opt-in skips, zero failures. Existing pinned 2.1.4 isolated-server tests exercised real directory/session/history loading, separate directories sharing `global`, Unicode/spaces, failed file/missing paths and backend isolation with the updated Core implementation. Installed-service discovery and task/provider integration remained opt-in and were not enabled. |
| `dotnet build OpenCodeDesktop.slnx -p:Platform=x64` | Passed: zero errors. Compiler/analyzer warnings are described below. |
| `git diff --check` | Passed. |

The first parallel presentation build hit a shared Core compiler-output file lock (CS2012); its sequential retry and subsequent presentation run passed. A temporary IDE2002 whitespace warning was corrected. Builds also reported pre-existing test analyzer warnings (cancellation-token propagation, ConfigureAwait, deliberate fixture blocking waits, disposable fixture tasks and regex generation). Adding project controls triggered CA1506 on the existing large `WorkspaceView` partial class (99 coupled types, threshold 96); this is a maintainability warning, not a failed build.

## Native verification limits

The first six criteria above have controlled Core/presentation and existing isolated-service evidence. They do not establish native UI usability. Native visual layout, keyboard traversal, screen-reader loading/error announcements, editor focus continuity and the full two-connection project-list interaction scenario have not been exercised in this implementation turn. The final two criteria remain unchecked to preserve the verification record; the user accepted completion with these limits as recorded below.

Exercise two isolated compatible connections with multiple directories: add/select projects and duplicate aliases; edit each new/session draft; remove inactive and active entries; reopen from Add project and pending attention while execution remains active; rename a disconnected connection, reconnect, then replace its endpoint; make a listed directory unavailable and retry/remove it. Confirm full-path accessibility, clear selection, keyboard access and loading/error announcements. Disk persistence belongs to WI-012 and has not been started.

## Completion and accepted verification limits (2026-10-24)

The user explicitly requested marking WI-011 Done after disclosure that native UI, keyboard and accessibility checks remained outstanding. This request accepts completion with those verification limits and waives the remaining native acceptance checks for this closure. No additional native checks were performed, and unchecked criteria remain unchecked rather than being presented as verified.

Moved WI-011 to the completed index, preserved its filename and ID, updated its status link, and advanced the active index's next task to WI-012. WI-012 remains Ready and has not been started. This closure changes documentation only; no additional application build or behavior tests were required. Verification: `git diff --check` passed; WI-011 appears in exactly one index and its status link points to the completed index.
