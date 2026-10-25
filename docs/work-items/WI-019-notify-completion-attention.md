# WI-019 Notify on completion and required attention

## Details

- Type: Implementation
- Related requirements: [WIN-01, CON-04 and interaction reliability](../requirements/overview.md)
- Priority: P1
- Dependencies: [WI-014](WI-014-verify-native-reliability-accessibility.md), [WI-009](WI-009-implement-session-recovery.md), [WI-012](WI-012-persist-connections-workspace.md)
- Status: See the [active work item index](README.md)

## Goal and baseline

Tell users when observed work completes or needs a decision while they are elsewhere. Existing attention/state surfaces remain the authority; Settings > Notifications is currently unavailable.

## Scope

- Add persisted, separately controllable completion and attention notification preferences; default both on, subject to Windows permission/preferences. Limit initial notifications to the currently connected backend and observed sessions; app-closed monitoring is excluded.
- Notify on a newly confirmed successful completion or newly pending permission/form after initial hydration. Do not notify on initial historical hydration, known settled requests, or repeated snapshots; failed/interrupted task alerts are excluded from this increment.
- Suppress Windows notifications when the app is foreground and the owning session is visible; keep in-app state updated. Track identities/boundaries across reconnect so missed live events can yield one actionable notification without replaying historical completions.
- Activate a notification into its connection/project/session and pending request where applicable. Use normal validated navigation; require explicit connection switching if another backend is active. Stale/missing targets show guidance without submitting or replying to anything.
- Use minimal text with no prompt/tool contents, credentials or full paths. Handle Windows-disabled notifications and activation failures honestly, with no effect on task execution.

## Exclusions and deliverables

No background service, push delivery, tray lifecycle, sound customization or notifications for disconnected backends. Deliver Core notification policy/identity abstraction, Windows integration in App, preferences via existing persistence boundaries, activation routing and tests. Verify Windows APIs using the local winapp workflow.

## Acceptance criteria

- [ ] An observed completion or new pending request produces one eligible notification; foreground-owning-session suppression and independent preferences work.
- [ ] Initial hydration/restart does not emit historical notification floods; duplicate events, snapshot refresh and reconnect do not repeat already observed notifications.
- [ ] Notification activation reaches the validated owning workspace or explains unavailable targets; it never silently switches an active backend or answers a request.
- [ ] Preferences survive restart; Windows suppression/unavailability is explained without claiming delivery or disrupting execution.
- [ ] Notification payloads contain no task content, secrets or full paths; stale activations remain safe.
- [ ] Automated policy/persistence/routing checks, x64 build and actual packaged Windows notification/activation evidence pass.

## Verification requirements

Apply the [shared checks](README.md#shared-execution-and-verification-requirements). Use deterministic Core tests for boundaries, foreground state, preferences, duplicates, missed-event recovery and baseline hydration; test settings migration and activation ownership. Native checks use the packaged app with isolated fixture completion/forms, foreground/background states, notification activation with app open and closed, disabled Windows notifications and removed targets. Closed-app activation may restore the normal workspace but does not imply background monitoring. Record actual notification delivery and OS settings.

## Planning record

Defined on 2026-10-25. No implementation or native API verification has started. Documentation validation is recorded in the active index.
