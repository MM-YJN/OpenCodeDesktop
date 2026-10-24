# WI-007 Verify the complete task execution workflow

## Details

- Type: Investigation
- Related requirements: [SES-02, MSG-01, EXE-01, EXE-02, INT-01, INT-02, SYN-01, SYN-02](../requirements/overview.md#functional-requirements)
- Priority: P0 prerequisite
- Dependencies: [WI-006](WI-006-implement-live-projects-sessions.md)
- Status: See the [active work item index](README.md)

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

- [ ] Evidence distinguishes submitting a prompt, seeing progress, and reaching a terminal outcome.
- [ ] Model/agent choice, message updates, and tool activity have a verified basis for implementation.
- [ ] Permission scopes, supported form controls, validation, and stale-request behavior are established, with limitations explicit.
- [ ] An interruption scenario establishes what the user can be told about stopping and the resulting task state.
- [ ] A connection-loss scenario establishes how to recover history and pending attention without blindly resending the prompt.
- [ ] Missing facts essential to implementation are resolved or recorded as blockers with an unblock condition; no unverified scenario is represented as supported.
- [ ] The subsequent implementation items have acceptance criteria and verification prerequisites aligned with these findings.

## Verification

Use an isolated pinned service and disposable workspaces. Prefer controlled fixtures for repeatable failures; distinguish fixture observations from real-service evidence. Document any opt-in model access, clean up owned resources, and omit credentials and private content from evidence. Record exact commands, observed results, and limitations here. Documentation-only changes require link and formatting checks, not an App build.
