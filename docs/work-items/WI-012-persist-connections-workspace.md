# WI-012 Persist connections and restore the last workspace on launch

## Details

- Type: Implementation
- Related requirements: [CON-01, CON-04, PRJ-01, SYN-02, SYN-03](../requirements/overview.md#functional-requirements)
- Priority: P0
- Dependencies: [WI-011](WI-011-multi-project-support.md), [WI-010](WI-010-detect-local-environment.md), [WI-009](WI-009-implement-session-recovery.md)
- Status: See the [completed work item index](completed.md)

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
- [x] With a compatible running server and valid saved credential, launching automatically connects to the last active backend and restores its active project and selected session without password entry or directory selection.
- [ ] Restored history, execution and pending requests are read from the server before being labelled current; no prompt or decision is automatically resent. A remembered session outside the first page restores correctly.
- [x] A saved new-session selection restores the project without creating a session. A first launch or launch after explicit disconnect respects the defined disconnected behavior.
- [ ] Missing paths/sessions, offline/incompatible servers and rejected or unavailable credentials show actionable states without losing saved lists, retry loops, unrelated fallback selection or startup crashes.
- [x] Add/edit/rename/remove and project-list changes survive restart; failed writes preserve the last valid settings and report failure. Profile removal cleans up its credential; endpoint edits cannot reuse old credentials or project ownership.
- [x] Settings contain credential references only; protected credentials can be retrieved by the intended user and safe diagnostics contain no passwords. Credential-protection failures offer explicit reconnect.
- [x] Missing/corrupt/unsupported settings, interrupted writes, concurrent save requests and shutdown during restoration have controlled coverage; saved data is not silently destroyed.
- [x] User navigation/disconnect during startup cancels obsolete work, including held responses that arrive after the new selection.
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


## Implementation decisions (2026-10-24)

- User confirmed packaged-app storage, DPAPI `CurrentUser`, and Remember credentials on this device enabled by default. App resolves `Microsoft.Windows.Storage.ApplicationData.GetDefault()`: version-1 JSON is stored under `LocalPath`; immutable protected credentials live under `LocalCachePath/Credentials` and are excluded from app-data backup. No unpackaged fallback or UI dependency was added to Core/Infrastructure. See [storage format, credential lifecycle and reset behavior](../workspace-storage.md).
- Core owns persisted navigation models, credential/settings abstractions, serialized snapshot orchestration and startup policy. Infrastructure owns bounded JSON validation, thread-pool file I/O, durable temporary-file replacement and DPAPI protection. App composes the implementations, exposes progress/Retry/reconnect/reset, and flushes queued writes before normal close.
- Settings save confirmed selections independently of shutdown. Failed session loads do not become the persisted selection. A new credential entry is written before settings reference it; old and superseded entries are deleted only after successful settings replacement. Failed writes preserve the previous settings/credential pair; cleanup failures remain actionable. An interrupted process can leave an unreferenced encrypted entry, documented rather than treated as an authentication candidate.
- Connection restoration uses the normal authentication/version checks and the existing directory/session/history/task hydration path. Session lookup is by ID, independent of list pagination. A classified HTTP 404 for the remembered session returns to its validated project's list while preserving the saved ID for Retry. Directory/project ownership changes are rejected. Nothing replays a prompt, creates a session, answers a request or starts a service during restoration.
- Explicit disconnect cancels a pending connection and clears the automatic target while preserving profiles/projects/credentials. Existing connection-switch tests were updated for this intentional behavior change. Project navigation/removal and shutdown cancel obsolete restoration, with identity/generation checks also protecting against dependencies that ignore cancellation. Removing the remembered directory during a held startup load clears its saved selection before a late response can arrive.
- Unreadable/unsupported settings make the store read-only until explicit reset. Reset archives the original JSON in the same directory and writes current in-memory settings; protected entries potentially needed by the archived document remain available. Early user navigation superseding initial settings loading preserves disk data and offers restart/reset instead of replacing it silently. Credential read/protection errors use fixed safe messages and never surface raw exceptions.

## Verification evidence (2026-10-24)

Commands ran from the repository root. Logs and disposable native fixtures are under ignored `artifacts/wi012*`; credentials and authentication headers are not recorded in logs.

| Command | Actual result |
| --- | --- |
| `dotnet test --project tests/OpenCodeDesktop.Core.Tests/OpenCodeDesktop.Core.Tests.csproj --verbosity quiet` and final `dotnet test --no-build --project tests/OpenCodeDesktop.Core.Tests/OpenCodeDesktop.Core.Tests.csproj --verbosity quiet` | Passed: **124 tests**, zero failures/skips. Nineteen added cases cover two saved connections and fresh-client restoration, per-project/new-session selections, off-page selected sessions, disconnect, opt-out, credential failures, unavailable/incompatible/rejected backends, missing selections/ownership, failed writes and credential replacement/deletion, rename/endpoint changes, reset, ordered pending saves, early navigation, shutdown, held responses and removal during startup. Extended the existing initial-history-recovery test to assert that its session selection becomes persistent only after authoritative recovery succeeds; existing task/recovery regression tests also pass. |
| `dotnet test --project tests/OpenCodeDesktop.Infrastructure.Tests/OpenCodeDesktop.Infrastructure.Tests.csproj --verbosity quiet` | Passed: **93 tests**, four opt-in skips. Ten added cases cover real JSON round trips and secret exclusion, malformed/null/unsupported formats, bounds/ownership/reference validation, concurrent saves, interrupted temporary files, failed/cancelled replacements, explicit reset preservation, and Windows current-user DPAPI round trip/tampering/deletion. |
| `$env:OPENCODE_CONNECTION_TEST_EXECUTABLE = (Resolve-Path 'artifacts/wi006/runtime/package/bin/opencode.exe').Path; $env:OPENCODE_TASK_TEST_EXECUTABLE = $env:OPENCODE_CONNECTION_TEST_EXECUTABLE; $env:OPENCODE_LOCAL_DISCOVERY_TEST = '1'; dotnet test --no-build --project tests/OpenCodeDesktop.Infrastructure.Tests/OpenCodeDesktop.Infrastructure.Tests.csproj --verbosity quiet` | Passed: **97 tests**, zero failures/skips. Includes pinned 2.1.4 isolated connection/workspace tests, read-only installed-service discovery, and the existing deterministic local-provider task/question/interrupt fixture. No external model/provider access was enabled. |
| `dotnet test --no-build --project tests/OpenCodeDesktop.Presentation.Tests/OpenCodeDesktop.Presentation.Tests.csproj --verbosity quiet` | Passed: **6 tests**, zero failures/skips. Two added cases distinguish restoration progress, disconnected onboarding, credential-required and unreadable-storage actions. Existing project/session/task presentation tests pass. |
| `dotnet build OpenCodeDesktop.slnx -p:Platform=x64 -p:GenerateAppxPackageOnBuild=true -p:AppxPackageSigningEnabled=false -v:quiet` | Passed: zero errors; produced the unsigned development MSIX. The full build reported 55 analyzer/tooling warnings (primarily test cancellation/style and existing warnings; missing `mspdbcmf.exe` prevents a symbols package). This is not a clean-warning claim. |
| `dotnet build OpenCodeDesktop.slnx -p:Platform=x64 -v:quiet` | Passed: final incremental build **zero errors, zero warnings**, after the remaining UI warning/enablement and confirmed-recovery-selection adjustments. Earlier compilation reported the existing `WorkspaceView` coupling warning; full-build warnings are recorded above. |
| `dotnet format style OpenCodeDesktop.slnx --no-restore --include <changed C# paths> --diagnostics IDE0011 IDE2001 IDE0055 IDE0007 --verbosity quiet` | Applied repository formatting only to the selected changed files. |
| `git diff --check` | Passed. |

### Native packaged verification

Used the Windows computer-use skill, the registered development package and an isolated pinned OpenCode 2.1.4 service. The registered AppX layout initially contained older binaries, so its initial observations were excluded. Generated the unsigned MSIX with the build command above and extracted it over the verified registered development layout while the app was closed. Activated it with:

```powershell
Start-Process -FilePath explorer.exe -ArgumentList 'shell:AppsFolder\5b997942-1d29-4b4a-b547-ef4205349465_xqhx3qy4qq3eg!App' -WindowStyle Hidden
```

`artifacts/wi012/native-fixture.ps1` launched a disposable server with isolated XDG/config/cache/state/temp directories, project config/model fetching disabled and a generated fixture password. It created two empty Git project directories and one server session, and seeded the packaged app's ordinary settings plus a DPAPI-protected credential. This fixture setup verifies restoration; it is not evidence of native password-entry/Add-local-environment interaction. The fixture seed refused to overwrite pre-existing settings.

Observed through the native app and inspected ordinary settings:

- Launch automatically connected to the isolated service and selected its existing session without password input or directory selection. The workspace showed both projects, authoritative empty server history and hydrated task status `Inactive — completion unconfirmed`, with no execution submitted.
- Selected the second project in the UI, observed its confirmed directory in JSON with a null session ID, closed with Alt+F4, and activated the package again. The app reconnected and restored that project's new-session context; no session was created.
- Selected the original project, closed and reopened again. Its existing session and both project entries were restored automatically. Credential replacement left one current protected entry rather than retaining each prior committed entry.
- Opened Connections and visually confirmed Remember credentials on this device checked by default. Explicitly disconnected, confirmed a null automatic target in JSON, closed/reopened, and observed disconnected onboarding without automatic reconnection.
- With the app closed, changed only the fixture's remembered session ID to a nonexistent ID. Launch connected, validated the project, retained both entries, returned to its session list and explained Retry/select alternatives rather than silently selecting another session.
- With the app closed, replaced the fixture credential reference with a nonexistent opaque reference. Launch remained disconnected and showed explicit credential/reconnect guidance without exposing an exception or crashing.
- Stopped only the verified isolated server process, restored its valid credential reference, then launched again. The app stayed responsive/disconnected and offered Retry/Connections for the refused endpoint. It did not start a server.
- Replaced only the fixture settings with malformed JSON while closed. Launch showed the preserved-data/reset notice; reading the file afterwards confirmed the malformed bytes had not been overwritten.
- Closed the fixture app, moved its settings and protected entries into ignored verification artifacts, and confirmed a clean packaged launch returned to disconnected onboarding. The isolated service was stopped. No existing user settings were replaced by the fixture.

### Verification limitations at implementation handoff

At the implementation handoff, acceptance remained pending. Controlled fresh-client tests establish two-connection isolation and the complete failure/cancellation matrix, but the native run used one backend with two projects. Native close/reopen with nonempty history plus an active task/pending permission or question, two-backend native navigation, a rejected password, a missing directory and delayed manual navigation remain unrecorded. The existing local-provider fixture passed as an integration test; it was not driven across a native restart. Exhaustive keyboard/accessibility and minimum-window layout review of the new notice/actions also remains manual. These limitations do not convert unchecked criteria into passes.

## User-accepted closure (2026-10-25)

The user explicitly requested that WI-012 be marked Done. Closed at that request with the documented verification limitations retained. Unperformed checks remain unchecked; this closure does not claim additional native verification or test results. The index row moved to the completed index, and WI-013 is the next active item; its execution has not started. Documentation-only closure validation: `git diff --check` and index/link consistency checks passed. No application build or tests were rerun.
