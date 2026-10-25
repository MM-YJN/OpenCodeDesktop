# WI-020 Inspect and manage queued inputs

## Details

- Type: Implementation
- Related requirements: [EXE-03, EXE-01 and SYN-02](../requirements/overview.md#functional-requirements)
- Priority: P1
- Dependencies: [WI-014](WI-014-verify-native-reliability-accessibility.md), [WI-008](WI-008-implement-live-task-workflow.md), [WI-009](WI-009-implement-session-recovery.md)
- Status: See the [active work item index](README.md)

## Goal and baseline

Expose the server's pending inputs and supported edits/cancellation. The current task snapshot retains only inbox IDs and the composer shows a count; sending remains gated while execution or uncertain delivery exists.

## Scope

- Verify pinned inbox list/update/cancel operations, editable fields, admission/delivery identities, attachment handling, stale-item errors and races with delivery. Retain the verified subset and evidence.
- Replace count-only inspection with queued item details in the central task workflow: readable text, delivery state and supported attachment summary without revealing internal metadata.
- Allow explicit editing of verified mutable fields and cancelling still-pending inputs. Retain edits on recoverable errors; reconcile missing/delivered/conflicting items before offering further action.
- Refresh authoritative inbox/history/execution after mutations and events. Distinguish cancellation of an inbox entry from interrupting an active task or removing delivered history.
- Preserve stable submission identity and uncertain-delivery protections. Unknown payload variants remain visible as unsupported with safe refresh; they are never overwritten by partial editors.

## Exclusions and deliverables

No new enqueue-while-running policy, reordering, priority, automatic resume, bulk cancellation or retry of uncertain mutations. Deliver typed inbox models, verified transport operations, Core state transitions, native queue UI and regressions. WI-017's richer attachments may be summarized when available but are not a prerequisite for text queue management.

## Acceptance criteria

- [ ] Server-created queued text inputs are inspectable with correct owning session, content and delivery status after live updates and reconnect.
- [ ] Editing a pending supported item changes the authoritative payload; cancellation removes that pending item without changing delivered history or interrupting work.
- [ ] Delivery during edit/cancel, stale IDs, validation errors and lost acknowledgements produce reconciled actionable state without duplicate submission or automatic mutation retry.
- [ ] Queue controls cannot target another backend/session through late responses; unsaved edits survive recoverable failure and navigation during the app lifetime.
- [ ] Unsupported payloads are not silently truncated or overwritten; existing text-send gating and uncertain-delivery safeguards remain intact.
- [ ] Contract probes, automated checks, x64 build and native queue interaction/accessibility evidence pass.

## Verification requirements

Apply the [shared checks](README.md#shared-execution-and-verification-requirements). Use the existing deterministic provider and parked-input fixtures to create durable queues without enabling a new composer policy. Probe update/cancel and authoritative postconditions against 2.1.4. Controlled tests cover payload mapping, races, lost responses, cancellation versus interruption, reconnect and session ownership. Native checks cover edit/save/cancel, validation focus, delivered-during-edit feedback and navigation with unsaved edits.

## Planning record

Defined on 2026-10-25. No implementation or runtime contract verification has started. Documentation validation is recorded in the active index.
