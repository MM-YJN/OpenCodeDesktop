# WI-003 Define design tokens and implement the application shell

## Details

- Type: Implementation
- Related requirements: Interaction and Reliability Requirements (keyboard navigation, accessibility, responsiveness, and separate connection/execution status); foundation for CON-01 and subsequent task workflows
- Priority: P0
- Dependencies: None
- Status: See the [completed work item index](completed.md)

## Goal

Replace the empty startup window with a usable native Windows application shell and a documented, reusable set of design tokens. Establish the layout and visual conventions before WI-002 adds the server connection flow.

## Scope and responsibilities

- App owns all shell views, presentation state, navigation, theme resources, and Windows window integration. Keep this increment within `src/OpenCodeDesktop`; no Core or Infrastructure behavior is needed.
- Define named XAML resources for semantic colors/brushes (surfaces, text, borders, accent, and status), typography, spacing, corner radii, and shell dimensions. Prefer WinUI theme resources and native control styles where appropriate. Document token names, values or platform mappings, intended uses, and extension conventions in `docs/design/design-system.md`.
- Support the Windows system theme with light, dark, and high-contrast resources. Use theme-aware resource references for colors; preserve native focus, disabled, hover, and pressed states. An in-app theme picker is outside this increment.
- Implement a single-window shell with application identity, native window controls, a collapsible navigation pane, a main content host, and a persistent connection-status region separate from future session execution status. Retain native title-bar behavior; a custom title bar is not required.
- Provide a Workspace destination with an empty state explaining that no server is connected, and a Connection destination with an explicit placeholder for the future WI-002 view. Navigation must switch the displayed content and identify the selected destination. Do not add destinations or controls for unimplemented session, project, or settings workflows.
- Initially show Disconnected, with no server, project, session, or model implied to be selected. The shell must launch and work without an OpenCode service. WI-002 will replace the Connection placeholder and supply real connection state.
- Define initial/minimum window sizes and the navigation collapse behavior during implementation and record them in the design document. Keep both destinations usable at the documented minimum size, at normal desktop sizes, and with display scaling/text enlargement.
- Use accessible names, logical keyboard navigation, visible focus, and text or icons alongside status colors. Prefer standard WinUI controls over custom control templates.
- Follow [the desktop concept](../requirements/desktop-concept.png) for layout: reserve project/session navigation in a Workspace-only panel separate from app navigation, a central header/conversation/bottom input area, and a collapsible right session-info pane. Use disconnected/empty placeholders; functional project/session/composer workflows remain later work.

## Exclusions

No HTTP calls, connection validation or credentials, server discovery/startup, persistence of window/navigation preferences, project/session data, functional prompt composer, agent execution, event streams, file/review panels, notifications, custom branding assets, or generalized navigation/plugin framework. Empty layout regions for future project/session content and composer input are included. Connection functionality remains WI-002. This item establishes the shell rather than implementing the full task workspace.

## Deliverables

- A design-system document describing the visual direction, token catalog, shell regions, responsive behavior, and how feature views use the resources and content host.
- Shared App XAML resource dictionaries and only the reusable styles needed by this shell.
- A working startup shell with Workspace/Connection navigation, empty states, and disconnected status.
- Build evidence and recorded manual visual, interaction, theme, and accessibility checks in this item.

## Observable acceptance criteria

