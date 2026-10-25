# WI-016 Review repository and session changes

## Details

- Type: Implementation
- Related requirements: [REV-02 and workspace layout](../requirements/overview.md)
- Priority: P1
- Dependencies: [WI-015](WI-015-browse-preview-project-files.md), [WI-008](WI-008-implement-live-task-workflow.md)
- Status: See the [completed work item index](completed.md)

## Goal and baseline

Enable Details > Changes to inspect task results with an explicit comparison basis. The current tab is disabled and no VCS or turn-diff transport is implemented.

## Scope

- Verify pinned VCS info/status/review-base/diff operations and session-turn diff boundaries. Record available comparison modes, default bases, untracked/binary/rename behavior and non-repository errors.
- Provide a changed-file list and expanded diff in Details, with an explicit selector distinguishing supported repository comparisons from a selected session turn/range. Show base/turn identities and do not label all working-tree changes as agent changes.
- Support refresh, added/deleted files, empty/error/unavailable states and bounded large patches. Preserve selection/scroll per comparison and workspace; expand/restore the pane for review.
- Open Changes from verified conversation change summaries and allow selecting a file for preview through WI-015. Guard loads against connection/project/session and comparison changes.

## Exclusions and deliverables

No staging, commit, discard, merge, file editing or revert mutation. Deliver pinned contract/evidence, Core review models/state, Infrastructure mappings, native diff presentation and regression tests. A readable selectable unified diff is sufficient; a full editor component is not required.

## Acceptance criteria

- [x] Repository and session-turn comparisons are independently selectable and clearly identify their actual basis/boundaries.
- [x] Controlled added/modified/deleted and supported rename/binary/untracked cases produce correct file lists and honest content states; non-repositories remain usable.
- [x] Conversation change links open the correct comparison/file; tab/pane navigation retains comparison selection and reading position.
- [x] Refresh and late responses cannot mix workspaces, turns or bases; large diffs respect documented limits and remain responsive.
- [x] Review performs no repository mutation and makes no unsupported claim that repository changes all belong to the selected task.
- [x] Contract probes, automated checks and x64 build pass; the user accepted completion with the documented native review/accessibility verification gaps on 2026-10-25.

## Verification requirements

