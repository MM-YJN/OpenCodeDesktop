# WI-009 Recover connections and reopen existing work

## Details

- Type: Implementation
- Related requirements: [SYN-02, SYN-03](../requirements/overview.md#functional-requirements)
- Priority: P0
- Dependencies: [WI-008](WI-008-implement-live-task-workflow.md); recovery findings from [WI-007](WI-007-verify-task-execution-workflow.md)
- Status: See the [active work item index](README.md)

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

- [ ] Losing the connection during a task shows that live state is unavailable without falsely reporting task failure or completion.
- [ ] When connectivity returns, the conversation, execution status, and pending requests converge on the server's current state, including changes missed while disconnected.
- [ ] The original prompt is not submitted again during recovery, including when delivery was uncertain.
- [ ] Draft text survives recoverable failures and navigation within the app lifetime, without being sent automatically.
- [ ] Requests resolved or expired during the outage no longer appear actionable after reconciliation; newly pending requests become discoverable.
- [ ] Unsuccessful recovery explains the next action and remains responsive; a user can disconnect or switch backends without later recovery overwriting that choice.
- [ ] After closing and reopening the app, explicit reconnection and project/session selection restore access to server-side history, current execution state, and pending requests without requiring saved credentials.
- [ ] Recovery does not duplicate messages or apply updates to the wrong session or backend.

## Definition and verification

Before making this item Ready, use WI-007 findings to define recoverable failure cases, retry limits, and the point at which refreshed state is considered current. Use controlled tests for lost responses, missed updates, repeated failures, session navigation, and cancellation of obsolete recovery. Run relevant Core, Infrastructure, and Presentation tests and the x64 solution build. Manually interrupt connectivity during an isolated live task, restore it, and confirm recovery without duplicate submission; also close/reopen the app and reconnect to existing work. Record exact commands, results, prerequisites, and limitations here.
