# WI-014 Resolve native reliability and accessibility verification gaps

## Details

- Type: Implementation / verification
- Related requirements: [Interaction and reliability requirements](../requirements/overview.md#interaction-and-reliability-requirements), existing P0 task workflow
- Priority: P0
- Dependencies: [WI-013](WI-013-polish-shell-feature-ux.md)
- Status: See the [completed work item index](completed.md)

## Goal and baseline

Establish reliable native interaction before extending the workspace. WI-013 was accepted with explicit limits: UIAutomation-driven tree traversal caused native XAML fail-fast crashes in the prior Windows session; Narrator, reduced motion, 100%/150% scaling and several native task actions were not fully verified. Its accepted completion remains unchanged; this item owns the follow-up evidence and any reproducible fixes.

## Scope

- Reproduce project/session tree UI Automation traversal after a clean interactive Windows session. Compare physical keyboard, UIA and Narrator behavior; collect crash diagnostics if a failure persists and fix reproducible application defects.
- Verify keyboard-only project add/select/remove, tree expansion, session selection, Settings return, pane changes and dialogs. Restore useful focus and preserve drafts/reading position.
- Exercise native send, streaming, interrupt, permission Allow once/Save allow rule/Reject, form reply/cancel, startup restoration and reconnect using an isolated pinned service and deterministic provider fixture.
- Review 100%, 150% and 200% display scaling, minimum and target effective window sizes, themes/high contrast, reduced motion, long titles/paths, multiline drafts and long/tool-heavy histories.

## Exclusions and deliverables

No new product features, visual redesign, server-version upgrade or broad analyzer cleanup. Deliver focused fixes if needed, meaningful regressions for changed behavior, and a native acceptance matrix with environment, exact reproduction steps, screenshots/UIA evidence and crash findings. Do not attribute an unreproduced crash to an environment cause without evidence.

## Acceptance criteria

- [x] In a clean session, repeated tree traversal using physical keyboard, UIA and Narrator completes without a crash; any original failure has a documented reproduction and resolution or evidence explaining why it cannot be reproduced.
- [x] Keyboard users can reach and operate project/session controls, removal, Settings, panes, composer and requests, with meaningful names, visible focus and useful focus return.
- [x] Native task submission, streaming, interruption, all supported permission decisions and form reply/cancel are observed against the controlled service, with correct resulting server state and no duplicate submission.
- [x] Startup restoration and connection-loss recovery preserve ownership, drafts and attention access; uncertain delivery is not resent automatically.
- [x] The size/scale/theme/long-content matrix is recorded with no inaccessible essential actions; reduced-motion behavior and Narrator announcements are observed.
- [x] Required automated checks and x64 build pass, and native evidence supports each completed criterion. Unavailable checks remain explicit acceptance gaps.

## Verification requirements

Follow the [shared checks](README.md#shared-execution-and-verification-requirements) and the linked winapp workflow. Record Windows/WinUI/tool versions, package/output identity, display settings, fixture prerequisites and crash dump findings. Obtain a clean session through the user's normal session lifecycle when needed; do not automatically log off/restart the workstation. If it is unavailable, record the blocked check and unblock condition. No real provider subscription is required. Repeat only checks affected by a fix and the final critical workflow smoke scenario.

## Planning record

Defined on 2026-10-25. At definition, implementation and native verification had not started. Documentation validation is recorded in the active index.

## Implementation and verification (2026-10-25)

Execution started after confirming WI-013 is Done in the completed index. Product scope and the pinned backend contract remain unchanged. The user confirmed this is still the original Windows session and requested continuing in it. The clean-session criterion therefore remains pending; no logoff, restart, display-scale or contrast-theme change was performed.

### Focused fixes

- Assign accessible names when project/session row content is loaded, including after container realization/virtualization, and update names when row models change. Previously only selection assigned the container name: baseline UIA exposed 38 of 39 realized rows as `Microsoft.UI.Xaml.Controls.TreeViewNode`. The rebuilt app exposed zero such names across the same 39 rows.
- Give the shell's New session draft button an explicit accessible name; its child StackPanel did not provide a button name in UIA.
- Name agent answer controls from their visible field labels, retain required-field wording, add description/constraint help text, and include field context in multiselect option names. Register actual input controls for busy/stale disabling rather than checking the enclosing StackPanel. Native inspection confirmed the Fixture edit is named/enabled while connected and disabled together with Submit answer after service loss.
- Re-read the reduced-motion preference before each Details pane opening/closing. Preserve the sanitized pane transitions so enabling animations again can restore them without reapplying the template. This also avoids requiring the Windows 10 2004-only `AnimationsEnabledChanged` event on the app's older minimum OS. Native motion timing/toggle acceptance remains unverified.
- Add [the native tree naming regression check](../integration/verify-native-tree.ps1). It checks every realized tree row over repeated read-only UIA snapshots, fails for empty trees or unnamed/default-type rows, and retains snapshots outside Git. It does not claim to test Narrator or physical keyboard input.

### Environment and reproducibility

- Windows 11 Pro 10.0.26300; boot/logon reported as 2026-10-19 22:22. WinApp CLI 0.7.1; .NET SDK 10.0.402; Microsoft.WindowsAppSDK 2.5.1; app target `net10.0-windows10.0.19041.0`, minimum 17763. API discovery used the restored App project and reported Windows SDK fallback to 26100, which does not establish minimum-OS availability. The existing SplitView pane events were verified in project metadata; no new OS event dependency was introduced. Microsoft documents the newer animation event's introduction at [Windows 10 2004](https://learn.microsoft.com/en-us/uwp/api/windows.ui.viewmanagement.uisettings.animationsenabledchanged).
- Local interactive target, 192 DPI / 200% scaling, dark theme. Captures are 2884x1860 physical pixels including window frame; other scales/sizes/themes were not independently reaccepted in this pass.
- Package `5b997942-1d29-4b4a-b547-ef4205349465_1.0.0.0_x64__xqhx3qy4qq3eg`, AUMID `5b997942-1d29-4b4a-b547-ef4205349465_xqhx3qy4qq3eg!App`. Final launch PID 49012; local HWND 5114388. Current binary: `src/OpenCodeDesktop/bin/Debug/net10.0-windows10.0.19041.0/win-x64/AppX/OpenCodeDesktop.exe`, built by the building launch. Earlier native actions used the preceding build at PID 37624 / HWND 11930122; the final build additionally names NewTaskButton explicitly.
- Disposable OpenCode 2.1.4 at `artifacts/wi006/runtime/package/bin/opencode.exe`, SHA-256 `B12B0613E9B4A491E5B12D90A5E9BBAE6D261FE9688C82BF73125A036963D399`, with isolated XDG/config/data/workspace directories and the existing `task-provider-fixture.mjs` Node provider. No real provider subscription or installed-service mutation was used.
- Evidence root: `%TEMP%\opencode\wi014\`. Fixture setup/metadata and the encrypted credential/settings backups remain private there. Original package settings were restored byte-for-byte, original credential files restored, test-created credential files removed, and owned app/provider/server processes stopped. Incidental RID-only lock-file changes from `winapp run` were reverted; dependencies were unchanged.

### Commands and results

Tests were run sequentially. Final logs are under the evidence root.

| Command | Result / evidence |
| --- | --- |
| `dotnet test --project tests/OpenCodeDesktop.Core.Tests/OpenCodeDesktop.Core.Tests.csproj` | 129 passed, zero failed/skipped; `core-final.log`. |
| `dotnet test --project tests/OpenCodeDesktop.Infrastructure.Tests/OpenCodeDesktop.Infrastructure.Tests.csproj` | With `OPENCODE_CONNECTION_TEST_EXECUTABLE` and `OPENCODE_TASK_TEST_EXECUTABLE` set to the pinned binary: 96 passed, zero failed, one skipped installed-service discovery test; `infrastructure-final.log`. Initial run without opt-in: 93 passed / four skipped. |
| `dotnet test --project tests/OpenCodeDesktop.Presentation.Tests/OpenCodeDesktop.Presentation.Tests.csproj` | 17 passed, zero failed/skipped; `presentation-final.log`. |
| `dotnet build OpenCodeDesktop.slnx -p:Platform=x64` | Zero errors, 33 existing warnings; `build-final.log`. No WinUI analyzer package was added, so its additional checks did not run. |
| `winapp run ./src/OpenCodeDesktop/OpenCodeDesktop.csproj --arch x64 --detach --json` | Final current packaged binary launched as PID 49012; `launch-final.log`. One earlier deployment attempt while the old instance was closing returned `UnauthorizedAccess_IODenied_NoPathName`; after closing the owned instance, a no-build retry of the just-built exact output succeeded. |
| `docs/integration/verify-native-tree.ps1 -AppProcessId 49012 -OutputDirectory "$env:TEMP/opencode/wi014/tree-final-binary"` | Three snapshots, three realized fixture rows per snapshot, all meaningfully named. Earlier full user-workspace comparison: 39 realized rows, zero framework names after the fix; `baseline-uia.json`, `fixed-uia.json`. |
| `git diff --check` | Passed after final edits. |

### Native acceptance matrix

| Area | Observed evidence | Remaining acceptance gap |
| --- | --- | --- |
| Tree naming/traversal | `baseline-uia.json` vs `fixed-uia.json`; repeated final naming snapshots. UIA focus on the project followed by injected Left/Right/Up/Down/Enter, with 250 ms between individual keys, navigated project/session/paging rows without a crash. Focus at each step is in `traversal.json`; `tree-after-traversal` checks passed. Tab from a project reached its inline New session draft and More actions buttons. | Same-session evidence only. Physical keyboard, Narrator, repeated clean-session traversal and original-crash resolution remain pending. No new dump was produced; non-reproduction here does not identify a cause or resolve the previous fail-fast. |
| Task/form/permission actions | Native Submit answer with Alpha and Cancel question each cleared the server form list. Native Send submitted `WI007:stall`; native Interrupt produced authoritative `outcome=interrupted`. Allow once/Reject each cleared a controlled pending permission. Save project allow rule cleared the request and a subsequent matching evaluation returned `effect=allow`. `native-actions.json`, `interrupt-state.json`, `saved-allow-state.json`, `cancel-state.json`, `permission-uia.json`, `stall.png`. | Permissions were controlled standalone requests under explicit ask rules, not all generated by an executing agent tool. Full no-duplicate/streaming-history assertions and physical keyboard operation are still pending. `stall.png` shows Running with older conversation content; it is not evidence that the streamed tail was visible. |
| Form accessibility/stale state | Before fix, the answer edit had no UIA name. After fix, `named-form-uia.json` exposes Fixture; visually inspected `named-form.png` retains its visible label and layout. `connection-loss-uia.json` shows Fixture and Submit answer disabled after stopping the owned service. | Narrator reading/announcements, all field variants and busy-state timing still need observation. |
| Startup, Settings and panes | Repeated packaged launches automatically restored the fixture project/session and pending question. Settings return retained `WI014 retained draft` and returned focus to ProjectsToggle; two Details toggles retained that draft. `settings-draft.json`, `settings-focus.json`, `panes-draft.json`. | Complete keyboard-only add/select/remove/dialog/focus matrix, reading-position and streaming-focus checks remain pending. Menu keyboard probes did not establish removal and are not counted as a pass. |
| Connection loss/recovery | Stopping the isolated server disabled request controls while preserving `WI014 recovery draft`; `connection-loss-draft.json`. | Reconnect to a restarted service, restored attention/ownership, and uncertain-delivery/no-resend native scenarios remain pending. |
| Size/scale/theme/long content/motion | Dark, 200% current-session pending-question and Running captures were inspected. Runtime preference reevaluation is implemented for Details. | 100%/150%, independent minimum/target-size and light/high-contrast checks, long titles/paths, multiline drafts, long/tool-heavy histories and actual reduced-motion behavior remain pending. |

At the end of the initial pass, the broad acceptance criteria remained unchecked because each still contained required unobserved native coverage. The clean-session follow-up below supersedes the tree-traversal gap in that initial matrix. Do not start WI-015 until this item's remaining acceptance criteria are satisfied.

## Clean-session follow-up (2026-10-25)

The user requested checking the session again after restarting Windows. `quser` now reports the active console logon as 2026-10-25 16:37, and `Get-CimInstance Win32_OperatingSystem` reports boot time **2026-10-25 16:37:07.500 +08:00**, replacing the earlier October 19 boot/logon. This establishes the requested fresh interactive Windows session. Windows remains 10.0.26300, WinApp CLI 0.7.1, local target at 192 DPI / 200%, dark theme.

### Setup and exact checks

- Evidence root: `%TEMP%\opencode\wi014-clean-20261025\`; `environment.json` records the boot/logon and tool version. `verification-start.txt` bounds crash-event/dump queries. A fresh isolated pinned 2.1.4 service/provider fixture restored a project with its pending question; twelve additional disposable sessions were created to exercise a populated tree. Original package settings and encrypted credentials were backed up before seeding.
- `winapp run ./src/OpenCodeDesktop/OpenCodeDesktop.csproj --arch x64 --detach --json` built and launched the current packaged binary, zero errors / 34 warnings, as **local PID 20420, HWND 197978**, using the same package/AUMID and `bin/Debug/net10.0-windows10.0.19041.0/win-x64/AppX/OpenCodeDesktop.exe` path recorded above. Build/launch evidence: `launch.log`. No application source changed during this follow-up, so unchanged unit/integration suites were not repeated.
- `docs/integration/verify-native-tree.ps1 -AppProcessId 20420 -OutputDirectory "$env:TEMP/opencode/wi014-clean-20261025/initial-names" -Iterations 5` passed: fifteen realized rows in each snapshot, all meaningfully named. The same check passed for five final snapshots under `final-names`.
- `& "$env:TEMP/opencode/wi014-clean-20261025/traverse.ps1" -Iterations 10` passed all ten cycles. Each cycle resolves the project from the current UIA tree, focuses it, invokes exact UIA Collapse then Expand, refocuses it, and injects Down, Down, Up, Enter through `send-input`, with 200 ms between operations. It records focused element names/types after each key, checks the accessibility tree again, and confirms the same app process is alive. Repeated collapse/expand also exercises child destruction/recreation and naming after realization. Exact harness, ten snapshots and per-key results are retained as `traverse.ps1`, `uia-keyboard-1.json` through `uia-keyboard-10.json`, and `uia-keyboard-results.json`.
- The first focus attempt was refused with `foreground_not_target`; no keyboard input was sent by that failed attempt. The user activated the app and replied ready, after which the ten-cycle run succeeded. This refusal is not an app crash or a passing traversal iteration.
- `winapp ui screenshot -a 20420 --output "$env:TEMP/opencode/wi014-clean-20261025/after-traversal.png" --json` produced a valid 2884x1860 capture. It was visually inspected: populated tree, selected session, workspace, composer and Details rendered normally. UIA inspection is the evidence for names behind truncated visible titles; no live XAML attachment/injection was performed.

### Results and remaining limits

| Check | Result | Evidence source |
| --- | --- | --- |
| Fresh Windows session | Confirmed boot and console logon after the earlier test pass. | `environment.json`. |
| UIA tree names | Five initial and five final snapshots passed, fifteen named realized rows each. | `initial-names`, `final-names`. |
| UIA patterns plus injected keyboard traversal | Ten complete cycles passed; the same process remained alive. | `uia-keyboard-results.json`, per-cycle snapshots. |
| Physical keyboard | The user physically repeated Left/Right expansion, Up/Down navigation and Enter selection, and reported **Works without a crash**. | Explicit user confirmation in this chat; not substituted with injected-input evidence. |
| Narrator | The user repeated navigation with Narrator and reported **Works and announces names**. | Explicit user confirmation in this chat; speech was not captured or independently transcribed by automation. |
| Crash diagnostics | Zero new Application events matching OpenCodeDesktop.exe with IDs 1000/1001/1026, zero new OpenCodeDesktop crash dumps after the verification start, and the test process remained alive through the final check. | `crash-events.json`, `summary.json`; earlier dumps dated 11:31–12:07 remain untouched. |
| Cleanup | Test app/service/provider closed; original settings restored byte-for-byte and original credential files restored. User-started Narrator was left under user control. | Settings hash comparison and owned-process cleanup in this chat. |

The clean-session tree acceptance criterion is now checked based on the automated results and the user's physical-keyboard/Narrator confirmations. The old `0xc000027b` fail-fast was **not reproduced** on the current fixed build after reboot. This does not establish whether reboot, the accessibility fixes, or another difference explains the earlier failure; the original crash's causal diagnosis remains unknown, and no new crash required dump triage. The earlier WI-013 reproduction/dump findings remain the historical record.

At the end of this follow-up, project-management/dialog/focus coverage, native reconnect/uncertain-delivery assertions, the task/streaming matrix, display-size/scale/theme/long-content coverage, reduced-motion observation, and request/status Narrator announcements beyond tree navigation still required work. The completion verification below addresses those gaps. `git diff --check` passed for that documentation update.

## Completion verification (2026-10-25)

The remaining native checks exposed four further defects: unpredictable project-action focus return, long titles/drafts hiding the initial compact composer toolbar, session navigation losing reading position, and an accepted task remaining Queued after a real tool rejection. A final visible-stream check also exposed partial REST history discarding live text until completion. These were fixed within the existing modules and product scope.

### Resulting behavior and regressions

- Project addition restores focus after the queued tree render; removal focuses the next remaining project or Add project. Native keyboard add/remove, dialog cancellation, project/session selection, Settings return, composer tab order, Details keyboard resize, expansion/restoration and closing were exercised. Minimum-size keyboard traversal reached the visible Send button. Removal changes the navigation list only; fixture directories and server sessions remained intact.
- Long workspace titles use at most two lines, or one at compact heights. The compact editor scrolls internally and the composer receives enough height for its toolbar. Full titles remain in UIA and tooltips. This preserves essential actions at a 640x480 client area without a visual redesign.
- Reading positions are retained in memory by the existing connection/project/session owner key and restored after history loads. The reproduced top → empty session → return jump from 0% to 100% now remains 0% → 0%. Streaming while reading earlier history retained the viewport and DraftBox focus.
- An explicitly completed assistant failure newer than the latest owned/admitted input can reconcile a task without a new idle marker, only when the service is inactive and its inbox is empty. This fixes the real 2.1.4 read-tool rejection case while preserving gates for older/different-input failures, active execution and queued inputs. Core tests cover both failure outcomes and four negative cases; Infrastructure maps only explicit completed errors. The [sanitized runtime evidence](../integration/contracts/opencode-2.1.4-native-reliability-evidence.json) records the observed stale idle/outcome semantics. In the same running native process, Reject now produces Interrupted, releases Send, and permits a new Control+Enter task; server history contains one input for each action.
- Unfinished assistant history retains content-part ordinals and completion metadata at the Infrastructure/Core boundary. Core overlays live text by part while preserving other content and keeps buffers until completed authoritative history replaces them. The regression exercises a partial history refresh between deltas and final replacement without duplicate text. The final packaged app displayed partial text while Running, grew from 275 to 650 accessible characters across two captures, retained composer focus/draft, interrupted successfully, and recorded one server-side input. This closes the earlier blank-until-completion defect.
- The provider fixture adds `WI014:stream` (forty timed chunks) and `WI014:long` (forty sections/code blocks). The new loopback-only [reliability proxy](../integration/native-reliability-proxy.mjs) deliberately loses one accepted prompt response and can hold an interaction reply for four seconds; it never logs headers, credentials or prompt bodies. These are verification utilities, not production dependencies.

### Final evidence matrix

Evidence root: `%TEMP%\opencode\wi014-remaining\`. All content originates in disposable fixtures; screenshots, recordings, settings/credential backups and detailed runtime output remain outside Git. The earlier clean-session physical keyboard/Narrator confirmations continue to apply.

| Area | Observed result | Evidence under that root |
| --- | --- | --- |
| Keyboard and focus | Keyboard entry/add, inline project actions/removal, remaining-project focus, project/session activation, Settings return, Model → Agent → Send traversal, dialog cancellation, Details Left/Right resize and expand/restore/close focus passed. At minimum size, Projects exposes the overlay and Settings/Add project; a tall permission card is scrolled to reach its actions. | `add-project-focus-fixed.json`, `remove-project-focus-fixed.json`, `keyboard-composer-focus.json`, `minimum-send-focus-final.json`, `final-critical-result.json`, `git-same-process-reject-fixed.json`. |
| Real execution and permissions | Native send/Control+Enter, visible incremental streaming, interruption, actual read-tool Allow once/Reject/Save project allow rule passed. The saved rule allowed a later matching read evaluation. A separate disposable Git project isolated rejection from that saved rule for the same-process gate regression. Explicit completion after rejection produced exactly one further user input. | `real-tool-permission-*.json`, `real-tool-results.json`, `git-same-process-reject-fixed.json`, `final-critical-result.json`, `last-stream-partial.png`, `last-stream-partial.json`, `last-stream-later.json`, `last-stream-result.json`. |
| Forms and busy state | Empty required answer produced a retained validation error. String/pattern/length, decimal, integer, boolean, multiselect, option selection and explicit external acknowledgment were filled and accepted. All eight controls were disabled during the held reply. Native cancellation cleared pending forms. | `form-invalid.json`, `form-busy2.json`, `form-busy-inputs2.json`, `validation-form-id.txt`; controlled server form-list reads. |
| Narrator | The user enabled Narrator, used the keyboard to submit an empty answer and then Alpha, and explicitly confirmed the field-name, validation-error and resulting status announcements all work. The server confirmed no form remained pending. | User confirmation in this chat; `manual-narrator-result.json`, `manual-acceptance.png`. Speech was observed by the user, not inferred from UIA properties or transcribed by automation. |
| Recovery and uncertain delivery | The proxy consumed an upstream HTTP 200 then dropped that prompt response; the prompt count stayed three before/after reconciliation and interruption. Stopping/restarting the transport proxy with the service intact restored the same session's pending form and editable controls, retained a two-line draft and sent zero further prompts. | `uncertain-stats.json`, `uncertain-stats-final.json`, `proxy-before-outage-stats.json`, `transport-down.json`, `transport-final.json`, `transport-recovered.png`, `recovered-draft.json`, `recovery-result.json`. |
| Startup/service restart | Packaged launches restored the protected fixture connection, selected project/session and pending question. A full service restart discarded the service's in-memory form; the client correctly reconciled to the empty authoritative form list rather than resurrecting it. Draft retention applies to the existing in-memory lifetime; persisting unsent drafts across app exit remains explicitly excluded by WI-012. | Earlier startup evidence; `service-down.json`, `recovery-inspect.json`, `connection-loss-draft.json` from the initial evidence root, and the restart/proxy scripts in this root. |
| Reading and long content | A long title/path, multiline draft, forty response sections/code blocks and more than thirty historical entries with tool/error cards remained navigable. Session round-trip reading position and streaming focus/viewport assertions passed. | `reading-position-before-fix.json` vs `reading-position-fixed.json`, `stream-reading-result.json`, `stream-reading-fixed.json`, provider fixture and retained screenshots. |
| Layout/themes/scales | All eighteen combinations below passed actual DPI/client-size assertions and visible Send bounds. All captures were reviewed in three contact sheets, with individual minimum/target captures inspected at useful resolution. High contrast preserved text, borders, focus/selection and textual statuses. | `light-*`, `dark-verified-*`, `high-contrast-*` PNG/JSON/dimension files and the three `*-contact.png` sheets. Earlier `dark-*` captures made before a theme-selector ambiguity was corrected are excluded; only `dark-verified-*` counts. |
| Reduced motion | Read the native animation preference as 1, disabled it to 0, and recorded two Details toggles with no observed intermediate motion. Restored it to 1 and verified continued operation. Both six-second captures contain three distinct endpoint images at approximately 16 achieved fps; the cadence warning means they do not establish sub-frame timing or a measured enabled-animation duration. | `original-animations.txt`, `motion-disabled-value.txt`, `motion-restored-value.txt`, `motion-off.mp4`/`.frames`, `motion-on.mp4`/`.frames`. No live XAML attachment was available in the installed CLI; source and native captures were used. |
| Cleanup/diagnostics | No new OpenCodeDesktop crash events/dumps since the clean boot. Original workspace JSON restored byte-for-byte and the original credential file verified by hash; owned app/server/provider/proxy processes stopped. Active system settings restored to dark apps/system mode, 200%, contrast off and animations enabled. Narrator started by the user remains under user control. | `crash-summary.json`, `restored-system-settings.json`, `cleanup-result.json`. Settings' contrast UI was unavailable during restore, so the documented native high-contrast setter cleared the active flag and readback verified it. |

The display matrix uses **client dimensions in DIPs**, with actual per-monitor DPI changes through Windows Settings and native client-size measurement:

| Theme | 100% / 96 DPI | 150% / 144 DPI | 200% / 192 DPI |
| --- | --- | --- | --- |
| Light | 1440x900 and 640x480 passed | 1440x900 and 640x480 passed | 1440x900 and 640x480 passed |
| Dark | 1440x900 and 640x480 passed | 1440x900 and 640x480 passed | 1440x900 and 640x480 passed |
| Night sky high contrast | 1440x900 and 640x480 passed | 1440x900 and 640x480 passed | 1440x900 and 640x480 passed |

### Final commands and results

| Command/check | Result |
| --- | --- |
| `dotnet test --project tests/OpenCodeDesktop.Core.Tests/OpenCodeDesktop.Core.Tests.csproj` | 136 passed, zero failed/skipped; `core-complete.log`. Seven meaningful cases added since the 129-test baseline. |
| `dotnet test --project tests/OpenCodeDesktop.Infrastructure.Tests/OpenCodeDesktop.Infrastructure.Tests.csproj` with both pinned-executable opt-ins set | 102 passed, zero failed, one skipped installed-service discovery check; `infrastructure-complete.log`. Six mapping cases added since baseline. |
| `dotnet test --project tests/OpenCodeDesktop.Presentation.Tests/OpenCodeDesktop.Presentation.Tests.csproj` | 17 passed, zero failed/skipped; `presentation-complete.log`. |
| `dotnet build OpenCodeDesktop.slnx -p:Platform=x64` | Final incremental build zero errors/warnings; `build-complete.log`. Earlier broader rebuilds still report existing warnings; they were not hidden or broadly cleaned up. |
| `winapp run ./src/OpenCodeDesktop/OpenCodeDesktop.csproj --arch x64 --detach --json` | Final building launch: local PID 19356 / HWND 787766, same package/AUMID and exact AppX output path recorded above; `launch-complete.log`. Final visible-stream/interrupt smoke passed on that binary, then it was closed for restoration. |
| Runtime OpenAPI comparison | All 25 retained task-workflow paths and 135 schemas structurally match the running pinned binary; `contract-comparison.json`. No version migration or new production operation. |
| `docs/integration/verify-native-tree.ps1 -AppProcessId 19356 -OutputDirectory "$env:TEMP/opencode/wi014-remaining/tree-complete" -Iterations 3` | Eight realized rows per snapshot, all meaningfully named. |
| `node --check docs/integration/task-provider-fixture.mjs` and `node --check docs/integration/native-reliability-proxy.mjs` | Passed. Proxy/provider paths were exercised by the native checks. |
| `git diff --check` | Passed after final source/documentation normalization. Incidental RID-only package-lock changes from verification were reverted; dependencies were unchanged. |

The initial streaming regression's completion assertion ran before a coalesced refresh; the test now waits for the verified terminal event/read instead of assuming a synchronous refresh during loading. Subsequent complete suites passed. Failed selector/foreground probes and captures taken before a setting applied are not counted as passes.

All acceptance criteria are supported by the evidence above and the user's manual confirmations. Coverage is bounded to this Windows 11/SDK/pinned-server environment and controlled provider; the installed-service discovery skip and unisolated original crash cause remain explicit limits, not passing claims. No further implementation or required manual confirmation remains for this item. Completion is recorded on 2026-10-25; WI-015 has not been started.