- [x] The app launches into the Workspace empty state without a running server, with application identity, navigation, main content, and Disconnected status visible.
- [ ] Workspace and Connection navigation work with pointer and keyboard, show the selected destination, and keep connection status visible. The Connection placeholder clearly states that connection functionality is not yet implemented.
- [x] Shared tokens cover semantic colors/brushes, typography, spacing, corner radii, and shell dimensions. Shell-authored repeated visual values use those resources; intentional exceptions are documented. The token catalog matches the implementation.
- [ ] Light, dark, and Windows high-contrast presentations remain readable, with visible focus and status conveyed beyond color. The app follows the system theme without requiring a saved preference.
- [ ] Resizing and collapsing/expanding navigation keep content reachable without overlap or clipped primary text at the documented minimum size. Checks at 100%, 150%, and 200% display scaling and enlarged system text are recorded.
- [ ] Window move, resize, minimize, maximize/restore, and close operate normally. Keyboard-only navigation can reach and activate both destinations and the pane toggle, with accessible names and logical focus order.
- [x] WI-002 has a documented integration point for its connection view and connection-state presentation, without shell dependencies on transport or business logic.
- [x] The Workspace layout follows the concept's sidebar, header, scrollable conversation, bottom task-input region, and collapsible right session-info pane. Empty states do not imply selected data or working session actions; narrow windows use the inspector as an overlay.
- [x] Projects is a separate Workspace-only panel, not custom content in app navigation. Switching to Connection hides it; it closes independently, and its content retains a fixed width during transitions.
- [ ] The x64 Windows App builds successfully, and manual checks include screenshots of both destinations and representative light, dark, high-contrast, and narrow-window layouts. Required checks that cannot be performed remain unchecked with their limitations recorded.

## Verification requirements

```powershell
dotnet build OpenCodeDesktop.slnx -p:Platform=x64
```

Launch the app without a server. Record navigation, pane toggle, keyboard/focus, accessible-name inspection, window controls, minimum-size behavior, scaling/text enlargement, and system-theme checks. Capture representative screenshots and record their external artifact locations; do not retain screenshots or automation dumps under `docs/design/evidence`. Inspect resource use against the design document and verify that no connection/network operation is introduced.

Core and Infrastructure tests are not required for an App-only presentation change. Add meaningful tests if implementation introduces independently testable behavior; do not count zero discovered tests as validation. The original completion gate required all manual checks before Done. The user's explicit completion request on 2026-10-24 accepts closure with the limitations recorded below; unperformed checks remain unchecked and are not reported as passes.

## Planning decisions

- 2026-10-24: Establish design tokens and the shell before implementing the connection flow. WI-002 depends on this item and retains ownership of all connection behavior.
- Start with Workspace and Connection only. Later work items can extend navigation as their features become usable.
- 2026-10-24: User requested alignment with the desktop concept and removal of repository evidence files. Prepare the concept's layout regions with empty states, without implementing the illustrated feature workflows. Keep future screenshots outside the repository.
- 2026-10-24: After reporting project text wrapping vertically during app-navigation collapse, the user requested a separate panel shown only in Workspace. App navigation now contains destinations only; project/session content lives in a fixed-width, independently collapsible Workspace panel. This replaces the earlier sidebar embedding and its pane-event visibility workaround.

## Implementation and verification record

### Implementation (2026-10-24)

- Added [the design system](../design/design-system.md), including the complete token catalog, native control conventions, responsive thresholds, sizing, and WI-002 integration guidance.
- Merged `Themes/ShellResources.xaml` after WinUI control resources. Explicit Light, Dark, and HighContrast dictionaries provide semantic brushes; shared styles cover wrapped text and empty-state cards without replacing native control templates.
- Replaced the empty startup window with native Workspace/Connection navigation, a content host, and a persistent text-based Disconnected status outside navigation. Added separate `WorkspaceView` and `ConnectionView` controls; only presentation code changed in App. No network, process, persistence, or business behavior was introduced.
- Initial client size: 1440 x 900 DIPs. Preferred outer minimum: 640 x 480 DIPs. App navigation uses a 48-DIP compact rail and a 280-DIP label overlay. Minimum dimensions are scaled to physical pixels and refreshed when monitor scaling changes. Native title bar and window controls are retained.
- Added the concept's central header/conversation/task-input rows, a Projects sidebar reservation, and a 320-DIP session-info pane. The inspector opens inline at 1000 workspace DIPs and uses a closed-by-default overlay below that threshold. Its toggle is native; its Close action returns focus to the toggle. All feature data remains explicitly unselected/unavailable.
- App navigation no longer uses minimal mode: its toggle stays in the compact rail, with no heading inset or pane-visibility event handlers. Workspace owns a 240-DIP Projects panel, inline at 1000 Workspace DIPs and an overlay below that threshold. Its fixed content width prevents reflow during transitions; its Close action restores focus to Projects.
- App navigation starts as a closed compact rail, while Workspace's panels use their own responsive defaults. Startup assigns programmatic focus to Workspace; subsequent focus visuals and keyboard navigation use native WinUI behavior.

