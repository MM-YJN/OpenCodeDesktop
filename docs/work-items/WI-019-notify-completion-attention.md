# WI-019 Notify on completion and required attention

## Details

- Type: Implementation
- Related requirements: [WIN-01, CON-04 and interaction reliability](../requirements/overview.md)
- Priority: P1
- Dependencies: [WI-014](WI-014-verify-native-reliability-accessibility.md), [WI-009](WI-009-implement-session-recovery.md), [WI-012](WI-012-persist-connections-workspace.md)
- Status: See the [completed work item index](completed.md)

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

- [x] An observed completion or new pending request produces one eligible notification; foreground-owning-session suppression and independent preferences work.
- [x] Initial hydration/restart does not emit historical notification floods; duplicate events, snapshot refresh and reconnect do not repeat already observed notifications.
- [ ] Notification activation reaches the validated owning workspace or explains unavailable targets; it never silently switches an active backend or answers a request.
- [ ] Preferences survive restart; Windows suppression/unavailability is explained without claiming delivery or disrupting execution.
- [x] Notification payloads contain no task content, secrets or full paths; stale activations remain safe.
- [ ] Automated policy/persistence/routing checks, x64 build and actual packaged Windows notification/activation evidence pass.

## Verification requirements

Apply the [shared checks](README.md#shared-execution-and-verification-requirements). Use deterministic Core tests for boundaries, foreground state, preferences, duplicates, missed-event recovery and baseline hydration; test settings migration and activation ownership. Native checks use the packaged app with isolated fixture completion/forms, foreground/background states, notification activation with app open and closed, disabled Windows notifications and removed targets. Closed-app activation may restore the normal workspace but does not imply background monitoring. Record actual notification delivery and OS settings.

## Planning record

Defined on 2026-10-25. No implementation or native API verification has started. Documentation validation is recorded in the active index.

## Implementation and decisions (2026-10-26)

- Core owns `TaskNotificationPolicy`, immutable observation/candidate/destination models, preferences, and validated activation routing. Completion requires a newly confirmed successful idle boundary; failed, interrupted, unconfirmed, stale and loading observations cannot announce completion. Completion watermarks and request identity sets consume suppressed/disabled notifications too, so later foreground or preference changes do not replay them. Identities include connection ID, address, project, directory and session, and survive in-process reconnect through the window-owned policy.
- Initial session hydration establishes completion and request baselines without announcing history. Each directory's successful attention discovery establishes its request baseline; a newly discovered session after that baseline may announce a live pending request. Existing workflow retention and authoritative recovery supply missed observations. Restart deliberately establishes fresh observation baselines rather than treating persisted notification destinations as a session database.
- Both preferences default on and persist through the existing version-1 workspace store. Existing files without these optional properties migrate through default initializers. Invalid notification settings/destinations preserve the unreadable store under the existing explicit-reset policy.
- App uses the existing **Microsoft.WindowsAppSDK 2.5.1** reference: `Microsoft.Windows.AppNotifications.AppNotificationManager` and `Microsoft.Windows.AppNotifications.Builder.AppNotificationBuilder`. The manifest declares matching toast activation/COM server registrations. Registration, settings and submission failures stay independent of task execution. Settings reports OS suppression/unavailability, and never equates submission with visible delivery. No dependency change, background service, push delivery, or app-closed monitoring was added.
- Windows receives only fixed generic text and a random GUID activation token. The local workspace settings retain at most 128 destinations, expire them after seven days, and persist the destination before submission. Navigation validates original connection/address and listed project ownership, then uses ordinary server-validated project/session navigation. Disconnected/other-backend, missing/expired, removed-project and settled-request targets show guidance; activation never connects, silently switches, submits, or answers. Successful attention navigation focuses the matching request action; completion opens the session.

No new backend operation was introduced. The retained [2.1.4 task-workflow runtime contract](../integration/contracts/opencode-2.1.4-task-workflow.openapi.json), existing snapshot/idle confirmation rules and [sanitized semantics/evidence](../integration/contracts/README.md#task-workflow-contract-wi-007) remain the baseline. Real completion and question observations below used the hash-verified pinned binary and deterministic provider, without a version migration.

## Verification record

Suites ran sequentially. Final totals: **363 passed, 0 failed, 1 skipped**.

- `dotnet test --project tests/OpenCodeDesktop.Core.Tests/OpenCodeDesktop.Core.Tests.csproj`: **197 passed**. New cases cover historical/restart hydration, duplicate/older snapshots, stale/recovered missed boundaries, failed/interrupted outcomes, independent preferences, foreground suppression with consumed identities, new live attention, connection/address isolation, authoritative workflow completion, routing without server mutations, removed projects, stale requests, explicit backend switching, and preference capture/restoration.
- With `OPENCODE_CONNECTION_TEST_EXECUTABLE` and `OPENCODE_TASK_TEST_EXECUTABLE` pointing to the hash-checked isolated 2.1.4 binary, `dotnet test --project tests/OpenCodeDesktop.Infrastructure.Tests/OpenCodeDesktop.Infrastructure.Tests.csproj`: **136 passed, 1 unrelated installed-service discovery opt-in skipped**. Includes migration, independent preference/destination round trips and invalid-property preservation, plus existing real-server task/connection checks. Earlier default run: 131 passed, 4 skipped; skips were not counted as live evidence.
- `dotnet test --project tests/OpenCodeDesktop.Presentation.Tests/OpenCodeDesktop.Presentation.Tests.csproj`: **30 passed**, including Notifications availability while deferred sections remain unavailable.
- `dotnet build OpenCodeDesktop.slnx -p:Platform=x64`: passed, **0 errors**, 149 warnings on the final incremental build. Existing analyzer/style warnings and new style/class-coupling warnings remain. No analyzer/dependency package was added solely for verification.
- `winapp find-api members AppNotificationManager AppNotification AppNotificationActivatedEventArgs --project-dir ./src/OpenCodeDesktop --json`, builder/member and enum discovery confirmed the SDK surface. Metadata reported project scope, with a caveat that the installed Windows SDK fallback is 26100 rather than the targeted 19041; compilation does not verify the full minimum-OS matrix. `winapp find-ui ToggleSwitch --json` retained control samples. Manifest activation follows the [Microsoft Windows App SDK notification quickstart](https://learn.microsoft.com/windows/apps/develop/notifications/app-notifications/app-notifications-quickstart).
- `git diff --check`: passed after trimming an index trailing blank line. Build/launch-generated runtime lock changes were restored; package versions and lock files are unchanged.

### Packaged native evidence and limitations

Evidence stays outside Git at `%TEMP%/opencode/wi019/native`; test/build/launch logs are `%TEMP%/wi019-*.log`. `winapp run ./src/OpenCodeDesktop/OpenCodeDesktop.csproj --arch x64 --detach --json` built and launched the packaged app. Final-source launch: PID **30576**, AUMID `5b997942-1d29-4b4a-b547-ef4205349465_xqhx3qy4qq3eg!App`, local execution target. Earlier successful PIDs 51040, 42192 and 52916 exercised settings/submission/restart. One deployment attempt while an earlier instance was still open returned `UnauthorizedAccess_IODenied_NoPathName`; the subsequent closed-instance launch succeeded. This failed attempt is not passing evidence.

Original app settings were backed up byte-for-byte before installing a disposable fixture profile and DPAPI-protected fixture credential. The owned 2.1.4 server used isolated data/config/temp directories and `task-provider-fixture.mjs`. UIA confirmed fixture ownership and Windows notification status **Enabled**. With the app minimized, a real successful fixture completion generated one persisted completion destination and Windows submission returned a nonzero ID (the zero-ID/error path would have reported failure). With completion disabled and attention enabled through the actual Settings controls, a real pending question generated one attention destination. No prompt/tool contents, credential, directory, session title or other task content is included in the Windows payload.

Packaged restart retained completion **Off**, attention **On**, reopened the pending fixture session, and retained exactly two destinations without a historical notification flood (`restarted-pending.json`, `restarted-preferences.json`). UIA then independently toggled completion **On**, attention **Off**; another final-source packaged restart retained these values and the same two destinations (`final-source.json`, `final-restart-summary.json`). `evidence-summary.json` records counts/status without credentials. Dark-theme screenshots `settings.png` and `restarted-settings.png` were captured and visually inspected for labels, switches and honest status text.

**Visible Windows delivery and notification-click activation are unverified.** The session is locked: `winapp ui send-keys 'win+n' -w 1969206 --via send-input --allow-system-keys --json` returned `no_interactive_desktop`. UIA pattern operations and app rendering worked, but the shell showed no accessible notification to click. Nonzero submission IDs and saved destinations are not proof of displayed banners or activation. Open-app and closed-app notification clicks, actual foreground suppression, disabled Windows notifications, missing/settled targets through actual OS activation, full keyboard/focus/Narrator checks, light/high-contrast themes, and exact 1440×900 / 640×480 effective-size checks remain outstanding. Core tests establish policy and routing behavior, not these native checks. No live XAML attachment was performed.

Cleanup completed: all owned app/server/provider processes stopped, original settings restored byte-for-byte, generated encrypted fixture credentials retired outside app storage, and the validated temporary fixture root removed. `restoration-result.json` records settings restoration, server shutdown, fixture removal and final-app shutdown. No real service/task was mutated. At implementation handoff WI-019 was Awaiting Acceptance; no subsequent item had started.

## Completion acceptance (2026-10-26)

Marked Done by explicit user acceptance with the documented verification limitations. Moved the row to the completed index, updated the status link and set WI-020 as next without starting it. Unperformed native checks remain unverified and their acceptance criteria remain unchecked; acceptance does not establish that they passed. This documentation-only update verified unique index membership, local links and `git diff --check`; no application build, tests or native checks were rerun.
