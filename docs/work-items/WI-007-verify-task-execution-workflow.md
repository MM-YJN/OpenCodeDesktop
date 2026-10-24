# WI-007 Verify the complete task execution workflow

## Details

- Type: Investigation
- Related requirements: [SES-02, MSG-01, EXE-01, EXE-02, INT-01, INT-02, SYN-01, SYN-02](../requirements/overview.md#functional-requirements)
- Priority: P0 prerequisite
- Dependencies: [WI-006](WI-006-implement-live-projects-sessions.md)
- Status: See the [completed work item index](completed.md)

## Goal

Establish how users can submit a task, understand its progress, respond when the agent needs them, and know when the task has finished. Verify what users can trust after an interruption or connection failure before implementing those experiences.

## Scope

- Verify model/agent selection and prompt submission, distinguishing acceptance of a request from completion of the task.
- Establish observable queued, running, waiting, completed, interrupted, and failed behavior where supported by the pinned service.
- Verify how assistant text, code, tool activity, and failures reach the conversation and remain associated with the right session.
- Verify permission choices and their scope, agent forms and validation, and what users see when a request expires, is cancelled, or has already been answered.
- Verify interruption, including what can still arrive after the user asks to stop.
- Establish how to recover authoritative history, execution status, and outstanding requests after transport failure, including uncertain prompt delivery and events missed while disconnected.
- Document prerequisites for a reproducible task run. If model access requires credentials or cost, make those prerequisites explicit and keep ordinary automated checks independent of them.

## Exclusions

No production task UI, provider onboarding, attachments, terminal integration, or advanced queue management. This investigation defines recovery behavior; WI-009 implements automatic recovery.

## Deliverables

- A verified task-workflow contract under `docs/integration/`, with sanitized evidence and reproducible scenarios against OpenCode 2.1.4.
- A user-facing state and interaction description, including failure and recovery examples.
- Refined [WI-008](WI-008-implement-live-task-workflow.md) and [WI-009](WI-009-implement-session-recovery.md). If implementation needs smaller independently acceptable increments, define linked child items and update execution order before implementation starts.

## Acceptance criteria

- [x] Evidence distinguishes submitting a prompt, seeing progress, and reaching a terminal outcome.
- [x] Model/agent choice, message updates, and tool activity have a verified basis for implementation.
- [x] Permission scopes, supported form controls, validation, and stale-request behavior are established, with limitations explicit.
- [x] An interruption scenario establishes what the user can be told about stopping and the resulting task state.
- [x] A connection-loss scenario establishes how to recover history and pending attention without blindly resending the prompt.
- [x] Missing facts essential to implementation are resolved or recorded as blockers with an unblock condition; no unverified scenario is represented as supported.
- [x] The subsequent implementation items have acceptance criteria and verification prerequisites aligned with these findings.

## Verification

Use an isolated pinned service and disposable workspaces. Prefer controlled fixtures for repeatable failures; distinguish fixture observations from real-service evidence. Document any opt-in model access, clean up owned resources, and omit credentials and private content from evidence. Record exact commands, observed results, and limitations here. Documentation-only changes require link and formatting checks, not an App build.

## Outcome and decisions (2026-10-24)

Completed on 2026-10-24. Delivered the [task-workflow contract](../integration/opencode-task-workflow-contract.md), a hash-pinned [PowerShell verifier](../integration/verify-task-workflow.ps1), a deterministic [loopback provider](../integration/task-provider-fixture.mjs), a verifier-only [SSE consumer](../integration/task-events-fixture.cs), [runtime OpenAPI](../integration/contracts/opencode-2.1.4-task-workflow.openapi.json), and [sanitized evidence](../integration/contracts/opencode-2.1.4-task-workflow-evidence.json). WI-008 and WI-009 now define concrete state, transport, interaction, recovery and acceptance requirements. They remain unstarted; execution/streaming/attention stay one coherent WI-008 increment without additional work-item IDs.

The user explicitly selected this investigation while WI-006 remained Awaiting Acceptance. Its isolated API scenarios do not depend on native App acceptance, so investigation proceeded without changing or waiving WI-006's outstanding criteria. WI-008 must wait for that acceptance before implementation.

Material findings:

- Prompt 200 durably admits an inbox item; it does not signal completion. A stable message ID can be located in inbox or paged history after uncertainty. Repeated IDs, including changed text, return the original payload. Preserve immutable submitted text and do not retry mutations automatically.
- Active-session REST state is essential: previous outcome/idle metadata remains present during a new running task. Waiting attention overlays active execution. Assistant/tool completion is not task completion.
- The real service executed fixture-generated read and question tools, resumed after once permission/form replies, emitted incremental text/tool events, and persisted succeeded/failed/interrupted boundaries. Model/agent switches accept unknown IDs, so the desktop must validate catalog availability itself.
- Save-allow created a project-associated rule and allowed another same-project session. Once/reject/save decisions and stale permission 404 are recorded. Forms validate typed fields and external acknowledgement; conditional fields can be inactive, but hidden required fields still require a value. Settled replies return 409 and missing requests return 404. No timed expiry is promised.
- Stop uses explicit `resume=false`, preserves partial output and leaves queued input parked. Continue processing events until authoritative reconciliation confirms the result.
- The global stream is volatile and may omit location/durable sequence. Subscriber disconnect missed a real question; REST pending/detail reads rediscovered it and answering completed the task. A discarded prompt acknowledgement was reconciled by stable ID with no second POST. The experimental log yielded only a sync marker, so recovery uses REST hydration rather than assumed replay.

## Verification evidence

Commands ran from the repository root against CLI SHA-256 `B12B0613E9B4A491E5B12D90A5E9BBAE6D261FE9688C82BF73125A036963D399`. Scratch services/workspaces were disposable; outputs are in ignored `artifacts/wi007/` and sanitized retained contracts. The verifier kills both owned processes and removes its validated scratch root in `finally`.

| Command / check | Actual result |
| --- | --- |
| `pwsh -NoProfile -File docs/integration/verify-task-workflow.ps1 -ExecutablePath artifacts/wi006/runtime/package/bin/opencode.exe -OutputDirectory artifacts/wi007` | Passed: 110 named service checks, 91 captured native events, 25 runtime paths and 135 referenced schemas. |
| Same command with `-OutputDirectory artifacts/wi007/recheck` after retaining the snapshot | Passed: 110 checks, 91 events, 135 schemas; structural comparison to the committed runtime slice passed. |
| `node --check docs/integration/task-provider-fixture.mjs` and PowerShell `Parser.ParseFile` | Passed with no syntax errors. The SSE helper compiled and ran in both verifier runs. |
| Local Markdown targets/anchors, JSON parse/schema-reference closure, work-item index uniqueness, evidence sanitization and `git diff --check` | Passed: 65 local targets/anchors; 25 paths/135 schemas with no unresolved references; 110 cases/91 events parsed and sanitized; no duplicate index rows or whitespace errors. |

No App code changed; no App build or application test run is needed for this investigation. These opt-in service checks are not counted as Core/Infrastructure unit tests and do not require real provider access.

## Explicit limitations and handoff

Fixture provider output is not commercial-model acceptance. No wire-level dropped POST response, server restart, multi-process execution ownership, OS subprocess-stop guarantee, timed expiry, consumer-overflow load, cross-project/restart saved-rule boundary, or native UI acceptance was performed. Format/inequality and additional schema controls need controlled implementation coverage before support is claimed. These boundaries are documented rather than represented as verified behavior; conservative unsupported/uncertain states allow implementation without depending on them. There is no unresolved service-contract blocker for the scoped WI-008/WI-009 design. WI-006 native acceptance remains the execution gate for WI-008, and each implementation retains its own native/real-model/fault acceptance requirements.
