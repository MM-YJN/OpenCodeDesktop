# WI-013 Polish the application shell and implemented feature UX

## Details

- Type: Implementation
- Related requirements: [Workspace layout](../requirements/overview.md#workspace-layout), [Interaction and reliability](../requirements/overview.md#interaction-and-reliability-requirements), and implemented connection, project, session and task workflows
- Priority: P0
- Dependencies: [WI-012](WI-012-persist-connections-workspace.md), which follows [WI-011](WI-011-multi-project-support.md); existing shell and live workflows delivered through WI-010
- Status: See the [completed work item index](completed.md)

## Goal

Make OpenCode Desktop look beautiful, cohesive and professional, and make its implemented workflows comfortable for daily use. Replace the rough collection of controls with a deliberate native Windows workspace whose hierarchy, spacing, typography and interaction states are consistent from connection setup to task completion.

## Design references and baseline

Use both repository concept images as the primary visual references:

- [Desktop workspace concept](../requirements/desktop-concept.png): balanced project/session navigation, a compact task header, generous readable conversation space, structured tool/permission cards, a unified composer, restrained teal accents, subtle surface boundaries and a persistent status strip.
- [Server connections concept](../design/server-connections-concept.png): a compact server switcher, clear active/inactive connection cards, aligned editor fields, distinct primary actions and contextual switch feedback.

Match their visual hierarchy, composition and level of finish while preserving the native Windows title bar and adapting to light, dark and high-contrast themes. Illustrative paths, server ports, model names, metadata and unimplemented controls are not product contracts. The [current workspace requirements](../requirements/overview.md#workspace-layout) and verified behavior take precedence where a concept differs; in particular, WI-012 replaces the connection concept's transient-password explanation.

Reuse and refine [the design system](../design/design-system.md), `Themes/ShellResources.xaml` and `Views/WorkspaceTemplates.xaml`. The design-system document still describes removed sample surfaces: reconcile those descriptions with the actual live implementation as part of this item. Inspect the running app at implementation start; XAML inspection alone does not establish visual defects or successful polish.

## Scope

- Audit the running shell and all implemented feature surfaces, capture baseline screenshots outside the repository, and record a concise list of concrete visual/usability problems. Establish shared semantic resources for typography, spacing, surfaces, borders, corner radii, icons, selection, focus and status before applying them consistently.
- Refine the shell's proportions, panel boundaries, header density, status strip and responsive layouts. Give the conversation and composer clear visual priority. Keep Projects/Details toggles, Settings navigation, pane resizing and expand/restore discoverable; animate Details without sliding or reloading the central content.
- Refine the connection switcher, Settings navigation, connection cards/editor, local environment discovery, save/connect/switch/disconnect and restoration states. Make active connection, connection progress, credentials guidance and validation obvious. Align labels/fields and give each context a clear primary action.
- Refine the per-connection project list, project/session selection, add/remove actions, session creation and pagination. Distinguish project groups from sessions and active selection from hover. Handle long paths, duplicate display names and long session titles with wrapping or ellipsis plus access to the full value.
- Improve conversation readability through message hierarchy, measured line lengths, selectable text, readable code/monospace content and structured rendering of already supported tool activity and errors. Keep long or streaming content usable without excessive layout churn or forced loss of the user's reading position.
- Compose draft input, model/agent selectors, send/interrupt actions and supported task controls into a cohesive composer. Make enabled, busy and unavailable states understandable. Preserve multiline input, drafts and existing send behavior; no shortcut changes are implied by concept annotations.
- Style permission requests and question forms as clear actionable surfaces with scope, validation, cancellation/expiry and pending-attention access. Keep connection/recovery state distinct from task execution and uncertain delivery. Group secondary diagnostics and retry actions so routine use is not dominated by implementation details.
- Design intentional first-launch, empty-project/session, loading, restoring, disconnected, stale, error and unavailable-feature states with concise task-oriented guidance. Remove obsolete sample/increment wording from active UI. Keep unsupported Details/terminal/settings capabilities honestly unavailable while making those surfaces visually consistent.
- Preserve focus, selection and drafts across Settings navigation, pane changes and asynchronous refreshes. Use accessible native controls, consistent icons with labels/tooltips, keyboard reachability, visible focus and meaningful live announcements. Respect system theme, contrast and reduced-motion preferences.

## Boundaries and exclusions

This is visual and interaction refinement of delivered features, including WI-011/WI-012 by execution time. Preserve module boundaries, connection ownership, atomic switching, credential protection, cancellation and authoritative recovery behavior. Keep presentation changes in App; change Core/Infrastructure only when a demonstrated usability fix requires it and add appropriate behavioral coverage.

Excluded: new backend operations, new session-management features, working file/diff/terminal integrations, attachments, new Markdown dialects, theme-preference persistence, fabricated production content, a web-based UI rewrite and a replacement product layout. Concepts may illustrate future features without authorizing their implementation. Controlled fixtures are acceptable for tests and visual evidence, with their origin identified; production onboarding remains honest.

## Deliverables

- A polished runnable shell and consistent presentation of all in-scope workflows, using shared resources and reusable controls/templates rather than isolated styling patches.
- Updated design-system documentation describing the actual components, visual rules and responsive/state behavior, with obsolete sample claims removed.
- A concise before/after visual walkthrough, screenshot paths outside the repository, an acceptance matrix and actual verification results recorded here. Compare the result with both concept references and explain material deviations.

## Acceptance criteria

- [x] The running app visibly follows both concept references in hierarchy, balanced spacing, typography, subtle surfaces, restrained accents, selection and action emphasis; user review confirms a cohesive professional finish.
- [x] Workspace, Projects, Settings/Connections, conversation, composer, permissions/forms and status/recovery surfaces share consistent resources and control states. There are no leftover rough diagnostic-control stacks or obsolete sample/increment messages in routine flows.
- [ ] Users can connect/discover a local service, switch connections, manage/select projects, create/select sessions, compose/send/interrupt supported tasks, respond to requests and recover from errors with clear primary actions and feedback.
- [ ] WI-012 startup restoration has clear progress and failure states; saved selections, project lists and protected-credential behavior remain correct.
- [ ] Empty, loading, busy, disconnected, stale/recovering, uncertain-delivery, failed and unavailable states are deliberate and truthful; successful-looking placeholder content is absent.
- [x] Light and dark themes are reviewed in the running app; high contrast retains readable text, controls, selection and focus without conveying status through color alone.
- [ ] At 1440x900 and the supported minimum 640x480 effective window sizes, plus 100%, 150% and 200% display scaling, essential actions remain reachable and text/composer/pending requests do not overlap or become clipped. Long paths/titles, multiline drafts, long histories and large supported tool output remain usable.
- [ ] Keyboard traversal, focus return after pane/dialog/navigation changes, accessible names and announcements work for the main workflows. Reduced-motion settings are respected.
- [ ] Pane changes, session refreshes and streaming preserve drafts and appropriate reading/focus context; Settings return and Details open/close do not recreate or animate the main conversation unexpectedly.
- [x] Relevant automated tests pass with nonzero discovery, the x64 solution builds, visual/manual evidence is recorded and required user visual acceptance is obtained.

## Verification requirements

Run relevant presentation and behavior tests for changed interactions and the x64 build. Add meaningful regression coverage for changed state/interaction logic; do not add tests that merely assert styling constants.

```powershell
dotnet test --project tests/OpenCodeDesktop.Core.Tests/OpenCodeDesktop.Core.Tests.csproj
dotnet test --project tests/OpenCodeDesktop.Infrastructure.Tests/OpenCodeDesktop.Infrastructure.Tests.csproj
dotnet test --project tests/OpenCodeDesktop.Presentation.Tests/OpenCodeDesktop.Presentation.Tests.csproj
dotnet build OpenCodeDesktop.slnx -p:Platform=x64
```

Manually review native UI against both concept images. Record a matrix covering themes, sizes/scales, keyboard/focus, long content and the state scenarios above. Capture before/after screenshots for onboarding, populated workspace/composer, Settings/Connections, a pending request and recovery/restoration, with theme, window size, scale and fixture/service prerequisites identified. Exercise live flows using an isolated compatible service and the existing local provider fixture where task execution is needed; do not require paid model access for visual review. Record exact commands, results and unperformed checks, and retain evidence outside the repository.

Automated tests and builds do not establish visual quality or accessibility acceptance. Once implementation is complete, leave this item Awaiting Acceptance until required native checks and user visual review are satisfied; unsupported criteria stay unchecked.

## Implementation plan (2026-10-25)

Baseline: the packaged app was launched against the current local service and inspected at 1440×900. Preliminary screenshots are retained outside the repository in `%TEMP%\opencode\wi013\baseline\`. The audit confirmed that the workspace is still mostly code-built stacks of raw native controls, and that Settings > Connections pushes its connection cards and editor below the fold at the target size.

Execution order:

1. Establish shared semantic resources and templates for typography, spacing, surfaces, borders, radii, icons, selection, focus, status, cards, list rows and state panels; make pane motion respect reduced-motion settings.
2. Rework the shell, persistent status strip, server switcher, Settings navigation, connection cards/editor and local-environment surface.
3. Rework project/session navigation, add-project flow and the workspace header, including long path/title handling.
4. Rework conversation rendering, the composer, pending permission/form cards and attention access; use incremental rendering to preserve scroll and focus.
5. Add the truthful Details surface, deliberate empty/loading/busy/stale/disconnected/uncertain/error states, and complete keyboard, contrast, theme, scaling and narrow-layout behavior.
6. Add presentation regression coverage, run the full verification commands, rewrite the design-system documentation, capture before/after evidence and leave the item Awaiting Acceptance.

Decisions:

- Presentation work stays in App. Core and Infrastructure change only if a demonstrated usability fix cannot be expressed through existing state.
- The project list shows the selected project's sessions; it does not fetch sessions for other projects just to populate the visual grouping.
- Non-connection Settings sections become honest unavailable surfaces instead of interactive sample controls. All routine "sample" and "increment" wording is removed.
- Details uses real session/server metadata in Info. Files, changes and terminal remain explicitly unavailable rather than fabricated.
- "New session draft" retains its current meaning and does not auto-create a server session. Ctrl+Enter and existing draft ownership remain.
- The system theme, minimum window size and existing connection/recovery/draft behavior remain. A compact custom title bar replaces the native caption row so the brand icon and title share one optical center; Windows still draws the caption buttons and owns drag, double-click maximize and the system menu. Initial user review found the native icon/title baseline misaligned, so the custom row is the accepted follow-up.

## Implementation evidence (2026-10-25)

Status: **Done** (2026-10-25). The user accepted the revised tree after the project-row alignment fix and explicitly requested completion. Automated verification and dark/light/high-contrast/640×480 visual captures pass; the remaining clean-session screen-reader and scaling checks are recorded as accepted verification limits below. The first user review's title-alignment, ellipse/capsule and status-color findings remain addressed.

Deliverables implemented:

- Expanded `Themes/ShellResources.xaml` with semantic surface, text, border, accent, status, spacing, radius, typography, icon, card, row, banner, composer and scroll resources. Views use these tokens instead of local color literals.
- Added `Views/ShellUi.cs` for reusable icon/label/row/badge/card/state-panel construction and added truthful presentation records for connection cards, task status, attention, composer state, conversation entries, details and settings sections.
- Rebuilt the shell, persistent status strip, storage/recovery banner, server switcher, Settings navigation, connection cards/editor, project/session navigation, task header, conversation, composer and pending-request surfaces.
- Added `DetailsView` with real Info metadata and explicitly disabled Files/Changes plus an unavailable terminal surface.
- Added incremental conversation rendering with ANSI cleanup, selectable text, structured tool cards, code blocks, warning/error cards and preserved reading position when appending or loading older messages.
- Added a 32 DIP custom title bar with a centered 16 DIP brand mark and 13 DIP semibold title, native caption buttons, drag/double-click behavior, and light/dark/high-contrast caption-button colors. Reduced-motion handling for pane transitions, theme resource mapping, compact state presentation at small heights, and accessible names/live regions/heading levels remain.
- Replaced circular connection indicators with reusable 8 × 8 rounded rectangles (2 DIP radius) in the status strip and server switcher. Green and amber come from Windows `SystemFillColorSuccess` and `SystemFillColorCaution`; the accent remains `SystemAccentColor`.
- Rewrote `docs/design/design-system.md` to describe the live components and remove obsolete sample claims. No routine `sample` or `increment` wording remains under `src/OpenCodeDesktop`.

### Verification commands

Ran from the repository root on 2026-10-25:

| Command | Actual result |
| --- | --- |
| `dotnet test --project tests/OpenCodeDesktop.Core.Tests/OpenCodeDesktop.Core.Tests.csproj` | Passed: 129 tests, zero failures. Five tree cases cover project-scoped loading, concurrent expansion, isolated failure/retry, inactive pagination and staged cross-project/draft selection. |
| `dotnet test --project tests/OpenCodeDesktop.Infrastructure.Tests/OpenCodeDesktop.Infrastructure.Tests.csproj` | Passed: 93 tests, four opt-in skips, zero failures. |
| `dotnet test --project tests/OpenCodeDesktop.Presentation.Tests/OpenCodeDesktop.Presentation.Tests.csproj` | Passed: 17 tests, zero failures. Two tree cases cover project/session/selection/full-path shape and draft/empty/load-more nodes without synthesized times/counts. |
| `dotnet build OpenCodeDesktop.slnx -p:Platform=x64` | Passed with zero errors; the final incremental build reported 33 analyzer/tooling warnings. A full rebuild reports 99 warnings across existing app/test files; none are emitted by the new tree files. This is not a clean-warning claim. |
| `dotnet build OpenCodeDesktop.slnx -p:Platform=x64 -p:GenerateAppxPackageOnBuild=true -p:AppxPackageSigningEnabled=false` | Produced the unsigned development MSIX used for native review. |
| `git diff --check` | Passed. |

### Native visual walkthrough

The packaged app was run with the registered development package. Screenshots and fixture metadata are retained outside the repository under `%TEMP%\opencode\wi013\`:

- Baseline before polish: `baseline\baseline-workspace.png`, `baseline\baseline-settings.png`.
- Final dark workspace/new draft: `after\final-titlebar-dark.png` (custom title bar).
- Final dark populated session: `after\final-session-dark2.png`.
- Final dark Settings/Connections: `after\final-settings-dark2.png` (rounded Active badge).
- Final light workspace/session: `after\final-titlebar-light.png`.
- Final 640×480 effective layout: `after\final-min-size.png` (light), `after\final-min-dark2.png` (dark).
- Pending question fixture: `after\final-pending.png`; answered state: `after\final-pending-answered.png`.
- Settings unavailable section: `after\settings-unavailable.png`.
- Add-project dialog: `after\add-project-dialog.png`.
- Server switcher: `after\server-switcher.png`.
- Persistence read failure: `after\persistence-error.png`.
- Final high contrast: `after\final-high-contrast.png`. The connected indicator is white system text, not dark-theme green. Earlier `after\high-contrast.png` is superseded.
- Tree follow-up dark: `after\tree-final-dark3.png`; expanded inactive project: `after\tree-expanded.png`; cross-project session selection: `after\tree-cross-project.png`.
- Tree row alignment after the review note: `after\tree-align-final.png` (unselected project rows) and `after\tree-align-project-selected.png` (selected/active project row).
- Tree follow-up light: `after\tree-final-light.png`.
- Tree follow-up high contrast: `after\tree-final-high-contrast2.png`.
- Tree follow-up 640×480 effective with the Projects overlay open: `after\tree-final-min-open.png`.

The pending-question screen was produced with an isolated pinned OpenCode 2.1.4 server and the repository's deterministic `docs/integration/task-provider-fixture.mjs` provider. The fixture created a local session with a pending question; no paid model access was used. The app auto-connected to the fixture, restored the session and rendered the question; entering and submitting an answer made the form disappear and the task status change to Completed. The fixture service and provider were stopped and the original package settings/credentials were restored afterwards.

### Review feedback follow-up (2026-10-25)

The first user visual review raised three specific items; all three were addressed before re-review:

1. **Title/icon alignment.** The native caption row put the title text and icon on visibly different baselines at the reviewed scale. It is replaced by a 32 DIP client-drawn title bar with a 16 DIP vector mark and 13 DIP semibold title sharing one centerline. Windows still draws Minimize/Maximize/Close and owns drag, double-click maximize and the system menu; caption colors follow light, dark and high contrast. Drag and maximize were exercised; caption restore was exercised.
2. **Ellipse/capsule shapes.** Connection indicators are now 8 × 8 rounded rectangles with a 2 DIP radius, not circles. Status badges (`Active`, `Running`, `Waiting for you`, `Completed`, queue/attention chips) now use a 6 DIP rounded rectangle (`ShellBadgeStyle`, `ShellCornerRadiusBadge`) instead of a fully rounded pill.
3. **Green/amber provenance.** `ShellConnectedBrush` maps to Windows `SystemFillColorSuccess` (green) and `ShellWarningBrush` maps to `SystemFillColorCaution` (amber), matching the concept's connected/attention indicators. They are separate from the system accent (purple on this machine), which remains `SystemAccentColor`. High contrast maps status to system window text; `ShellUi.Brush` now resolves the active theme dictionary so the connected indicator turns white in high contrast instead of retaining dark-theme green.

Verification after the follow-up: x64 build passed with zero errors; Core 129, Infrastructure 93 with 4 opt-in skips and Presentation 17 passed; dark, light, high contrast and 640×480 effective layouts were re-captured. The user accepted the tree follow-up after the alignment fix and requested completion.

### Project/session tree follow-up (2026-10-25)

The user requested that the Projects sidebar become a real hierarchical tree rather than project buttons with only the active project's session list. This follow-up is part of WI-013; it reopens the item for implementation and native acceptance.

- Use the native WinUI `TreeView` with project root nodes and session child nodes. Sessions are loaded lazily per project on expansion and cached in Core for the app lifetime.
- Each project row carries inline **New session** and **More** actions. New session selects that project's new-session draft without creating a server session. More contains Create session (active project), Refresh sessions, Load more sessions, Add project and Remove from list. The controls are rounded-rectangle icon buttons with accessible names and tooltips, not circled buttons.
- Project rows do not permanently repeat the server path; the full path remains available in the tooltip and accessible name, and the active path remains in the workspace header. Loading, empty and error text uses a compact second line.
- Expanding a project must not change the active workspace. Selecting a session under another project uses a staged navigation operation; failure or supersession retains the previous project, session, history and draft.
- The reference's relative session times and remaining-session counts are omitted because the current mapped session contract does not expose them. Paging is labelled "Load more sessions"; no values are synthesized.
- Implementation covers project-scoped session loading/cancellation in Core, a UI-independent tree presentation snapshot, node reconciliation that preserves expansion/selection/focus, keyboard activation semantics, and theme/high-contrast behavior.
- Verification must be repeated for the changed sidebar: multi-project expansion, cross-project selection, drafts, paging, removal/restoration, themes, 640×480, scaling, keyboard and screen-reader traversal.

### Tree implementation evidence (2026-10-25)

- Core now exposes per-project session snapshots (`SessionsLoaded`, `SessionsLoading`, `SessionsError`, `CanLoadMoreSessions`) and `LoadProjectSessionsAsync`/`RefreshProjectSessionsAsync` with project-scoped cancellation, cursor/ownership validation and failure isolation. Background expansion does not change the active project, history or draft.
- `OpenProjectSessionAsync` and `OpenProjectDraftAsync` extend the staged project-open path. A cross-project session selection validates the target directory/session/history before promotion; failure or supersession retains the previous workspace. Draft selection never creates a server session.
- `Presentation/ProjectTreePresentation.cs` provides the immutable project/session/draft/load-more snapshot. `Views/WorkspaceView.Tree.cs` reconciles native `TreeView` root/child nodes by stable key, preserving expansion, selection and focus; `Views/ProjectTreeRows.cs` supplies project, session, draft and paging rows.
- The project row includes inline New session and More actions with invocation suppression. New session selects a draft; More provides Refresh sessions, Load more sessions, Create session (active project), Add project and Remove from list. Project paths remain in tooltips/accessibility text; the active path remains in the workspace header.
- Reference relative timestamps and remaining-session totals remain omitted because the mapped session contract does not expose them. The paging action is labelled "Load more sessions"; no values are synthesized.
- Added Core coverage for on-demand/inactive expansion, concurrent loads, isolated failure/retry, inactive pagination and staged cross-project/draft selection. Added Presentation coverage for tree shape, active selection, full paths, draft/empty/load-more nodes and the absence of synthesized times/counts.
- Re-reviewed the packaged app at 200% display scaling with `ffmpeg` Desktop Duplication captures after BitBlt and PrintWindow returned blank WinUI surfaces in the current Windows session. Dark, light, high-contrast and 640×480 effective layouts were captured; the 640×480 layout uses the closed/overlay Projects pane as designed.
- Project and session row content is vertically centered within the native tree item so the folder/title centerline matches the expand/collapse chevron. Verified on an unselected project row and on the selected/active project row (`after\tree-align-final.png`, `after\tree-align-project-selected.png`).

### Acceptance matrix

| Criterion | Status | Evidence / limitation |
| --- | --- | --- |
| 1. Running app follows both concepts; user confirms cohesive finish | Checked | Final tree, dark/light and high-contrast screenshots were compared with the concepts. The sidebar shows projects as expandable roots with inline New session/More actions, draft and session children and a paging row; project-row content was vertically aligned to the native chevron after user review. The user accepted the result and requested completion. Material deviations are listed below. |
| 2. Shared resources/control states; no rough diagnostic stacks or sample wording | Checked | Shared resources and `ShellUi` are used across shell, Settings, Connections, the tree, header, conversation, composer, requests and Details. The tree uses `ShellProjectTreeItemStyle`, `ShellTreeActionButtonStyle` and shared row tokens; indicators/badges are rounded rectangles. Source search found no `sample`/`increment` strings in the App. |
| 3. Connect/discover/switch/manage/project/session/compose/send/interrupt/respond/recover | Partially observed | Connection restoration, server switcher, multi-project tree expansion, direct cross-project session selection, draft selection, project actions, composer controls, pending question answer and recovery/error banners were exercised. Native draft sending and interrupt were not driven end-to-end; existing Core/Infrastructure tests cover those workflows. |
| 4. WI-012 restoration progress/failure and saved selections/credentials | Partially observed | A seeded fixture auto-connected, restored its project/session and showed the pending question without password entry. Malformed settings produced the preservation/reset banner. The transient restoration progress state was not captured in a final screenshot. |
| 5. Empty/loading/busy/disconnected/stale/recovering/uncertain/failed/unavailable states are deliberate | Partially observed | Onboarding, empty project/session, project-row loading/error text, pending, completed, unavailable, disconnected and persistence-failure states were observed. Stale/recovering/uncertain states were inspected in running builds but not all were captured in a final-state matrix. |
| 6. Light and dark reviewed; high contrast readable without color-only status | Checked | Light and dark tree layouts were reviewed. High contrast was reviewed with white system text, project/session hierarchy, selection outline and inline actions; status is never conveyed by hue alone. |
| 7. 1440x900 and 640x480, 100/150/200% scaling; long content | Partially observed | 1440x900 and 640x480 effective layouts were reviewed at 200% display scaling. At 640×480 the Projects pane uses its designed overlay and remains scrollable. 100% and 150% scaling were not independently exercised; changing the per-monitor DPI registry value did not apply without a display restart. |
| 8. Keyboard, focus return, accessible names/announcements, reduced motion | Partially observed | Native focus, tree hierarchy, selected-node names, heading levels and live settings were inspected. Raw mouse/keyboard tree traversal and Enter activation worked. UIAutomation-driven keyboard traversal of the tree produced native `Microsoft.UI.Xaml.dll` fail-fast crashes in the current Windows session; screen-reader traversal must be rechecked after a clean session restart. Reduced-motion toggling and Narrator remain manual. |
| 9. Drafts/reading context across pane, session and streaming changes | Partially observed | Composer owner changes preserve the draft; conversation updates are keyed and incremental; Settings/Details do not recreate the workspace. Streaming focus/scroll behavior was not exhaustively exercised in the native app. |
| 10. Automated tests, x64 build, visual/manual evidence, user acceptance | Checked with accepted limits | Core 129, Presentation 17 and Infrastructure 93 (4 opt-in skips) pass; x64 build has zero errors. The final incremental build reports 33 warnings; a full rebuild reports 99 existing app/test warnings with none from the new tree files. Visual evidence is retained and the user accepted the revised build. Clean-session screen-reader traversal and 100/150% scaling remain accepted verification limits. |

### Material deviations from the concepts

- The application follows the Windows system accent (currently purple) rather than hard-coding the concept's teal. Green and amber status use Windows semantic success/caution values rather than the concept's exact swatches.
- Per the first user review, connection indicators are 8 × 8 rounded rectangles rather than the concept's circles, and status chips use a 6 DIP rounded rectangle rather than a fully rounded pill.
- The project pane shows all listed projects as tree roots and loads a project's sessions when it is expanded. This is the requested tree behavior rather than the concept's flat selected-project list. Relative session times and remaining-session counts from the user reference are omitted because the mapped contract does not expose them.
- Files, Changes and Terminal remain unavailable and explicitly labeled; no fabricated diffs, files or terminal output were added.
- Branch, attachments, reference/queue editing, provider sign-in and other concept illustrations are not implemented and are not presented as working.
- The title bar is a custom client-drawn 32 DIP brand row because the native title/icon baseline could not be aligned at the reviewed scale; system caption buttons, drag behavior and the system menu remain native.
- "New session draft" retains its current meaning; it does not auto-create a server session to mimic the concept's "New task" label.

### Accepted limitations at completion

- 100% and 150% display scaling were not reviewed; only 200% was available without changing and restarting the display configuration.
- OS reduced-motion, Narrator, complete keyboard traversal, context-menu/keyboard-only project removal and the full focus matrix remain manual.
- Native composer send/interrupt and permission Allow/Reject were not driven in the final fixture; the deterministic integration tests cover the underlying workflows.
- Some transient recovery/restoration states were observed but not captured in the final screenshot set.
- UIAutomation-driven keyboard traversal of the new tree caused repeated native `Microsoft.UI.Xaml.dll` fail-fast crashes (`0xc000027b`) in the current Windows session, while raw mouse and keyboard interaction worked. Crash dumps show native XAML/composition frames and not the app's `ItemInvoked` handler; this must be rechecked after a clean Windows session restart before screen-reader acceptance. Native screen-reader/keyboard-only project removal remains manual.
- In the current session, Windows high-contrast toggling left BitBlt/PrintWindow captures of all WinUI/UWP surfaces blank (including Calculator). The final tree captures use Desktop Duplication (`ffmpeg ddagrab`) and are valid; the environment itself should be restarted before concluding any remaining theme or rendering issue.
- The current environment's 200% display scale means a 1440x900 DIP window is physically 2880x1800; the 640x480 effective checks used a 1280x960 physical window.

## Completion and accepted verification limits (2026-10-25)

The user accepted the revised project/session tree after the project-row alignment fix and explicitly requested marking WI-013 Done. Completion is recorded with the limits above, especially the clean-session screen-reader/UIAutomation recheck and independent 100%/150% scaling review; those were not silently treated as verified.

The completed item records the final tree alignment evidence and the automated results: Core 129, Presentation 17, Infrastructure 93 with 4 opt-in skips, x64 build zero errors and the warning counts in the verification table. The active index was cleared, the completed row added under 2026, and the work-item status link now points to the completed index.

## Planning record (2026-10-24)

Definition only; implementation has not started. Scheduled after WI-012 to include the project-list and restart-continuity surfaces in one cohesive polish pass. Both repository concept images were inspected and linked at the user's request. No change to product scope or the requirements overview is needed for this work-item definition.

Documentation verification: `git diff --check` passed; local link-target and whitespace checks for WI-013 passed. No app build, behavior tests or native visual acceptance checks were run for this documentation-only change.