### Build and source verification

- `dotnet build OpenCodeDesktop.slnx -p:Platform=x64`: passed on 2026-10-24, **0 warnings, 0 errors**, after all App source changes.
- `git diff --check`: passed. Inspected the shared resource catalog against its XAML definitions and usage; typed spacing aliases and structural/native-control exceptions are documented.
- Inspected authored App source for transport, file/process operations, theme overrides, and custom title-bar behavior: none introduced. No OpenCode server was started or used for verification; shell startup does not consult one.
- Core/Infrastructure tests were not run: this increment adds only App presentation behavior and does not change either library. The solution build compiles both existing test projects; compilation is not a test pass.

### Launch environment and observed interaction checks

Directly launching the normal build executable failed before App startup with Windows App Runtime activation error `0x80040154 (REGDB_E_CLASSNOTREG)`, reported by the Windows Application/.NET Runtime event logs. A standalone executable launch does not establish packaged deployment support. To inspect the UI without installing runtime packages or changing repository packaging settings, built a temporary self-contained, unpackaged verification copy:

```powershell
dotnet build src/OpenCodeDesktop/OpenCodeDesktop.csproj -p:Platform=x64 -p:WindowsPackageType=None -p:WindowsAppSDKSelfContained=true -p:OutputPath=C:/Users/jinyi/AppData/Local/Temp/OpenCodeDesktop-WI003/
```

This also passed with **0 warnings, 0 errors**. `OpenCodeDesktop.exe` in that output directory launched successfully using the Windows computer-use helper. The original observations below came from the shell before concept alignment, not an installed MSIX package. They do not establish acceptance of the revised layout.

| Check | Actual result |
| --- | --- |
| Startup without server interaction | Passed: Workspace empty state, native application title, navigation, and Disconnected text visible. |
| Pointer navigation | Workspace/Connection content switches; native selection indicator follows the destination; connection status remains visible. |
| Keyboard activation | Down then Enter from startup selected Connection and displayed its placeholder, with a visible focus outline. Full Tab/Shift+Tab traversal and keyboard activation of both destinations and the pane toggle remain unverified. |
| Pane toggle | Pointer toggle expanded the pane from its collapsed rail. Final startup opens expanded at normal width. Full compact/minimal resize behavior remains unverified. |
| Accessible names | Automation tree exposes Workspace, Connection, native localized open/close navigation toggle names, and `Server connection: Disconnected`. Headings are authored with heading levels. Screen-reader announcements and full focus order remain unverified. |
| Current system appearance | Dark presentation visually readable in both destinations, with status expressed in text. |
| Native close | Closing the temporary copy allowed it to be rebuilt and relaunched. Move, minimize, maximize/restore, and constrained resizing remain unverified. |

The previously saved screenshots and accessibility dumps under `docs/design/evidence` were removed at the user's request on 2026-10-24. Their observations are summarized above; no links to removed artifacts are retained.

### Concept layout verification before Workspace-panel revision (2026-10-24)

