# WI-008 Run tasks and respond to agent requests

## Details

- Type: Implementation
- Related requirements: [SES-02, MSG-01, EXE-01, EXE-02, INT-01, INT-02, SYN-01](../requirements/overview.md#functional-requirements)
- Priority: P0
- Dependencies: [WI-007](WI-007-verify-task-execution-workflow.md), [WI-006](WI-006-implement-live-projects-sessions.md), both complete
- Status: See the [active work item index](README.md)

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

- [ ] The user can choose an available model and agent and submit a text request to the selected session.
- [ ] Sending and task progress remain visibly distinct; rejection or failure has an actionable explanation and does not silently discard unsent text.
- [ ] Assistant content, code, tool activity, and errors update without blocking navigation or typing, and stay associated with the correct session.
- [ ] Waiting for permission or an answer is distinguishable from running; outstanding requests remain discoverable after navigating to another session.
- [ ] Permission decisions show their scope; supported questions accept valid answers and explain invalid, expired, or already-resolved requests.
- [ ] Interrupting shows progress toward stopping and the resulting authoritative state, including failures to interrupt.
- [ ] Completion, interruption, and failure are distinguishable. Historical content and unknown message variants do not falsely imply active work or crash the workspace.
- [ ] Keyboard users can submit, interrupt, and respond to pending requests, with readable focus and status feedback.
- [ ] Connection loss or uncertain delivery never causes automatic duplicate prompt submission.

## Definition and verification

Before making this item Ready, incorporate WI-007 findings and assess whether separate child items are needed for execution, streaming, and user interaction. Use meaningful controlled tests for state transitions, session ownership, duplicate prevention, stale requests, and failures. Run relevant Core, Infrastructure, and Presentation tests and the x64 solution build. Manually verify a complete task against the pinned isolated service, including permission/form scenarios, interruption, and navigation during execution. Document model-access prerequisites, exact commands, outcomes, and any unavailable checks here.

## Executable contract from WI-007 (2026-10-24)

Follow the [verified task contract](../integration/opencode-task-workflow-contract.md) and its runtime snapshot/evidence. WI-007's real service uses a deterministic local provider; commercial-model and native UI behavior remain acceptance work for this increment. The user accepted WI-006 on 2026-10-24, satisfying this item's remaining dependency. No implementation has started here.

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

- [ ] An old failed/succeeded metadata outcome cannot override a new active task; standalone pending attention on an inactive session does not falsely imply execution.
- [ ] Stable input IDs, queued-versus-delivered history, duplicate/edited sends, lost acknowledgements and paged-history reconciliation are tested without mutation retries.
- [ ] Controlled transport tests cover split/coalesced SSE, comments, unknown/malformed/oversized frames, a failed stream, missing location and durable sequence, disposal/deadlines, and late events after Stop/backend replacement.
- [ ] Typed forms cover bounds, formats, options/custom input, conditional and hidden fields, explicit external acknowledgement, cancellation and stale/missing replies. Unsupported forms remain readable and cancellable.
- [ ] The opt-in pinned-service verifier passes; a native task run exercises code/tool rendering, permission once/reject/save, a question, interrupted partial output, navigation/draft preservation, explicit recovery and keyboard/screen-reader feedback. Record any real-model credential/cost prerequisite separately; fixture output must be identified as fixture output.
