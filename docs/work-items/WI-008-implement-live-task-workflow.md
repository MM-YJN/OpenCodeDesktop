# WI-008 Run tasks and respond to agent requests

## Details

- Type: Implementation
- Related requirements: [SES-02, MSG-01, EXE-01, EXE-02, INT-01, INT-02, SYN-01](../requirements/overview.md#functional-requirements)
- Priority: P0
- Dependencies: [WI-007](WI-007-verify-task-execution-workflow.md), [WI-006](WI-006-implement-live-projects-sessions.md), both complete
- Status: See the [completed work item index](completed.md)

## Goal

Users can carry out a coding task from the desktop: choose a model and agent, send a request, follow the work, answer requests, and see the outcome or stop execution.

## Scope

- Show available models and agents and the current selection. Explain when no usable choice is available.
- Submit a text prompt in the selected real session and distinguish sending, accepted, running, and finished states according to WI-007.
- Render user and assistant messages, code blocks, tool activity, and actionable execution errors as updates arrive.
- Keep updates attached to their owning session while users navigate elsewhere. Preserve unsent text and prevent accidental duplicate submission.
- Surface pending permissions and agent questions both in the conversation and through the existing attention entry point. Show what each permission choice allows; support verified form validation and cancellation behavior.
- Let users interrupt active work and show the confirmed outcome rather than assuming the task stopped immediately.
- On connection failure or uncertain delivery, explain the uncertainty and prevent blind resubmission. Automatic recovery follows in WI-009.

## Exclusions

Attachments, provider sign-in, advanced input queue editing, file/diff review, interactive terminals, OS notifications, and automatic reconnection. Do not add illustrative responses to a real conversation when live execution is unavailable.

## Deliverables

The live task experience within the existing workspace, meaningful automated coverage, and an end-to-end verification record. Preserve module boundaries: Core owns workflow rules, Infrastructure owns server communication, and App owns interaction and rendering.

## Acceptance criteria

Accepted for completion by the user on 2026-10-24. This records product acceptance, not a claim that additional manual checks were performed.

- [x] The user can choose an available model and agent and submit a text request to the selected session.
- [x] Sending and task progress remain visibly distinct; rejection or failure has an actionable explanation and does not silently discard unsent text.
- [x] Assistant content, code, tool activity, and errors update without blocking navigation or typing, and stay associated with the correct session.
- [x] Waiting for permission or an answer is distinguishable from running; outstanding requests remain discoverable after navigating to another session.
- [x] Permission decisions show their scope; supported questions accept valid answers and explain invalid, expired, or already-resolved requests.
- [x] Interrupting shows progress toward stopping and the resulting authoritative state, including failures to interrupt.
- [x] Completion, interruption, and failure are distinguishable. Historical content and unknown message variants do not falsely imply active work or crash the workspace.
- [x] Keyboard users can submit, interrupt, and respond to pending requests, with readable focus and status feedback.
- [x] Connection loss or uncertain delivery never causes automatic duplicate prompt submission.

## Definition and verification

Before making this item Ready, incorporate WI-007 findings and assess whether separate child items are needed for execution, streaming, and user interaction. Use meaningful controlled tests for state transitions, session ownership, duplicate prevention, stale requests, and failures. Run relevant Core, Infrastructure, and Presentation tests and the x64 solution build. Manually verify a complete task against the pinned isolated service, including permission/form scenarios, interruption, and navigation during execution. Document model-access prerequisites, exact commands, outcomes, and any unavailable checks here.

## Executable contract from WI-007 (2026-10-24)

Follow the [verified task contract](../integration/opencode-task-workflow-contract.md) and its runtime snapshot/evidence. WI-007's real service uses a deterministic local provider; commercial-model and native UI behavior remain acceptance work for this increment. The user accepted WI-006 on 2026-10-24, satisfying this item's remaining dependency. Implementation began on 2026-10-24; the implementation and verification record follows below.

- Offer enabled active models and visible primary agents. Keep catalog settings/headers/credentials out of public state. Selection POSTs return 204 even for unknown IDs, so gate sending on current catalog/metadata and handle later provider rejection. Keep model/agent mutations and prompt submission under one per-session operation gate.
- Allocate a stable `msg_` ID before sending `{id,text,delivery:"steer"}`. Limit WI-008 to one foreground submission per inactive session: disable additional sends while active, pending submission, or unresolved delivery exists. Preserve editable drafts separately from the submitted immutable payload. Admission 200 means accepted, not completed; queued server work remains visible without queue editing. Repeated IDs retain the original text, so an edited draft needs a new ID only after the prior delivery is resolved.
- Determine running from `/api/session/active`, waiting from pending permissions/forms, queued from inbox, and terminal outcome from inactive state plus metadata/latest idle history. Old outcome/idle metadata persists during a new running turn. Do not treat assistant/tool completion as task completion or infer a task failure from connection loss.
- Consume global authenticated SSE in Infrastructure with frame parsing, cancellation, bounded frame/buffer sizes and resource cleanup. Handle comments, split/coalesced frames, missing location/durable sequence, unknown variants, stream failure events and disconnected streams. Core keys updates by backend generation/session/message/tool/ordinal; App owns code/text/tool/error rendering. Replace reconciled history by identity without duplicating prior deltas.
- Use pending-list/detail/reply APIs for permissions and forms. Permission labels show action/resources and proposed project save patterns; once/reject/saved-rule behavior and stale 404 follow WI-007. Waiting is an overlay on activity, and attention must remain discoverable for every opened directory/session.
- Render typed string/options/custom, number/integer, boolean, multiselect and external acknowledgement fields with declared constraints, defaults and `when` visibility. Hidden required fields remain required: preserve supplied values/defaults; explain unsupported forms with no valid value and offer cancel/refresh. Validate formats and unknown/unsafe controls with controlled fixtures before claiming support. Preserve answers on 400; on settled 409 or missing 404 refresh detail and disable stale actions. Do not invent timed expiry or auto-open external links.
- Interrupt with explicit `resume=false`, retain late events/partial text, and reconcile active/history/inbox/attention before showing a result. Idle `interrupted:false` is a no-op, and parked inputs may remain queued. Show interrupt failure with a usable retry/refresh action.
- On stream failure or lost mutation response, keep the draft/immutable submission record and mark live state stale. Provide explicit refresh using the WI-007 hydration algorithm, including inbox and paged-history ID lookup; never automatically retry prompts or decisions. Automatic scheduling belongs to WI-009.

Execution, streaming and attention remain one increment because its acceptance is a complete user task; they are internal steps, not separately acceptable product features. No child items are needed before starting.

### Additional acceptance and verification prerequisites

- [x] An old failed/succeeded metadata outcome cannot override a new active task; standalone pending attention on an inactive session does not falsely imply execution.
- [x] Stable input IDs, queued-versus-delivered history, duplicate/edited sends, lost acknowledgements and paged-history reconciliation are tested without mutation retries.
- [ ] Controlled transport tests cover split/coalesced SSE, comments, unknown/malformed/oversized frames, a failed stream, missing location and durable sequence, disposal/deadlines, and late events after Stop/backend replacement.
- [ ] Typed forms cover bounds, formats, options/custom input, conditional and hidden fields, explicit external acknowledgement, cancellation and stale/missing replies. Unsupported forms remain readable and cancellable.
- [ ] The opt-in pinned-service verifier passes; a native task run exercises code/tool rendering, permission once/reject/save, a question, interrupted partial output, navigation/draft preservation, explicit recovery and keyboard/screen-reader feedback. Record any real-model credential/cost prerequisite separately; fixture output must be identified as fixture output.

## Implementation and verification record (2026-10-24)

### Delivered

- Core owns a session-scoped live task workflow, safe catalog models, typed attention models, form validation, operation gates and immutable input records. Selection mutations and prompt submission share the session gate. Historical outcomes cannot override active work, including an event arriving during hydration.
- Infrastructure implements the pinned model/agent, prompt, active/inbox, permission, form, interrupt and global SSE APIs. Network I/O, JSON response mapping, prompt/answer serialization and SSE parsing run on the thread pool. The reader handles UTF-8 and split/coalesced CR/LF frames, comments, unknown variants, optional location/sequence, bounded frames and delivery buffering, read/header deadlines, cancellation and response disposal. Raw server error bodies and catalog credentials are not projected to the UI.
- App provides model/agent selection, Ctrl+Enter submission, visible loading/sending/stopping states, streaming text and fenced code, tool activity, safe execution errors, explicit refresh, project-scoped permission choices, typed question controls and a header attention menu spanning opened directories. External requests require an explicit acknowledgement and do not open links automatically.
- Editor and request controls survive background status refreshes. Updates are coalesced onto the UI dispatcher at low priority; unchanged navigation/history projections are retained. Stop remains available during background reconciliation. Session navigation does not cancel or retarget a task operation.
- An admitted, unchanged draft is consumed; edits made during sending and uncertain submissions are retained independently. Backend replacement retains a stale copy of session delivery/attention state and isolates late acknowledgements from the new connection generation. No prompt or decision mutation is retried automatically. Stream reconnection requires an explicit refresh/open action in this increment.
- Hydration rechecks metadata, active state, inbox, pending attention and history, invalidates reads touched by concurrent events, and searches older history pages for an unresolved stable input ID. Missing delivery evidence keeps the original submission unresolved. Interruption is confirmed through authoritative reads, retaining partial content and any parked inbox items.

### Automated evidence

Commands were run from the repository root on Windows x64. Local logs and generated verifier output are under ignored `artifacts/wi008*`; no credentials are committed.

| Command | Result |
| --- | --- |
| `dotnet build OpenCodeDesktop.slnx -p:Platform=x64` | Passed, no errors (final incremental build: zero warnings). |
| `dotnet test --project tests/OpenCodeDesktop.Core.Tests/OpenCodeDesktop.Core.Tests.csproj` | 86 passed, no failures or skips. |
| `dotnet test --project tests/OpenCodeDesktop.Infrastructure.Tests/OpenCodeDesktop.Infrastructure.Tests.csproj` with `$env:OPENCODE_TASK_TEST_EXECUTABLE=(Resolve-Path artifacts/wi006/runtime/package/bin/opencode.exe).Path` | 80 passed, 3 unrelated opt-in tests skipped, no failures. |
| `dotnet test --project tests/OpenCodeDesktop.Presentation.Tests/OpenCodeDesktop.Presentation.Tests.csproj` | 2 passed, no failures or skips. |
| `pwsh -NoProfile -File docs/integration/verify-task-workflow.ps1 -ExecutablePath artifacts/wi006/runtime/package/bin/opencode.exe -OutputDirectory artifacts/wi008` | Passed: 110 service checks, 91 events, 135 schemas. |
| `git diff --check` | Passed. |

New controlled coverage exercises active/terminal precedence, hydration invalidation, inactive attention, duplicate/edited sends, immutable payloads, draft preservation/consumption, lost acknowledgements and paged history, backend replacement and late acknowledgements, session-owned streaming and stream failure without automatic reconnection, scoped permission identity after refresh, invalid answers and cancellation, numeric/string/boolean/multiselect/external constraints, conditional/hidden fields, unknown controls, and presentation action/loading states. Transport coverage exercises SSE framing/bounds/disposal/deadlines, exact mutation contracts, safe catalog projection, typed form mapping, HTTP 400/401/404/409/500 without mutation retries and synchronous-handler isolation from the initiating thread.

`LiveTaskIntegrationTests.ProductionClientRunsFixtureTaskAnswersQuestionAndInterruptsPinnedService` is the new opt-in production-client check. It verifies the executable hash, starts an owned isolated service and deterministic Node provider, selects the fixture model/agent, subscribes through the production SSE parser, submits one stable input, verifies code output in history, answers a real agent question, and interrupts a held response. Owned processes and scratch storage are cleaned up. It uses no commercial model credential or paid inference. The existing PowerShell verifier separately exercises permission once/reject/save and server-side form validation; that is service-contract evidence, not native UI acceptance.

### Completion and accepted verification limits

Completed on 2026-10-24 at the user's explicit request to mark this work item Done. The user accepted the delivered implementation with the previously disclosed verification limits. No additional native UI, accessibility, commercial-model or failure/race checks were performed as part of this documentation-only closure.

The unchecked additional verification prerequisites above remain unchecked to preserve the evidence boundary; their remaining checks are waived for this completion. Native WinUI navigation/typing responsiveness, keyboard focus, screen-reader feedback and the complete native permission/question/interruption workflow are not claimed as manually verified. Automated build, controlled-test and pinned-service results remain as recorded above.

A commercial-model run requires an enabled server-side model/provider credential and may incur inference costs; the automated provider remains explicitly fixture output. WI-009 automatic recovery remains excluded and unstarted. The item file retains its original path and ID; only its index row moved to the completed index.
