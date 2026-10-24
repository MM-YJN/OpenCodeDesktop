# WI-006 Open real projects and manage sessions

## Details

- Type: Implementation
- Related requirements: [PRJ-01, SES-01, CON-01](../requirements/overview.md#functional-requirements)
- Priority: P0
- Dependencies: [WI-005](WI-005-verify-project-session-workflow.md), [WI-004](WI-004-implement-interactive-workspace-layout.md)
- Status: See the [completed work item index](completed.md)

## Goal

After connecting, users can open a real project on that server, create a session, and return to previous conversations. The workspace clearly identifies whose work is being shown and where it lives.

## Scope

- Let users select a server-side project directory using the workflow verified in WI-005. Keep the active server and directory visible.
- Show that project's sessions and let users create and select a session.
- Load conversation history and provide access to older messages where required. Show recorded messages and activity without implying that historical work is currently running.
- Provide understandable loading, empty, unavailable, and error states with an appropriate retry action.
- Preserve unsent drafts during session/project navigation and recoverable loading failures for the app lifetime.
- Keep each server's projects, sessions, and drafts separate. Switching or disconnecting must not leave old content labelled as belonging to the new server, and delayed loads must not replace the current selection.
- Use the authenticated connection lifetime established by WI-005. Open with an empty disconnected workspace; remove illustrative sample content and actions as requested during acceptance.

## Verified implementation contract

Follow the [WI-005 workflow contract](../integration/opencode-project-session-contract.md) and its retained runtime OpenAPI, rather than current website examples or the older frozen source. WI-005 resolved the essential contracts; no product decision or external prerequisite remains open for this increment.

- Use a server-directory text field and explicit Open action. Resolve through `/api/location` using encoded `location[directory]`, validate with `/api/fs/list`, then list sessions with the separate `directory` filter. Promote the candidate only after all three succeed. Location resolution accepts files, so validation cannot be omitted. No native folder picker or directory-tree browsing is required.
- Key workspace/drafts by backend identity plus server-returned directory, then session ID. Distinct empty roots can both have project ID `global`. Keep an in-memory list of opened directories per backend; do not treat `/api/project` as an exhaustive directory chooser.
- Create with `POST /api/session` and an explicit location, then verify the response ownership and refresh listing. Reopen metadata before history. Gate duplicate creates; reconcile an uncertain response through list refresh before another explicit creation attempt.
- Request descending session/message pages with an explicit limit of 50; follow opaque next cursors without `order`, preserving the directory filter for sessions. Provide Load more sessions and Load older messages. Stop on empty data or absent cursor, guard repeated cursors, and deduplicate IDs. Reverse descending history pages for chronological presentation. A nonempty final history page can still return a next cursor.
- Render supported historical content and activity with a safe unknown-variant fallback. No historical entry is evidence of live execution. Show unavailable placeholders in Details/terminal/attention surfaces rather than fictional sample data under a real project.
- Introduce an authenticated connection context behind Core interfaces, implemented by Infrastructure. Retain only the active credential in memory; atomic switch candidates are temporary. Dispose old credentials/loads on successful switching, disconnect, 401 and shutdown. Failed/cancelled switching retains the previous credential and workspace. Returning to an inactive backend requires password entry, then context revalidation. Public profile/state records and diagnostics contain no secrets.
- Save separate per-session and per-directory new-session drafts. Transfer a new-session draft only after confirmed creation. Preserve drafts on failures, disconnect and switching, and clear them at app exit. Editing/removing a profile invalidates its old workspace ownership.
- Guard results by connection generation and selection/load generation, including disconnect/reconnect to the same profile. Directory/session retries must not change unrelated selection. A later 401 invalidates the active auth context and requires explicit reconnect; preserve drafts and remembered navigation while hiding live content.
- Use cancellation, a 15-second total workspace deadline, and explicit body limits (1 MiB directory/session lists, 8 MiB history page). Explain size, protocol, timeout, HTTP and transport errors safely; never silently truncate or automatically retry mutations.

## Exclusions

Prompt submission, live execution updates, model/agent selection, permission decisions, agent answers, session search/rename/archive/fork, file browsing, and automatic reconnection. Profiles, credentials, and drafts are not persisted across restarts in this increment.

## Deliverables

A real project/session workspace integrated into the existing layout, meaningful automated coverage, and recorded manual verification. Keep user workflow rules in Core, server communication in Infrastructure, and presentation in App.

## Acceptance criteria

Remaining UI acceptance criteria were accepted by the user on 2026-10-24 through an explicit request to mark WI-006 Done. This acceptance does not assert additional agent-run manual checks.

- [x] A connected user can open a valid server-side directory and see its server and directory clearly.
- [x] An empty project explains how to create a session; creating one makes it available for selection.
- [x] Selecting an existing session displays its own history, including access to older messages when applicable.
- [x] Loading failures explain the problem and allow recovery without losing the user's draft or displaying another session's history.
- [x] Switching sessions and projects restores their respective unsent drafts for the app lifetime.
- [x] Successful backend switching displays only the new backend's workspace; failed/cancelled switching retains the previous workspace. Returning to a backend restores its in-memory context where still valid.
- [x] Disconnecting makes the disconnected state clear and prevents live workspace actions. Sample content never appears as real server data.
- [x] Keyboard navigation can reach project selection, sessions, history, and retry actions, with readable loading/error announcements.
- [x] Missing paths and ordinary-file paths fail project opening; an empty directory succeeds. A path with spaces/Unicode is encoded and shown correctly. Failed candidate opening retains the prior project and draft.
- [x] Two directories with project ID `global` have separate sessions/drafts; session metadata from a different directory is rejected before its history can be displayed.
- [x] Session and message pagination handles a final nonempty page with a next cursor followed by an empty page; repeated/invalid cursors and page failures preserve loaded data and offer recovery.
- [x] New-session draft transfer happens only on confirmed creation; duplicate create actions are gated and uncertain delivery offers reconciliation without automatic POST retry.
- [x] Authenticated calls continue after password UI clearing. Failed/cancelled switch preserves active authorization; successful switch, disconnect, later 401 and shutdown release it and invalidate late loads, including reconnect to the same backend.
- [x] No password/provider settings appear in profiles, state snapshots, errors or diagnostics; returning to an inactive backend requires explicit authentication and revalidates its remembered context.
- [x] Unknown history variants, oversized or malformed responses and historical execution entries produce honest readable states; real workspaces contain no sample files, messages, attention requests or enabled illustrative actions.
- [x] Relevant Core, Infrastructure and Presentation tests pass with nonzero discovery; the x64 solution builds and the isolated-service/manual scenarios below have recorded results and limitations.

## Definition and verification

Use controlled tests for ownership, draft preservation, loading failures, pagination and late responses. Infrastructure fixtures must cover exact query/body/auth construction, 400/401/404/5xx, redirects, malformed/unknown fields, deadlines, cancellation, size bounds and resource disposal. Core tests cover connection promotion/cleanup, ownership generations, draft transfer and uncertain creation. Presentation tests cover honest loading/empty/error/history states and exclusion of sample content from real sessions.

```powershell
dotnet test --project tests/OpenCodeDesktop.Core.Tests/OpenCodeDesktop.Core.Tests.csproj
dotnet test --project tests/OpenCodeDesktop.Infrastructure.Tests/OpenCodeDesktop.Infrastructure.Tests.csproj
dotnet test --project tests/OpenCodeDesktop.Presentation.Tests/OpenCodeDesktop.Presentation.Tests.csproj
dotnet build OpenCodeDesktop.slnx -p:Platform=x64
```

Manually exercise the acceptance scenarios against an isolated pinned service, including two backends, two directories sharing `global`, empty/new sessions, imported paged history, unavailable/file paths, keyboard access and loading/retry announcements. The WI-005 verifier supplies a disposable fixture pattern; its import endpoint is not a production feature. WI-005's 31 runtime cases establish the server contract only; implementation evidence follows.

## Implementation decisions (2026-10-24)

- Core owns `LiveWorkspace`, its connection/selection generations, project/session ownership, remembered directories, drafts, pagination, creation gating and explicit reconciliation. Server-returned directory plus backend profile identity keys ownership; project ID alone never does. Remembered sessions outside the first list page are reopened through metadata before history.
- `ServerConnections` promotes an Infrastructure-provided authenticated workspace context only after the identity probe succeeds. Failed/cancelled switches retain the active context. Successful switch, disconnect, authorization rejection and shutdown dispose it. Profile edits/removal invalidate remembered ownership. No public profile, result or presentation record contains a credential.
- Infrastructure implements the retained contract with per-request Basic authentication, encoded values, a 15-second request deadline linked to the Core operation's 15-second total budget, bounded body reads, safe failures and resource cleanup. Both connection probes and workspace transport/body parsing execute on the thread pool, including synchronous HTTP fixture completions. There are no blocking task waits in production UI paths.
- The existing workspace layout now shows explicit server-directory entry, opened directories, sessions, creation, list/history paging and retry actions. Loading displays an indeterminate progress indicator and readable status while the current draft editor remains usable. Live-region events announce loading/error text. Unsupported Details/terminal/attention/submission features are unavailable. Initial startup and disconnect both show an empty, honest disconnected surface; no illustrative workspace content is loaded.
- Presentation state has UI-independent tests. The presentation test project now references Core; its lock file records that project dependency without package-version changes. Native controls retain existing theme resources and selectable message text; historical activity is explicitly labelled recorded.

## Verification evidence (2026-10-24)

Commands were run from the repository root. Logs and the separately downloaded runtime are in ignored `artifacts/wi006/`; no credentials were written to those logs.

| Command | Actual result |
| --- | --- |
| `dotnet test --project tests/OpenCodeDesktop.Core.Tests/OpenCodeDesktop.Core.Tests.csproj` | Passed: 47 tests, zero failures/skips. Includes independent global-directory/session drafts, failed candidate retention, metadata ownership, initial/older-page errors, cursor loops/deduplication, uncertain creation, duplicate gates, atomic/cancelled backend switching, later 401, profile invalidation and stale responses after reconnecting to the same backend. |
| `$env:OPENCODE_CONNECTION_TEST_EXECUTABLE = (Resolve-Path 'artifacts/wi006/runtime/package/bin/opencode.exe').Path; dotnet test --project tests/OpenCodeDesktop.Infrastructure.Tests/OpenCodeDesktop.Infrastructure.Tests.csproj` | Passed: 52 tests, zero failures/skips, including both opt-in service tests. Exact requests/authentication, HTTP 400/401/404/5xx/redirects, malformed/unknown history, body deadlines/cancellation, transport errors, declared/streamed 1 MiB and 8 MiB limits, disposal and off-caller-thread execution are covered. |
| `dotnet test --project tests/OpenCodeDesktop.Presentation.Tests/OpenCodeDesktop.Presentation.Tests.csproj` | Passed after sample removal: 1 test, zero failures/skips. The live scenario covers initial empty/disconnected state, server ownership, loading, history failure/retry, empty history, disconnect and unavailable features. Five obsolete sample-only tests were removed with the feature. |
| `dotnet build OpenCodeDesktop.slnx -p:Platform=x64` | Passed: zero errors. Four test-only analyzer warnings remain: two CA2025 warnings on completed fixture-response tasks, and CA1849/xUnit1031 on the deliberate blocking wait used to prove that backend work progresses independently of its calling thread. No production blocking wait is involved. |
| `git diff --check` | Passed. |

The new opt-in `LiveWorkspaceIntegrationTests` uses the exact CLI SHA-256 `B12B0613E9B4A491E5B12D90A5E9BBAE6D261FE9688C82BF73125A036963D399`, two isolated ephemeral servers, generated disposable passwords, private config/data/cache/temp directories and disabled project config/model fetching. It exercises the production Core and Infrastructure implementations: an empty directory with spaces/Chinese characters; confirmed creation and draft transfer; verifier-only import of 55 messages; 50 + 5 + empty history pages in chronological order; separate empty Git roots with the same `global` ID; missing/file path rejection preserving the previous draft; reopening previous history/draft; rejected and successful backend switching; and disconnect. Servers and owned scratch directories are cleaned up with the existing validated cleanup pattern. It does not invoke a provider or send a prompt.

## Acceptance and follow-up

Acceptance follow-up (2026-10-24): the user requested removal of the initial sample workspace. Removed the sample models/fixtures, sample conversation/composer/terminal/attention actions, sample Details view and unused templates. The same live workspace now renders before the first connection, with an empty disconnected state and guidance to Settings > Connections. Updated product scope accordingly. Historical sample-only tests were removed with their feature; the live presentation scenario now verifies the initial empty state as well. Follow-up verification: the presentation test passed (1/1), `dotnet clean src/OpenCodeDesktop/OpenCodeDesktop.csproj -p:Platform=x64` followed by `dotnet build OpenCodeDesktop.slnx -p:Platform=x64` passed (zero errors/warnings), and `git diff --check` passed. Cleaning regenerated a stale XAML event-handler reference left by incremental compilation after sample removal. Native UI acceptance was still outstanding at that follow-up.

Acceptance follow-up: new server profiles now prefill `http://127.0.0.1:28894`, the Windows v2 service default verified in official v2 Web documentation and on this PC. Saving or connecting with a null, empty or whitespace-only address resolves to this same default in Core; explicit valid addresses retain their supplied port, invalid nonempty addresses remain rejected, and duplicate-origin checks apply after default resolution. The editor shows the resolved address after saving and explains the blank-field behavior. Removed obsolete sample-workspace wording from Connections. Verification: `dotnet test --project tests/OpenCodeDesktop.Core.Tests/OpenCodeDesktop.Core.Tests.csproj` passed 51/51 tests, including missing/blank inputs, duplicate defaults, explicit overrides and invalid-address retention; `dotnet build OpenCodeDesktop.slnx -p:Platform=x64` passed with zero errors and the four previously recorded test-only warnings. `git diff --check` passed. Native UI acceptance was still outstanding at that follow-up.
The user accepted WI-006 and explicitly requested completion on 2026-10-24, closing the remaining acceptance criteria. Native keyboard traversal, screen-reader announcements, visual states, draft focus continuity, password UI clearing and responsiveness were not independently exercised by the agent; user acceptance does not change that verification limitation. Existing automated and isolated-service evidence remains recorded above. This documentation-only completion update required no additional build or test run; `git diff --check` passed. WI-008 is now unblocked and remains unstarted.
