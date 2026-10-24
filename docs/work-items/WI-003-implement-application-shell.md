# WI-003 Define design tokens and implement the application shell

## Details

- Type: Implementation
- Related requirements: Interaction and Reliability Requirements (keyboard navigation, accessibility, responsiveness, and separate connection/execution status); foundation for CON-01 and subsequent task workflows
- Priority: P0
- Dependencies: None
- Status: See the [work item index](README.md)

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

## Exclusions

No HTTP calls, connection validation or credentials, server discovery/startup, persistence of window/navigation preferences, project/session data, prompt composer, agent execution, event streams, file/review panels, notifications, custom branding assets, or generalized navigation/plugin framework. Connection functionality remains WI-002. This item establishes the shell rather than implementing the full task workspace.

## Deliverables

- A design-system document describing the visual direction, token catalog, shell regions, responsive behavior, and how feature views use the resources and content host.
- Shared App XAML resource dictionaries and only the reusable styles needed by this shell.
- A working startup shell with Workspace/Connection navigation, empty states, and disconnected status.
- Build evidence and recorded manual visual, interaction, theme, and accessibility checks in this item.

## Observable acceptance criteria

- [ ] The app launches into the Workspace empty state without a running server, with application identity, navigation, main content, and Disconnected status visible.
- [ ] Workspace and Connection navigation work with pointer and keyboard, show the selected destination, and keep connection status visible. The Connection placeholder clearly states that connection functionality is not yet implemented.
- [ ] Shared tokens cover semantic colors/brushes, typography, spacing, corner radii, and shell dimensions. Shell-authored repeated visual values use those resources; intentional exceptions are documented. The token catalog matches the implementation.
- [ ] Light, dark, and Windows high-contrast presentations remain readable, with visible focus and status conveyed beyond color. The app follows the system theme without requiring a saved preference.
- [ ] Resizing and collapsing/expanding navigation keep content reachable without overlap or clipped primary text at the documented minimum size. Checks at 100%, 150%, and 200% display scaling and enlarged system text are recorded.
- [ ] Window move, resize, minimize, maximize/restore, and close operate normally. Keyboard-only navigation can reach and activate both destinations and the pane toggle, with accessible names and logical focus order.
- [ ] WI-002 has a documented integration point for its connection view and connection-state presentation, without shell dependencies on transport or business logic.
- [ ] The x64 Windows App builds successfully, and manual checks include screenshots of both destinations and representative light, dark, high-contrast, and narrow-window layouts. Required checks that cannot be performed remain unchecked with their limitations recorded.

## Verification requirements

```powershell
dotnet build OpenCodeDesktop.slnx -p:Platform=x64
```

Launch the app without a server. Record navigation, pane toggle, keyboard/focus, accessible-name inspection, window controls, minimum-size behavior, scaling/text enlargement, and system-theme checks. Capture representative screenshots and record their repository or artifact locations. Inspect resource use against the design document and verify that no connection/network operation is introduced.

Core and Infrastructure tests are not required for an App-only presentation change. Add meaningful tests if implementation introduces independently testable behavior; do not count zero discovered tests as validation. Required manual checks must be completed before marking Done; use Awaiting Acceptance if implementation/build are complete but required manual confirmation remains.

## Planning decisions

- 2026-10-24: Establish design tokens and the shell before implementing the connection flow. WI-002 depends on this item and retains ownership of all connection behavior.
- Start with Workspace and Connection only. Later work items can extend navigation as their features become usable.

## Implementation and verification record

Not started. This item defines the next implementation increment; no shell code or manual verification has been completed.