- Both the normal x64 solution build and the temporary self-contained App build passed with **0 warnings, 0 errors** after concept alignment.
- The helper's launch API failed with `accessibility window-opened handler did not become ready`, including after resetting its JavaScript session. Launching the self-contained executable directly with PowerShell `Start-Process` succeeded; the Windows helper could then select that returned window and capture its layout. Accessibility-tree capture remained unavailable in this run.
- At the initial 1440 x 900 client dimensions, visually confirmed the expanded project/session sidebar, workspace header, conversation empty state, bottom task-input reservation, and inline session inspector. Disconnected status remains separate and visible; no sample project, model, session, activity, or usage is fabricated.
- Used the native system-menu Size command to reduce window width: observed compact navigation, a full-width central workspace, and the inspector closing automatically. The Session info toggle opened its right overlay, and Close dismissed it. The bottom task-input region stayed anchored while the main conversation region absorbed the available space.
- Resized down to the native minimum constraints; observed wrapped primary text and visible bottom task-input/status regions. Found and corrected the minimal-mode pane-toggle/heading overlap with the content inset above. After rebuilding both outputs, relaunched and rechecked the minimum-width layout: the heading and native toggle are now clearly separated.
- No screenshots or automation dumps from this run were saved to the repository. Verification images were inspected inline only. Theme, scaling, assistive-technology, and full keyboard acceptance remain outstanding.

### Workspace-only Projects panel verification (2026-10-24)

- Replaced app-navigation custom content with a separate Projects `SplitView` owned by `WorkspaceView`. Its fixed 240-DIP content width avoids the reported narrow-column reflow during collapse. The earlier pane-event workaround and minimal-mode content inset are no longer needed and were removed.
- `dotnet build OpenCodeDesktop.slnx -p:Platform=x64` passed with **0 warnings, 0 errors**. The temporary self-contained build, using the previous command with output directory `C:/Users/jinyi/AppData/Local/Temp/OpenCodeDesktop-WI003-Collapse/`, also passed with **0 warnings, 0 errors**.
- Launched that separate copy, leaving existing user instances alone. Visually checked the initial compact app rail and Workspace Projects panel, expanded app navigation, selected Connection (Projects absent), returned to Workspace (Projects restored), and closed Projects independently. The main task content and disconnected status remained visible.
- Projects has its own header toggle and Close control; its responsive inline/overlay threshold is 1000 DIPs of Workspace width. Inspector sizing uses the width remaining after Projects. Full narrow-window, text-scaling, and keyboard checks for the revised panel remain outstanding; earlier minimum-mode observations above describe the previous layout.
- Inspected screenshots inline only; no repository evidence files were created. No Core/Infrastructure behavior changed and no new unit tests were needed for this presentation adjustment.

### Recorded verification limitations at completion

The original automation run was interrupted during window sizing, and keyboard focus metadata was inconsistent with the visible outline. Concept verification above subsequently exercised sizing and the inspector using screenshot-based actions; full keyboard/focus verification remains incomplete. Light/high-contrast checks and display/text-scaling checks are still outstanding. System display/text settings were not changed.

The following checks were not completed before user-accepted closure. Future verification can use a configured packaged development environment (or the temporary self-contained copy for presentation checks):

- Full keyboard-only traversal and activation of both destinations and the pane toggle, logical focus order, and visible focus.
- Native move, resize, minimize, maximize/restore, and close; both views at the documented minimum size, with pane collapse/expansion and no clipped primary text.
- Concept alignment at normal and narrow widths: the three regions, independent conversation scrolling, bottom input reservation, inspector toggle/close, and keyboard focus recovery.
- Both destinations at 100%, 150%, and 200% display scaling, enlarged Windows text, and a monitor-scale transition where available.
- System light, dark, and high-contrast themes, including live changes, readable borders/text, focus, and status; capture representative light, high-contrast, and narrow layouts.
- Assistive-technology confirmation of names, headings, and focus order. Record any remaining environment limitation explicitly.

### Completion (2026-10-24)

The user explicitly requested marking WI-003 Done after reviewing the shell and the Workspace-only Projects panel adjustment. Recorded user acceptance and moved the item to the completed index with completion date 2026-10-24. Delivered the shell, shared theme resources, concept-aligned layout, and design-system documentation; the final x64 build passed with 0 warnings and 0 errors. The verification limitations above remain documented, and unperformed combined acceptance checks remain unchecked rather than being represented as successful tests. WI-002 is next and has not been started.
