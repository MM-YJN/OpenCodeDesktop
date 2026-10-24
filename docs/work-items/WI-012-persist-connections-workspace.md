# WI-012 Persist connections and restore the last workspace on launch

## Details

- Type: Implementation
- Related requirements: [CON-01, CON-04, PRJ-01, SYN-02, SYN-03](../requirements/overview.md#functional-requirements)
- Priority: P0
- Dependencies: [WI-011](WI-011-multi-project-support.md), [WI-010](WI-010-detect-local-environment.md), [WI-009](WI-009-implement-session-recovery.md)
- Status: See the [active work item index](README.md)

## Goal

After closing and reopening the app, the user automatically reconnects to the last active connection and returns to the project and session they left. Every saved connection retains its own project list.

## Scope and decisions

- Persist connection IDs, names, validated addresses, and each connection's WI-011 project list. Store the last selected project and per-project session ID or new-session selection, plus the last active connection ID. Persist confirmed navigation changes without relying solely on a clean shutdown.
- Store credentials needed for automatic reconnection using Windows-protected storage for the current user. Keep only a credential reference in ordinary settings; never place plaintext secrets in configuration, logs, public state or error messages. Cover manual connections as well as connections added through local-service discovery.
- Define a versioned, bounded settings format in per-user app storage, with atomic replacement and serialized writes. Core owns persistence abstractions and restoration policy; Infrastructure owns serialization, file access and credential protection; App composes them and presents startup progress and recovery actions.
- On launch, load all saved profiles and their project lists, then automatically connect only the last active connection through the normal authentication/version checks. Do not require a password prompt when a valid saved credential is available. Keep the window responsive and show connection/restoration progress.
- After connection succeeds, validate the remembered directory and session ownership, then use the existing authoritative hydration/recovery path before showing history, execution and pending requests as current. Restore the saved session even if it is outside the first session-list page. Never replay prompts or interaction decisions.
- Interpret workspace continuity as the active connection, its project list, active project and selected session/new-session context. Restore other connections' lists and navigation when selected; only one backend is actively connected.
- If no saved state exists, show the existing empty disconnected onboarding. If the user explicitly disconnects, retain the profile and project list but clear the automatic-connect target so the next launch respects that choice. Reconnecting sets that target again.
- A missing/deleted project or session preserves saved entries and explains the failure with Retry/select alternatives. Do not silently select another project/session or submit work. A missing session returns to that project's session list after directory validation.
- An offline server uses the existing bounded recovery policy where applicable, then offers Retry or manual connection selection. Rejected/unavailable credentials or an incompatible server require actionable user intervention without repeated authentication attempts. Do not start the server automatically.
- Profile/project additions, removals and edits update storage consistently. Rename preserves identity; an endpoint change invalidates old credentials and workspace ownership. Removal deletes the profile's stored credential and navigation state. Successful credential replacement updates its protected entry. Explicit disconnect retains the protected credential for later explicit connection.
- Missing, malformed, unsupported-version or unreadable settings and credential-unprotection failures must not crash startup or overwrite recoverable data silently. Report persistence failures without exposing secrets, keep the current in-memory workspace usable, and offer an explicit reset/reconnect path. Preserve the last valid settings on failed writes.
- A manual connection/project selection, disconnect or shutdown cancels superseded startup work; late restore results cannot replace the user's choice or resurrect removed entries. Saved selections represent confirmed user navigation, not failed startup attempts.

## Exclusions

Persisting conversations, execution state, pending requests, unsent drafts, uncertain submissions, pane/scroll positions or terminal processes; cloud sync, import/export, multiple simultaneously active connections, remote/TLS support expansion, and automatic service startup. Server state remains authoritative. Existing in-memory drafts remain within their current lifetime boundary.

## Deliverables

A versioned per-connection settings model and storage implementation, Windows-protected credential lifecycle, startup orchestration integrated with WI-009 recovery, visible failure/retry paths, automated coverage, and a recorded native close/reopen scenario. Update obsolete in-memory-only UI wording and document the storage location, format version, credential mechanism and reset behavior without recording secrets.

## Acceptance criteria

- [ ] Save two connections with multiple projects and different selections, restart the app, and find both lists intact and isolated under their owning connection.
- [ ] With a compatible running server and valid saved credential, launching automatically connects to the last active backend and restores its active project and selected session without password entry or directory selection.
- [ ] Restored history, execution and pending requests are read from the server before being labelled current; no prompt or decision is automatically resent. A remembered session outside the first page restores correctly.
- [ ] A saved new-session selection restores the project without creating a session. A first launch or launch after explicit disconnect respects the defined disconnected behavior.
- [ ] Missing paths/sessions, offline/incompatible servers and rejected or unavailable credentials show actionable states without losing saved lists, retry loops, unrelated fallback selection or startup crashes.
- [ ] Add/edit/rename/remove and project-list changes survive restart; failed writes preserve the last valid settings and report failure. Profile removal cleans up its credential; endpoint edits cannot reuse old credentials or project ownership.
- [ ] Settings contain credential references only; protected credentials can be retrieved by the intended user and safe diagnostics contain no passwords. Credential-protection failures offer explicit reconnect.
- [ ] Missing/corrupt/unsupported settings, interrupted writes, concurrent save requests and shutdown during restoration have controlled coverage; saved data is not silently destroyed.
- [ ] User navigation/disconnect during startup cancels obsolete work, including held responses that arrive after the new selection.
- [ ] Relevant automated tests pass with nonzero discovery, the x64 solution builds, and native close/reopen and failure scenarios are recorded.

## Verification requirements

Core tests use fake stores, credentials, connections and scheduling for restoration ordering, ownership checks, cancellation, persistence triggers and no mutation replay. Infrastructure tests use temporary storage for round trips, schema validation, atomic/failed writes and cleanup; verify credential lifecycle through controlled abstractions and a Windows-only current-user protection round trip. Presentation tests cover restoring, disconnected, authentication-required, missing-selection and storage-error states.

```powershell
dotnet test --project tests/OpenCodeDesktop.Core.Tests/OpenCodeDesktop.Core.Tests.csproj
dotnet test --project tests/OpenCodeDesktop.Infrastructure.Tests/OpenCodeDesktop.Infrastructure.Tests.csproj
dotnet test --project tests/OpenCodeDesktop.Presentation.Tests/OpenCodeDesktop.Presentation.Tests.csproj
dotnet build OpenCodeDesktop.slnx -p:Platform=x64
```

Against an isolated compatible service, close/reopen the native app after selecting a session; verify automatic connection, project lists and authoritative workspace restoration. Repeat with a new-session selection, explicit disconnect, server unavailable, invalid credential, deleted directory/session, and manual navigation during delayed restoration. Inspect ordinary settings and diagnostics for secret exclusion. Keep active-task/pending-request scenarios opt-in using the existing local provider fixture and document prerequisites. Record exact commands, results and limitations here; automated fresh-client tests alone do not prove native app restart behavior.

## Planning record (2026-10-24)

Definition only; implementation has not started. Execute after WI-011. This deliberately extends the previous in-memory-only profile boundary and WI-009's explicit-reconnection restart behavior; it does not change historical acceptance records. Documentation validation: `git diff --check` passed; local link-target and whitespace checks for the new work items passed. No application build or behavior tests were run for this documentation-only change.
