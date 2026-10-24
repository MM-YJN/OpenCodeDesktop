# WI-008 Run tasks and respond to agent requests

## Details

- Type: Implementation
- Related requirements: [SES-02, MSG-01, EXE-01, EXE-02, INT-01, INT-02, SYN-01](../requirements/overview.md#functional-requirements)
- Priority: P0
- Dependencies: [WI-007](WI-007-verify-task-execution-workflow.md)
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
