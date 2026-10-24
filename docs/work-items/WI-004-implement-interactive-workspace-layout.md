# WI-004 Implement the interactive workspace layout and Settings navigation

## Details

- Type: Implementation
- Related requirements: [Workspace Layout and interaction requirements](../requirements/overview.md#workspace-layout); presentation surfaces for PRJ-01, SES-01/02/03, MSG-01/02, EXE-01/02/03, INT-01/02, REV-01/02, and P2 tools/settings. This item does not complete their live-service behavior.
- Priority: P0 shell foundation
- Dependencies: [WI-003](WI-003-implement-application-shell.md), completed
- Status: See the [completed work item index](completed.md)

## Goal

Deliver a runnable WinUI shell whose principal components have real layout, navigation, selection, and local interaction. A user must be able to launch without an OpenCode server and try the workspace, Details tabs, terminal surface, and Settings using explicitly labeled in-memory sample content. Empty boxes, static screenshots, and inert buttons alone do not satisfy this item. Later work supplies actual API operations and richer rendering.

## Design and agreed boundaries

Use the replaced [desktop concept](../requirements/desktop-concept.png), the [workspace requirements](../requirements/overview.md#workspace-layout), and existing [design tokens](../design/design-system.md). The image shows a representative Changes-tab state; the written scope below also defines the other tabs and Settings. Preserve the native Windows title bar and existing light/dark/high-contrast foundation. Match hierarchy and interaction rather than generated pixel details or example model names.

- Remove the outer application navigation rail. Projects/sessions are the only left workspace panel.
- Keep the central header, independently scrolling conversation, pending-input area, and anchored composer.
- Replace Session info with a right-hand Details pane containing Info, Files, and Changes tabs. File previews and expanded diffs belong here, never in a bottom preview/diff panel.
- Put Connections and Extensions inside Settings. Settings is reached from the projects-panel footer; when Projects is hidden, reopen it to reach Settings. Do not duplicate Settings in the workspace header. Settings replaces the workspace content and has an explicit return action.
- Keep connection status and Needs attention reachable outside Settings. Connection state and session execution state have separate labels.
- Both side panels collapse independently. Details is resizable and has expand/restore; narrow layouts use overlays or a full-content Details view without squeezing text into unusable columns. Terminal output alone may use a collapsible bottom surface.
- Opening/closing Details animates only the Details pane. The main header, conversation, and composer may reflow to their available width but must not slide in or appear to reload.

## Scope and component interactions

### 1. Sample data and session state

The app opens directly with sample data and a selected session. There is no separate sample/preview mode, mode flag, entry/exit flow, or global reset control. Supply at least two fictional projects with invented names, paths, task text, and file content unrelated to the user's projects; include several sessions with distinct execution states, a multi-message conversation with code and tool activity, a pending permission, a question form, files, changes, and terminal output. Connection status remains truthfully Disconnected; the status strip identifies sample data. Restarting the app recreates the in-memory fixtures for repeatable review.

Keep sample state in App presentation models/view models, separate from real connection state and future service composition. Switching sessions preserves independent drafts, tab selection, selected file/diff, and scroll position within the running app. Samples never trigger HTTP, process execution, credential persistence, repository changes, provider authentication, or actual permission decisions. No fake success is shown as a live result.

### 2. Projects and sessions

Implement project expand/collapse, project/session search with clear and no-results states, session selection, and a New task action creating/selecting an in-memory draft. Project controls expose a local sample directory/worktree selector. A session menu provides local rename, fork, archive/restore, and delete with observable local effects; cancel leaves state unchanged. Keep archived sessions reachable through a filter. Other future operations may be omitted rather than exposed as inert buttons.

### 3. Header, conversation, and composer

Show selected session title, server-side directory, sample branch/worktree, and execution state. Provide Projects, Details, Terminal, and Settings access. Render selectable sample text, a code block, expandable tool cards, a clickable file reference, and a clickable change summary.

Composer controls include multiline draft input, attachment/reference sample picker with removable chips, model/agent selectors, and command/skill discovery with sample entries. Submit appends a clearly simulated local message; no agent response is implied. Show a pending-input list with queue/steer selection and cancel actions. A local interrupt action changes only the sample execution state. Disabled actions explain their prerequisite; enabled actions must have an observable effect.

Permission cards expose their scope and allow/reject choices; question cards support input, validation, submit, and cancel. Local responses update those cards and attention counts. Needs attention opens a cross-session list; choosing a request selects its owning session and reveals the relevant card. Restarting restores requests for another trial.

### 4. Details pane

- **Info:** sample session/provider/model/agent metadata, context usage, activity, usage/cost, and instructions. Support expandable sections and a local instructions editor with save/cancel feedback clearly scoped to sample data.
- **Files:** searchable sample file navigation and a selected-file text/code preview with path and line numbers. Conversation file references open this tab and select the referenced file. Unsupported/binary and missing-file samples have explicit states; do not read actual server paths through local file APIs.
- **Changes:** selectable changed-file list and a readable unified diff with additions/deletions identified by more than color. Comparison choices are Session turn, Working copy, and Branch, each with a visible basis and separate sample results. Conversation change summaries select the relevant diff. Represent no-changes state. No commit, push, or real revert action is required.
- Preserve each tab's selected item and scroll position across tab and session navigation. Closing/reopening Details preserves context. Expand makes Details occupy the available workspace content area; restore returns to the previous split width. Both actions retain the active tab and content.

### 5. Terminal surface

Provide a collapsed-by-default, independently scrollable bottom terminal panel within the central workspace. Include terminal selection, sample output, clear/reset output, and close/reopen. An optional input field must label its interaction as simulated. Do not start a PTY or shell process. Closing the surface restores conversation space; file previews and diffs remain in Details.

### 6. Settings

Provide section navigation for **Connections**, **Models & Providers**, **Extensions**, **Permissions**, **Appearance**, and **Notifications**. Connections has the address/password form layout and explicit unavailable/live-integration explanation; do not accept or save real secrets while using sample data. Extensions includes MCP and plugin sample lists with selection and detail/status surfaces. The other sections provide representative selectors, toggles, or list/detail controls with local sample feedback. Appearance can continue following the system theme; theme persistence is deferred. Mark service-dependent actions unavailable with an explanation rather than simulating real authentication or updates.

All sections must be reachable, scroll correctly, and offer usable controls rather than headings alone. Returning to Workspace restores the selected session, draft, open panels, and Details context. Do not implement live connection behavior here; [WI-002](WI-002-implement-server-connection.md) replaces the Connections sample form afterward.

## Responsibilities and exclusions

App owns views, reusable presentation controls, view models, sample fixtures, in-memory navigation, and composition seams. Core and Infrastructure remain UI-independent; do not introduce business or transport abstractions solely for sample behavior. Reuse/update semantic resources and native controls rather than hardcoding independent styling per component.

Excluded: all live OpenCode operations, real filesystem browsing/writing, terminal execution, streaming/recovery, credential storage, persisted preferences, full editor/LSP, full Git management, production Markdown/diff virtualization, and complete coverage of every API operation. The layout must leave clear extension points without implementing those workflows.

## Deliverables

1. Revised runnable shell and functional sample-backed workspace covering every component above.
2. Maintainable App views/view models and repeatable sample fixtures, with no new backend dependency.
3. Updated design-system documentation reflecting actual regions, sizing tokens, responsive thresholds, Settings navigation, and WI-002 connection integration points. Remove obsolete navigation resources when unused.
4. A short repeatable launch/interaction walkthrough and actual verification evidence in this work item. Save screenshots outside the repository and record their paths; the concept remains the repository's design image.

## Observable acceptance criteria

- [x] App launches directly with fictional sample projects and a selected session, has no sample-mode entry/exit/reset controls or application navigation rail, and never implies live connection or performs service/process/file mutations.
- [ ] Project grouping, search/no-results, session switching, sample directory/worktree selection, New task, and session-menu operations work locally; drafts remain independent between sessions.
- [ ] Header, conversation, tool expansion, attachment chips, model/agent selectors, command/skill picker, simulated submission/interrupt, and queue/steer/cancel controls have observable local behavior.
- [ ] Permission and form interactions, validation/cancellation, attention counts, and cross-session attention navigation can be exercised repeatedly by restarting the app.
- [ ] Info, Files, and Changes tabs work; file references and change summaries navigate to the correct content; comparisons identify their basis; file/diff empty and unavailable states are inspectable.
- [ ] Tab/session selection and scroll state survive navigation; Projects and Details collapse/reopen independently; Details resize and expand/restore work without losing content or focus.
- [ ] Terminal opens, selects sample output, clears/resets, and closes without executing commands or taking ownership of previews/diffs.
- [ ] All six Settings sections are reachable with representative controls; Connections and Extensions have no outer navigation entries; return restores workspace state; Settings is reached through the Projects footer, reopening Projects when hidden, without a duplicate header entry.
- [ ] At initial 1440 x 900 DIPs and existing 640 x 480 outer minimum, primary actions remain reachable, overlays dismiss, independent scrolling works, and long paths/diffs do not squeeze sidebar text or overlap the composer/status area.
- [ ] Keyboard traversal/activation, tab and list navigation, focus recovery, accessible names, non-color status/diff cues, system light/dark/high contrast, and 100%/150%/200% display scaling plus enlarged text are checked with actual results recorded.
- [ ] Normal x64 solution build passes; a documented app launch route works; runtime walkthrough covers the interactions above. Required review is accepted before Done; unavailable checks remain unchecked and implementation-complete work stays Awaiting Acceptance.

## Verification requirements

```powershell
dotnet build OpenCodeDesktop.slnx -p:Platform=x64
git diff --check
```

Run the built app using a working packaged development deployment. If normal activation fails, record the error and exact temporary self-contained/unpackaged build/launch command used (see WI-003 precedent); do not silently change repository packaging or treat a build as a successful launch.

Walkthrough: sample-backed startup -> project/search/session navigation -> edit draft and switch away/back -> create/rename/fork/archive/restore/delete sample session -> expand tool card -> reply to permission/form through attention list -> use composer selectors/attachments/commands/queue -> open referenced file -> switch/select diff comparisons -> Info -> resize/expand/restore/close/reopen Details -> sample terminal -> every Settings section -> return with context intact -> narrow-window and keyboard checks -> restart and confirm fresh sample fixtures. Repeat theme and scaling checks, recording exact environment, observed results, external screenshot paths, and limitations.

Inspect sample actions for accidental service/process/file operations and real-secret retention. Core/Infrastructure test runs are unnecessary for App-only layout work; run their meaningful tests if their behavior changes. Add focused tests only for substantive state logic introduced, such as per-session draft isolation or attention ownership, rather than tests mirroring XAML. Zero discovered tests do not establish behavior validation.

## Planning decisions and evidence

- 2026-10-24: User approved removal of outer navigation, Settings ownership of Connections/Extensions, and Info/Files/Changes within the right Details pane. User requested an updated concept and a work item that yields a runnable, navigable component layout before backend detail is filled in.
- 2026-10-24: Replaced the concept using built-in image generation with the previous PNG as edit reference. The selected image shows Changes active, a file list/diff within Details, the projects-panel Settings entry, and persistent status/attention. Exact generation prompt: [desktop-concept-prompt.txt](../design/desktop-concept-prompt.txt). It is a design illustration, not implementation or runtime evidence.
- 2026-10-24: Place this item before WI-002 and update WI-002 to integrate Connections inside Settings. Explicit offline preview reconciles hands-on layout review with truthful disconnected behavior.

## Implementation and verification record

The initial implementation and observations below describe the superseded sample-mode design. They are historical evidence only. The user correction and current verification at the end of this record supersede entry/exit/reset behavior, fixture names/text, and affected screenshots.

### Implementation (2026-10-24)

- Replaced the outer navigation rail with retained Workspace and Settings views. The native title bar/window sizing foundation remains. Projects and Details are independent SplitViews; Details supports pointer/keyboard resizing and expand/restore. Connection status and cross-session attention remain outside Settings.
- Added App-only `PreviewWorkspace`, `PreviewSession`, and embedded fixtures, with no Core/Infrastructure changes. Preview includes two projects, six initial sessions (including an archived session), distinct execution states, conversation/code/tool samples, permission and question cards, file/binary/missing fixtures, three comparison bases, and recorded terminal output. Normal startup and exit remain disconnected and empty; reset restores fresh fixtures. Samples never call services, open server paths, execute commands, or retain secrets.
- Implemented local search/grouping, session selection and context menus, cancellable rename/delete, fork, archive/restore, new drafts, directory/worktree choices, draft/reference/model/agent controls, command/skill discovery, local submission, queue/steer/cancel, and sample interruption. Drafts, selected Details state, and scroll offsets belong to each sample session. New tasks start with an empty local conversation.
- Added `DetailsView` with Info metadata/expanders/instructions save/cancel, searchable Files with selectable numbered text, and Changes with selectable unified diffs and explicit basis/empty states. File references and change summaries open the matching Details content. Added terminal selection/clear/reset/close without a shell or PTY.
- Added all six Settings sections, a Connections layout with read-only address/disabled password and connection action, sample MCP/plugin status/detail controls, and local-feedback controls for models, permissions, appearance, and notifications. Returning retains the workspace view. WI-002 still owns live integration inside `ConnectionView`.
- Updated the design system for current regions, responsive thresholds, size resources, and WI-002 seams; removed obsolete rail/inspector width resources. Added a UI-independent Presentation test project which links the App preview model sources rather than adding sample concerns to Core or requiring WinUI to execute tests. Its package lock is included.

### Build and state verification

The initial incremental build found stale generated XAML references to removed shell controls. Cleaning the App outputs and rebuilding resolved that issue; no generated files were edited. Analyzer/style warnings introduced during implementation were corrected.

- `dotnet build OpenCodeDesktop.slnx -p:Platform=x64 -t:Rebuild -v:minimal`: passed after final source changes, **0 warnings, 0 errors** (2026-10-24).
- `dotnet test --project tests/OpenCodeDesktop.Presentation.Tests/OpenCodeDesktop.Presentation.Tests.csproj`: passed after final source changes, **4 passed, 0 failed, 0 skipped**. Tests cover independent draft/Details state, attention ownership/validation/reset/exit, fork/delete isolation, and queued versus local submission.
- `git diff --check`: passed. Final text files follow the repository's CRLF convention.

Core/Infrastructure behavior did not change, so their existing test suites are not claimed as executed. Source inspection of preview views/models found no HTTP/client creation, process invocation, local filesystem reads/writes, provider authentication, credential persistence, or real permission decisions. The password control is disabled and has no binding or storage field.

### Launch route and runtime observations

Environment: Windows x64, .NET SDK selected by `global.json`, current system dark appearance, initial 1440 × 900-DIP client area (captured outer window 1442 × 930 pixels). System display scaling and text size were not changed; this run does not establish 100%/150%/200% or enlarged-text acceptance.

Directly starting the normal build executable failed before App startup with Windows App Runtime `0x80040154 (REGDB_E_CLASSNOTREG)`, confirmed in the .NET Runtime event log. The computer-use launch helper selected the already installed older packaged shell, which was identified by its old navigation and excluded from WI-004 evidence. Existing user instances were left alone. A separate self-contained/unpackaged verification copy was built and launched directly:

```powershell
dotnet build src/OpenCodeDesktop/OpenCodeDesktop.csproj -t:Rebuild -p:Platform=x64 -p:WindowsPackageType=None -p:WindowsAppSDKSelfContained=true -p:OutputPath=C:/Users/jinyi/AppData/Local/Temp/OpenCodeDesktop-WI004/ -v:minimal
Start-Process -FilePath C:/Users/jinyi/AppData/Local/Temp/OpenCodeDesktop-WI004/OpenCodeDesktop.exe
```

`-t:Rebuild` is material: reusing packaged intermediate outputs for the first temporary build did not produce a targetable new preview window. The explicit rebuild passed with **0 warnings, 0 errors** and the separate process launched successfully. Repository packaging properties were not changed. A configured packaged development deployment remains unverified.

| Check | Observed result |
| --- | --- |
| Startup / preview / reset / exit | Passed in temporary copy: no rail, disconnected empty state, labeled sample banner and persistent status, reset restores two attention requests, exit restores empty workspace and zero attention. |
| Keyboard preview activation | Enter activated the focused Preview sample workspace button, with visible native focus. Full traversal is still outstanding. |
| Session selection and independent drafts | Edited first draft through native UI Automation value control; selected second session and observed empty draft; returned to first and observed its original draft. Fixed selectable row text consuming session clicks; conversation text stays selectable. |
| Simulated submit | Appended `You · simulated local message` with the entered draft; draft cleared and no assistant response was generated. |
| Attention navigation / sample permission | Attention dialog showed both owners; selecting the permission owner selected its session and scrolled the scope/command/choices into view. Reject sample recorded `No command was executed`, changed only sample execution state, and reduced attention from two to one. |
| Files / Changes navigation | Conversation file reference opened Files with the expected selected path and numbered embedded text. Change summary opened Changes with a readable unified diff and explicit sample turn basis. Other comparison/file states are implemented but their full runtime matrix remains to be checked. |
| Details expand | Expanded Changes filled available workspace content while preserving the active tab/diff. Full resize/restore/session/tab-scroll matrix remains to be checked. |
| Settings | Visited all six sections and inspected representative controls. Connections password and Connect are disabled; no secret was entered. Notification preview produced local feedback. Return restored selected session, recorded permission outcome, open panels, and conversation position. Persistent disconnected/sample status remained visible. |
| Terminal | Opened recorded output with an explicit no-process/sample label, cleared it, reset the recording, and closed it; conversation space returned. Alternate terminal selection and reopen-state matrix remain to be checked. |
| Current appearance / names | Dark presentation was readable in observed states. Native UI Automation exposed session lists, editable draft, sample request choices, section selector, disabled credential field, and pane controls. Full screen-reader/focus-order acceptance remains outstanding. |

The Windows helper sometimes reported stale accessibility focus/trees immediately after an input. Later observations and screenshots were used to confirm changes. Literal typing did not reliably enter the draft through this helper, so draft verification used its native `set_value` API. A pointer edge drag did not resize the window; native system-menu Size mode produced incomplete captures and was cancelled with Escape. Neither attempt establishes narrow/minimum-size acceptance. No OS theme, display, text, security, or privacy settings were changed.

External screenshots (outside the repository; temporary, not durable release artifacts):

- `C:/Users/jinyi/AppData/Local/Temp/OpenCodeDesktop-WI004-evidence/disconnected.jpg`
- `C:/Users/jinyi/AppData/Local/Temp/OpenCodeDesktop-WI004-evidence/workspace-changes.jpg`
- `C:/Users/jinyi/AppData/Local/Temp/OpenCodeDesktop-WI004-evidence/settings-connections.jpg`

### Repeatable review walkthrough

1. Launch the temporary copy with the commands above (or use a configured packaged development deployment). Enter Preview sample workspace; confirm the sample banner and Disconnected connection status.
2. Search/clear a project or session title, try a no-match query, collapse a project, change its sample worktree, and switch sessions after editing a draft. Right-click or Shift+F10 a session to rename/fork/archive; show archived to restore it. Create a new task and inspect its empty conversation. Cancel rename/delete once before confirming a sample operation.
3. Expand the recorded tool activity. Use Needs attention to reveal permission and question owners; try an empty form answer, then submit/cancel. Reset restores both requests for another trial.
4. Edit a multiline draft. Scroll the composer to References, model, agent & commands, add/remove a reference, change model/agent, insert a command/skill, and choose Queue or Steer submission mode. Inspect the Pending inputs expander, change delivery mode, cancel an input, and try Append locally. Select the Running session to exercise sample interruption.
5. Open the conversation's file reference; search/select numbered text, binary and missing fixtures. Open the change summary; compare Session turn, Working copy, and Branch (empty). Inspect Info expanders and save/cancel preview instructions.
6. Scroll each tab, change sessions, and return; resize Details by its grip or arrow keys, expand/restore, close/reopen it, and independently collapse Projects. Open/select/clear/reset/close/reopen terminal output. Check composer/conversation scrolling independently.
7. Visit every Settings section, try local controls, return, and check draft/session/panel/Details context. Repeat with Projects hidden using header Settings. Test 640 × 480 outer minimum, overlays/light dismissal, keyboard traversal and focus recovery, long paths/diffs, light/dark/high contrast, display scaling, and enlarged text.
8. Exit preview to restore disconnected empty state; re-enter/reset for another review.

### Verification limitations recorded before acceptance

The unchecked combined criteria above are intentionally not claimed as fully passed: remaining checks include the complete session-menu/composer/form/file/comparison interaction matrix, terminal selection/reopen, all scroll/focus restoration paths, pointer/keyboard pane resize, minimum-window overlays, full keyboard and assistive-technology traversal, light/high-contrast appearance, scaling/enlarged text, and packaged activation. These limitations were recorded before user-accepted closure below and remain documented; unperformed checks have not been converted to passes.

### User correction: direct sample-backed startup (2026-10-24)

The user rejected a separate sample mode and requested fictional data unrelated to their projects. Updated the scope and requirements overview accordingly. Removed the sample-mode banner, entry/exit/reset controls and handlers, mode flag, and reset/exit model APIs. The app now opens directly with populated projects and a selected session; connection status remains `Disconnected · Sample data`. Restarting creates fresh in-memory fixtures. Terminal Clear/Reset output remain terminal operations, not mode controls.

Renamed the App state/fixtures to `SampleWorkspace` / `SampleFixtures` and the shared view helpers to `WorkspaceControls`. Projects are now **Cinder Orchard** and **Lunar Postbox**, with invented `C:\Samples\...` paths, recipe/delivery task titles, recipe conversation/code, `RecipeList.xaml` / `RecipeFilter.cs` references and matching diffs. The user's project names and source filenames are not sample project content. Updated tests to assert populated startup and independent new-workspace fixtures instead of mode transitions.

- `dotnet build OpenCodeDesktop.slnx -p:Platform=x64 -t:Rebuild -v:minimal`: passed after the correction, **0 warnings, 0 errors**.
- `dotnet test --project tests/OpenCodeDesktop.Presentation.Tests/OpenCodeDesktop.Presentation.Tests.csproj`: **5 passed, 0 failed, 0 skipped**.
- Built the separate verification copy with the previous unpackaged/self-contained command, replacing its output directory with `C:/Users/jinyi/AppData/Local/Temp/OpenCodeDesktop-WI004-Samples/`; build passed with **0 warnings, 0 errors**. Launched that directory's `OpenCodeDesktop.exe` directly.
- Runtime confirmed immediate selection of **Add recipe tags**, both fictional project groups, sample path/conversation, two attention requests, and no entry/exit/reset-mode toolbar. Opening the conversation change summary selected the matching recipe diff in Details.
- Current startup screenshot: `C:/Users/jinyi/AppData/Local/Temp/OpenCodeDesktop-WI004-Samples-evidence/startup.jpg`. Earlier screenshots and mode-transition observations above describe the superseded implementation.

Current walkthrough starts by launching the app directly into its sample-backed workspace. Exercise the project/session/composer/Details/terminal/Settings interactions described in Scope; restart the app when fresh requests or fixtures are needed. The previous walkthrough's entry/exit/reset steps no longer apply. The remaining manual acceptance limitations above are unchanged; this correction does not establish those checks or user acceptance of the whole work item.

### User adjustments: footer Settings and pane-only Details motion (2026-10-24)

- Removed the duplicate workspace-header Settings button. Settings remains in the Projects footer; reopening Projects makes it reachable when the sidebar is hidden. Updated the requirements, scope, acceptance wording, and design system to reflect that choice.
- Investigated the bundled WinUI `2.3.9` `Themes/generic.xaml`. Its `DefaultSplitViewStyle` transitions from Closed to OpenInlineRight and back animate `ContentTransform.TranslateX` as well as `PaneTransform` and pane clipping. The main workspace was being translated by the native template, not rebuilt by `Details_Click`.
- Added a narrowly scoped `PaneOnlySplitView` for Details. Its template hook removes only transition timelines targeting `ContentTransform` from that instance. Native pane animation, inline width allocation, overlay/light dismissal, focus controls, and the existing content tree remain. Projects continues using an ordinary SplitView. The design system records the internal template-name dependency to recheck when upgrading WinUI.
- `dotnet build OpenCodeDesktop.slnx -p:Platform=x64 -t:Rebuild -v:minimal`: passed, **0 warnings, 0 errors**. This changes only App presentation; no state-model or Core/Infrastructure behavior changed, so no additional unit test run was needed.
- Built and launched a separate temporary copy using the documented unpackaged/self-contained command with output directory `C:/Users/jinyi/AppData/Local/Temp/OpenCodeDesktop-WI004-PaneMotion/`; build passed with **0 warnings, 0 errors**.
- Runtime checked the header containing Projects/Details/Terminal only, entered a draft, closed/reopened Details, and confirmed the same draft and session content remained. Main content reflows to the available width without the template's content-translation animation. Verified that footer Settings still opens Connections. Frame-by-frame animation timing and the previously outstanding narrow/theme/scaling/accessibility matrix remain manual acceptance work.
- Screenshot: `C:/Users/jinyi/AppData/Local/Temp/OpenCodeDesktop-WI004-PaneMotion-evidence/details-reopened.jpg` (outside the repository).

### Completion (2026-10-24)

The user explicitly requested marking this work item Done after reviewing the sample-backed workspace and requesting the implemented corrections: direct sample-data startup with fictional projects/text, footer-only Settings, and pane-only Details motion. This request accepts closure with the verification limitations recorded above. Moved the item to the completed index dated 2026-10-24; retained its filename, ID, evidence, and unchecked criteria for unperformed checks.

Delivered the interactive workspace, Details tabs, composer/session interactions, terminal samples, all six Settings sections, App-only sample models/fixtures, presentation tests, and updated design documentation. Latest x64 solution build passed with 0 warnings and 0 errors; the presentation suite passed all 5 tests. This closure is a documentation-only update; no build or tests were rerun. WI-002 is next and has not been started.