Apply the [shared checks](README.md#shared-execution-and-verification-requirements). Use temporary Git repositories with known commits and working changes, plus controlled session turns; compare returned results with fixture expectations. Test query encoding, mapping, comparison identity, missing bases/turns, stale responses and patch limits. Native checks cover comparison changes, selectable patch text, long paths, large patches, Files handoff and keyboard selection. Keep user repositories unchanged.

## Planning record

Defined on 2026-10-25. At definition, implementation and runtime contract verification had not started. Documentation validation is recorded in the active index.

## Implementation decisions (2026-10-25)

- Added Core review models and state keyed by connection, resolved project location and session, with per-comparison selection and patch/list reading offsets. Cancellation plus monotonically increasing generations reject late responses after refresh, base/turn changes, navigation or reconnect. Connection address changes/removal discard retained review state. UI request generations additionally guard overlapping conversation links for the same turn.
- Infrastructure implements read-only repository and turn comparisons against the [pinned runtime contract](../integration/contracts/README.md#change-review-contract-wi-016). It checks returned repository locations and validates session directory/project/ID before fetching snapshots. Repository comparisons identify HEAD or merge-base/ref and destination, explicitly warn that repository changes can include other work, and keep session snapshots separate.
- Enabled Details > Changes with comparison controls, optional explicit base, recorded user-message range selectors, refresh, changed-file selection, selectable unified patches, loading/empty/error states and current-file preview handoff to project Files. Existing pane expansion/restoration remains available. The displayed basis identifies the loaded result; edited selector values take effect when Apply / Refresh is chosen. Session turn options combine loaded older history and current authoritative task history.
- At compact heights the comparison controls/basis occupy a scrollable viewport capped at 96 DIPs, keeping the changed-file list and patch accessible below it; at larger heights the cap is 220 DIPs. Keyboard focus scrolls hidden comparison controls into view. A bounded 25 ms restoration timer (at most 20 ticks) restores virtualized list and patch offsets after layout.
- Conversation links come only from structured assistant `snapshot.files` and a preceding recorded user boundary. Prose that resembles paths or claims changes creates no review link. The server verifies the requested turn; file selection uses its returned diff list. Deleted files remain reviewable but have no current-file preview action.
- Requests use three context lines. Transport rejects responses over 4 MiB; the list is capped at 1,000 files and each displayed patch at 100,000 characters, without splitting a surrogate pair. Truncation/size errors are visible. Binary notices and added/deleted rename pairs retain the server's actual representation. No textual patch supplied and no returned changes have honest states, including the no-snapshot case.
- Review sends no mutation requests. The deterministic verifier's separate task fixture intentionally writes synthetic files to obtain real turn snapshots; it never operates on a user repository. No package dependencies changed.

## Verification evidence (2026-10-25)

Evidence root: `%TEMP%\opencode\wi016\`. Detailed logs, disposable native fixture metadata, screenshots and UIA snapshots remain outside Git. The retained OpenAPI/evidence contain synthetic data only.

| Check | Actual result / evidence |
| --- | --- |
| `docs/integration/verify-review.ps1 -ExecutablePath "$env:TEMP/ocd-wi002-cli/package/bin/opencode.exe" -OutputDirectory "$env:TEMP/opencode/wi016/probes-final"` | Passed against the hash-checked isolated 2.1.4 binary and structurally matched the retained runtime slice. Checked added/modified/deleted, staged rename as add/delete, binary notice, Unicode/space/hash/percent untracked paths, three repository modes, explicit/missing base, non-repository, missing session/turn and no-snapshot turns. Real provider/tool-driven first/second turns and their range returned expected patches. Review-only requests preserved fixture Git status. `probes-final.log`. |
| `dotnet test --project tests/OpenCodeDesktop.Core.Tests/OpenCodeDesktop.Core.Tests.csproj` | 158 passed, 0 failed/skipped. New checks cover noncooperative late responses, bases/owners/reconnect, comparison and session identity, selected path/patch/list offsets, missing turns and safe size failures. `core-final.log`. |
| `dotnet test --project tests/OpenCodeDesktop.Infrastructure.Tests/OpenCodeDesktop.Infrastructure.Tests.csproj` | 126 passed, 0 failed, 1 installed-service discovery opt-in skipped. Both pinned executable opt-ins enabled. New checks cover GET-only encoded requests, inferred-base pinning/labels, ownership, structured summaries, file/patch bounds, oversized/wrong-location rejection. `infrastructure-final.log`. |
| `dotnet test --project tests/OpenCodeDesktop.Presentation.Tests/OpenCodeDesktop.Presentation.Tests.csproj` | 28 passed, 0 failed/skipped, including recorded change-summary boundaries and no path guessing. `presentation-final.log`. |
| `dotnet build OpenCodeDesktop.slnx -p:Platform=x64` | Passed with 0 errors; analyzer/style warnings remain. `build-final.log` and current-source `build-current.log`. |
| `winapp run ./src/OpenCodeDesktop/OpenCodeDesktop.csproj --arch x64 --detach --json` | Building packaged launches passed; PID 18124 verified automatic loading and bounded restoration. PID 1760 verified the final compact-height layout, merged turn options and conversation-link guards. `launch-final.log`, `launch-verified.log`, `launch-responsive.log`. |
| Native UIA scenarios | Verified 7 working-copy entries, binary notice, actual second-turn comparison/file from a conversation link, current server preview in Files, session-tab return retaining Changes, inferred committed comparison with `refs/heads/main`, safe missing-base error, long 600-line patch, pane close/reopen and expand/restore, file selection with Down key. Patch position survived Files/tab switching at 100% and comparison switching at 99.90% (same retained pixel offset with changed viewport height). `native/final-*.json`, `native/summary-linked.json`, `native/files-handoff.json`, `native/scroll-before.json`, `native/scroll-after.json`. |
| Final compact layout and source | Initial 640x480 UIA inspection exposed an unusably small patch viewport. After reducing the compact comparison-control viewport, UIA measured a 27-DIP file list and 53.5-DIP patch at 640x480, with controls/basis available by scrolling. Rechecked the actual second-turn conversation link at 1440x900. `native-final/responsive-min-bounds.json`, `native-final/responsive-min-dimensions.json`, `native-final/responsive-wide-dimensions.json`, `native-final/current-source-turn.json`. These bounds do not replace visual acceptance. The current-source Infrastructure rerun passed 123 tests with 4 opt-ins skipped; a subsequent current-source run with both pinned executable opt-ins enabled passed 126 with 1 installed-service discovery opt-in skipped. `infrastructure-current.log`, `infrastructure-current-optin.log`. |
| API/control discovery | `winapp find-ui 'ComboBox'`, `winapp find-ui --id gallery-combobox-2`, and project-scoped `find-api` for ScrollViewer/TextBlock selection and offsets. SDK discovery fell back from targeted 19041 to installed 26100; compilation/native runs establish this environment only. |

Initial verification corrected a fixture Git helper recursion/name collision, ambiguous unnamed-HEAD base setup, an incorrect test call to the existing `ServerAddress.TryParse` signature, and the Pivot event-source assumption that prevented initial loading. Corrected probes/tests and native automatic loading passed. An exact 100% comparison-scroll assertion was too strict after viewport resizing; observed 99.90% retains the saved reading offset. These failed attempts are not passing evidence.

## Acceptance and retained verification limitations

The user explicitly requested marking WI-016 Done on 2026-10-25, accepting completion with the previously disclosed visual/theme and accessibility verification gaps. This acceptance does not establish that the unavailable checks passed. WinApp UIA exposes populated, interactive controls, but both window capture and `--capture-screen` produced black client areas; those images were inspected and are not usable visual evidence. The Computer Use skill's independent Windows capture path was also attempted and showed black/occluded content, not a usable review image. The light/dark/high-contrast matrix, readable layout at both required sizes, patch text selection/copy and Narrator confirmation are not marked passed. Installed WinApp CLI lacks `devtools`, so no live XAML attachment was possible. The project does not reference the WinUIAnalyzer package, so its dedicated checks did not run; no dependency was added for verification. No additional SDK/tooling or system theme changes were made merely to work around capture. These limitations remain recorded after user acceptance; no new native verification was performed for the status change.

Owned app/provider/server processes are stopped after verification; original workspace settings are restored and temporary protected credentials are absent. `native/restoration-result.json` and `native-final/restoration-result.json` record restoration. Synthetic native fixtures and evidence remain outside source control. Unobserved contract boundaries include active snapshots, busy steering, cross-location ranges and remote/POSIX providers. No later work item was started.

Completed on 2026-10-25 by explicit user acceptance. Moved the index row to the completed index and set WI-017 as next without starting it. Documentation-only status update: verified unique index membership, status link and git diff --check; no application build or tests rerun.
