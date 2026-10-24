# WI-009 Recover connections and reopen existing work

## Details

- Type: Implementation
- Related requirements: [SYN-02, SYN-03](../requirements/overview.md#functional-requirements)
- Priority: P0
- Dependencies: [WI-008](WI-008-implement-live-task-workflow.md); recovery findings from [WI-007](WI-007-verify-task-execution-workflow.md)
- Status: See the [completed work item index](completed.md)

## Goal

A temporary connection failure or app restart should not leave users guessing what happened to their task. They can return to the server's current conversation, execution state, and outstanding requests without submitting the same work twice.

## Scope

- Show when live updates are interrupted and when recovery is in progress, separately from the task's last known execution state.
- Reconnect after recoverable transport failures and refresh authoritative history, execution status, and pending permissions/forms before presenting the workspace as current.
- Preserve unsent drafts during recovery within the app lifetime. Never retry a prompt or decision merely because its response was lost.
- Allow an explicit recovery action when automatic attempts cannot succeed, with an explanation when credentials or user action are required.
- After restarting the app, let users reconnect, select their server-side project, and reopen existing sessions with current history and pending requests.
- Stop recovery for a backend the user has disconnected from or replaced; old recovery results must not overwrite the current workspace.

## Exclusions

Automatic server startup, offline execution, a local conversation database, persisted passwords/profiles/drafts, automatic sign-in, and automatic restoration of the last selection. Restart recovery means access to server-side work after explicit reconnection, within the existing in-memory profile boundary.

## Deliverables

A recoverable live workspace and session-reopening experience, controlled failure coverage, and a recorded restart/disconnection acceptance scenario. Follow WI-007's verified recovery contract; keep recovery rules in Core, transport in Infrastructure, and feedback in App.

## Acceptance criteria

- [x] Losing the connection during a task shows that live state is unavailable without falsely reporting task failure or completion.
- [x] When connectivity returns, the conversation, execution status, and pending requests converge on the server's current state, including changes missed while disconnected.
- [x] The original prompt is not submitted again during recovery, including when delivery was uncertain.
- [x] Draft text survives recoverable failures and navigation within the app lifetime, without being sent automatically.
- [x] Requests resolved or expired during the outage no longer appear actionable after reconciliation; newly pending requests become discoverable.
- [x] Unsuccessful recovery explains the next action and remains responsive; a user can disconnect or switch backends without later recovery overwriting that choice.
- [ ] After closing and reopening the app, explicit reconnection and project/session selection restore access to server-side history, current execution state, and pending requests without requiring saved credentials.
- [x] Recovery does not duplicate messages or apply updates to the wrong session or backend.

## Definition and verification

Before making this item Ready, use WI-007 findings to define recoverable failure cases, retry limits, and the point at which refreshed state is considered current. Use controlled tests for lost responses, missed updates, repeated failures, session navigation, and cancellation of obsolete recovery. Run relevant Core, Infrastructure, and Presentation tests and the x64 solution build. Manually interrupt connectivity during an isolated live task, restore it, and confirm recovery without duplicate submission; also close/reopen the app and reconnect to existing work. Record exact commands, results, prerequisites, and limitations here.

## Executable recovery contract from WI-007 (2026-10-24)

Follow [WI-007's verified recovery rules](../integration/opencode-task-workflow-contract.md#stop-and-recovery), using the explicit hydration path implemented by WI-008. The prerequisite does not promise durable event replay: the experimental log yielded only a sync marker, while the global stream is explicitly volatile.

- Recover read/stream transport failures and timeouts with bounded delays of 1, 2, 4, 8 and 15 seconds, then stop automatic attempts and offer explicit Retry. Use a cancellation-aware scheduler abstraction for deterministic tests. Reset the budget only after successful hydration or explicit retry. Treat 401 as reconnect-required and incompatible version/protocol as requiring user action; do not loop on them.
- Subscribe before fetching and buffer/invalidate session changes during hydration. Refresh session ownership/metadata, active map, inbox, newest and necessary older history pages, and location pending permissions/forms for opened directories; fetch form detail when needed. A request resolved during the outage is removed/disabled and a new request is discoverable even if its session is not selected.
- Mark state current only after all required reads succeed and intervening buffered changes have been applied or their affected sessions re-fetched. The endpoints do not form an atomic snapshot. Deduplicate by identity and never append already-refreshed text deltas a second time. An incomplete refresh stays visibly stale, with previous observations labelled accordingly.
- Running comes from the active map, not old session outcome/idle metadata. Combine activity, pending attention, queued input and the latest terminal boundary. Inactive/no terminal outcome is not proof of task completion. Do not wait for idle before showing a recovered pending question/permission.
- Reconcile a lost prompt acknowledgement by its immutable message ID in both inbox and paged history. Finding the ID confirms admission; one absent page does not prove rejection. Preserve unresolved delivery and unsent draft independently. Never replay prompts, permission decisions, form answers, cancels or interrupts automatically.
- Scope recovery and draft updates to backend connection generation plus directory/session identity; disconnect, successful backend replacement, app shutdown and superseding recovery cancel obsolete operations. Failed backend switching retains the original context. Restart requires explicit profile/authentication/project/session selection, then the same hydration. No credential/profile/draft persistence is added.

### Additional acceptance and verification prerequisites

- [x] A controlled wire-level lost POST response, as well as the WI-007 client-discarded-acknowledgement fixture, preserves uncertain delivery and never issues a second POST during recovery.
- [x] Missed terminal events, a question created during disconnection, attention settled while disconnected, and old terminal metadata during active work converge through authoritative reads.
- [x] Hydration races, partial query failure, repeated disconnects, buffer bounds/overflow, superseded generations and cancellation are deterministic tests; no incomplete hydration is labelled current.
- [x] Backoff attempts occur at the specified limits, remain cancellable, and expose explicit retry or reconnect when exhausted or unauthorized.
- [ ] A native close/reopen acceptance scenario reconnects explicitly to retained server work; a server process restart and lost-response fault scenario are separately recorded rather than inferred from the subscriber-disconnect verifier. Real-model execution remains opt-in with explicit model-access/cost prerequisites.


## Implementation and verification record (2026-10-24)

### Delivered and decisions

- Core owns recovery independently of execution status. Read/stream failures invalidate observed state and retry after 1, 2, 4, 8 and 15 seconds through an injectable cancellation-aware scheduler. Exhaustion exposes explicit Retry recovery; explicit retry performs an immediate read and starts a fresh bounded budget. Authorization and incompatible protocol/size failures stop automatic attempts and explain reconnection or compatibility action. Neither prompts nor decisions are replayed.
- Reconnection subscribes before reading. Recovery discovers pending attention across all opened directories, validates session ownership, and refreshes catalog, metadata, active state, inbox, pending requests and history for all tracked/discovered sessions. Already resolved requests are replaced, including requests in unselected sessions. Opened directories remain registered across explicit reconnection within the app lifetime.
- Event delivery remains bounded to 256 queued frames with backpressure and a 4096-ID deduplication window. Queued events and per-session/directory revisions invalidate hydration; eight invalidation passes bound each attempt. Every session stays stale until the whole read set is reconciled. Stream overflow triggers authoritative recovery rather than attempting event replay. Refreshed history replaces matching streaming text even during active work.
- History pagination bridges retained history, looks for terminal boundaries when inactive, and searches for the immutable submission ID in inbox/history. Absence retains uncertainty. Inactive sessions without terminal evidence say completion is unconfirmed. A failed initial selected-session history read also joins recovery and restores its history/pagination when reconciliation succeeds.
- App shows a separate recovery notice, attempt/progress feedback and Retry recovery outside Settings, including when no session is selected. Stale task status identifies the last observation. Mutation/selection controls remain gated by freshness; recovery status changes use the existing accessible live-region pattern. Existing draft ownership remains backend/directory/session scoped and no persistence is added.
- Infrastructure maps broken sockets and stream overflow to recoverable transport errors and checks the pinned identity/version again before resubscribing, including when another server process takes over the same port. Backend replacement/disconnect disposes the old workflow; late reads cannot publish into its replacement. Failed backend switches continue to retain the original connection through the existing connection generation contract.

### Automated evidence

Commands run from the repository root on Windows x64; ignored logs are in `artifacts/wi009-*.log`.

| Command | Result |
| --- | --- |
| `dotnet build OpenCodeDesktop.slnx -p:Platform=x64` | Passed, zero errors; analyzer warnings remain in the build log. |
| `dotnet test --project tests/OpenCodeDesktop.Core.Tests/OpenCodeDesktop.Core.Tests.csproj` | 99 passed, zero failures/skips. |
| `$env:OPENCODE_TASK_TEST_EXECUTABLE=(Resolve-Path artifacts/wi006/runtime/package/bin/opencode.exe).Path; dotnet test --project tests/OpenCodeDesktop.Infrastructure.Tests/OpenCodeDesktop.Infrastructure.Tests.csproj` | 84 passed, zero failures, 3 unrelated opt-in checks skipped. |
| `dotnet test --project tests/OpenCodeDesktop.Presentation.Tests/OpenCodeDesktop.Presentation.Tests.csproj` | 3 passed, zero failures/skips. |
| `git diff --check` | Passed. |

Controlled tests cover exact delay budgets for reads and repeatedly failed subscriptions, exhaustion and explicit retry, cancellation during backoff, held obsolete responses, a partial read failure retaining global staleness, missed completion and new unselected-session questions, settled attention, unresolved delivery, a 300-delta burst during hydration, active work overriding old terminal metadata, deduplicated refreshed text, initial history failure and unsent drafts, fresh workspace reconnection, and retry/reconnect presentation controls. Existing navigation/draft and atomic backend-switch tests also pass. Healthy background reads retain interruption access, and a concurrent interrupt invalidates the held snapshot. A previously racy transport-isolation assertion now uses a dedicated caller thread: a pool thread can legitimately be reused by a later handler after an await, which is not evidence of running on the UI thread.

`WireLevelLostPromptResponseRetainsDeliveryAndRecoveryNeverPostsAgain` uses a real loopback TCP listener for the prompt request. It reads the immutable input, records admission, writes HTTP headers declaring a 256-byte response, writes only `{`, and closes the socket. Other endpoints are controlled HTTP fixtures. The production client retains uncertain delivery until history exposes that ID, then reconciles it; exactly one prompt POST was observed. This is a socket-level lost-response test, not a client-discarded acknowledgement or a claim about provider execution.

The opt-in production-client task integration test verifies the pinned executable SHA-256 and uses an owned isolated OpenCode 2.1.4 service plus `task-provider-fixture.mjs` (Node required). After its completion/question/interruption checks it starts the Core workflow, terminates and restarts the actual server process with the same port, credential and scratch storage, observes stale/recovering/current transitions, and verifies the original input appears once. A fresh connection/workspace then explicitly reconnects and selects that directory/session to recover server history. Owned server/provider processes and scratch files are cleaned up. This verifies server restart and fresh client state, separately from subscriber-disconnect fixtures; it does not launch or restart the native app. No paid provider or commercial model was used.

### Required native acceptance scenario and remaining limitations

The native disconnection and close/reopen scenarios below have **not been performed**. Their checkbox and the combined native acceptance prerequisite remain unchecked; automated results do not substitute for them.

Prerequisites: Windows with the WinUI tooling, the built x64 app, an isolated compatible 2.1.4 server and known directory, and a usable fixture model/agent. For deterministic tasks use the local Node provider/configuration demonstrated in `LiveTaskIntegrationTests`; any real-model alternative requires the user's server-side model access and may incur inference costs. Client-only transport interruption requires a controlled loopback fault/proxy or debugger fixture that leaves the server/provider running.

1. Launch the native app, explicitly connect in Settings > Connections, open the isolated directory and select a session. Enter an unsent draft, navigate to another session and back, and verify the draft remains.
2. Submit one held task (fixture: `WI007:stall`). Interrupt only the client's stream/read transport. Verify the recovery notice appears separately from the last Running observation, actions are gated, typing/navigation remain responsive, and the submitted prompt is not sent again.
3. Let the server finish or create a pending question while the client is cut off; settle an older pending request through a second client. Restore transport. Verify current conversation/execution state, removal of settled actions, discovery of new attention including an unselected session, retained unsent text, and one occurrence of the original input ID.
4. Keep transport unavailable through all five delays; verify Retry recovery and actionable guidance. Exercise retry, then disconnect or switch backends during a pending attempt and verify no later result replaces the selected backend/session. Check keyboard focus and recovery live-region feedback.
5. Close the native app while server work/history or attention remains available. Reopen it: verify the disconnected/empty starting state, explicitly recreate the in-memory profile and authenticate, then select the directory/session. Verify server history, current execution and pending requests return, with no saved password/profile/draft or duplicate prompt.
6. Separately repeat an owned server process restart on the same port/storage and the lost-response fault scenario if native end-to-end evidence is required. Their automated evidence above is independent of native interaction acceptance.

No durable replay, automatic server startup, offline execution, persistence, automatic sign-in or last-selection restoration was introduced. Native UI responsiveness/accessibility and app-process close/reopen remain acceptance work; real-model execution remains opt-in.


### Completion and accepted verification limits

Completed on 2026-10-24 at the user's explicit request to mark this work item Done, following disclosure that native disconnection and app close/reopen acceptance remained unverified. The user accepted completion with these verification limits; the remaining native acceptance checks are waived for this closure.

Unchecked criteria and the native scenario above remain unchecked to preserve the evidence boundary. No additional native UI, accessibility, app-process restart or real-model checks were performed for this documentation-only closure. Automated evidence remains as recorded above. The original work item path and ID are preserved; its row moved to the completed index, and no new work item was started.
