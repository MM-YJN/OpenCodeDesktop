# OpenCode Desktop design system

This initial visual foundation is delivered by [WI-003](../work-items/WI-003-implement-application-shell.md). It covers the disconnected application shell. Server connection and task workflows extend it in later work items.

The revised [desktop concept](../requirements/desktop-concept.png) and [workspace requirements](../requirements/overview.md#workspace-layout) supersede the application-navigation and single-inspector layout described below. [WI-004](../work-items/WI-004-implement-interactive-workspace-layout.md) will remove the rail, move Connections and Extensions into Settings, and introduce the interactive tabbed Details pane. Until that item is implemented, the token catalog and implementation descriptions below describe the existing WI-003 shell. WI-004 must update them to match the delivered layout.

## Visual direction

Use [the desktop concept](../requirements/desktop-concept.png) as the layout reference: a left project/session sidebar, a central task workspace with header, conversation, and bottom input area, and a right session inspector. Use native WinUI controls, system typography, quiet layered surfaces, and restrained borders. Content headings establish hierarchy; explanatory text wraps rather than truncates. The native title bar supplies application identity and standard window controls. No custom branding or control templates are introduced.

The shell prepares these regions with explicit disconnected/empty states. Project/session trees, model/agent selectors, messages, tool cards, permission requests, an editable composer, usage metrics, and Settings become real controls only when their behavior is implemented. The concept's sample project, session, provider, model, costs, and execution status are not application defaults.

The application follows the Windows system theme (no `RequestedTheme` override or saved theme preference). All authored brushes are accessed with `ThemeResource`. Light and dark colors map to WinUI semantic resources; explicit high-contrast overrides use Windows system colors. Native controls retain their own focus, hover, pressed, selected, and disabled states.

## Resource catalog

Resources live in `src/OpenCodeDesktop/Themes/ShellResources.xaml`, merged after `XamlControlsResources` in `App.xaml`. Dimensions below are effective pixels (DIPs), except where noted.

### Semantic brushes

| Token | Light/dark platform color | High-contrast system color | Use |
| --- | --- | --- | --- |
| `ShellSurfaceBrush` | `SolidBackgroundFillColorBase` | `SystemColorWindowColor` | Shell background |
| `ShellContentSurfaceBrush` | `LayerFillColorDefault` | `SystemColorWindowColor` | Empty-state card surface |
| `ShellStatusSurfaceBrush` | `SolidBackgroundFillColorBase` | `SystemColorWindowColor` | Persistent connection region |
| `ShellTextPrimaryBrush` | `TextFillColorPrimary` | `SystemColorWindowTextColor` | Headings and body |
| `ShellTextSecondaryBrush` | `TextFillColorSecondary` | `SystemColorWindowTextColor` | Supporting explanation |
| `ShellBorderBrush` | `CardStrokeColorDefault` | `SystemColorWindowTextColor` | Card and region boundaries |
| `ShellAccentBrush` | `SystemAccentColor` | `SystemColorHotlightColor` | Future feature emphasis; native navigation uses its own accent states |
| `ShellDisconnectedBrush` | `TextFillColorSecondary` | `SystemColorWindowTextColor` | Neutral disconnected status, always accompanied by text |

Only Disconnected is represented in this increment. Add other semantic status tokens with actual feature behavior; do not imply a successful connection or active task through decoration.

### Typography and styles

Font family, weight, and automatic system text scaling come from the corresponding native WinUI text styles.

| Token/style | Value or platform base | Use |
| --- | --- | --- |
| `ShellTitleFontSize` / `ShellTitleTextStyle` | 28 / `TitleTextBlockStyle` | Destination title (accessibility heading level 1) |
| `ShellHeadingFontSize` / `ShellHeadingTextStyle` | 20 / `SubtitleTextBlockStyle` | Empty-state title (heading level 2) |
| `ShellBodyFontSize` / `ShellBodyTextStyle` | 14 / `BodyTextBlockStyle` | Wrapped body and status text |
| `ShellSecondaryTextStyle` | Body style with secondary brush | Supporting explanation |
| `ShellCardStyle` | Content surface, border, radius, and padding tokens | Empty-state container |

### Spacing, shape, and dimensions

| Token | Value | Use |
| --- | --- | --- |
| `ShellSpaceSmall` | 8 | Header/inspector label and value spacing |
| `ShellSpaceMedium` | 16 | Empty-state text spacing |
| `ShellSpaceLarge` | 24 | Destination section spacing |
| `ShellContentPadding` | 24 on all sides | Scrollable destination padding |
| `ShellCardPadding` | 24 on all sides | Empty-state inner padding |
| `ShellStatusPadding` | 16 horizontal, 8 vertical | Connection region padding |
| `ShellSidebarPadding` | 16 horizontal, 24 vertical | Project/session sidebar empty state |
| `ShellWorkspaceHeaderPadding` | 24 horizontal, 16 vertical | Workspace header |
| `ShellComposerMargin` | 16 on all sides | Reserved bottom task-input region |
| `ShellHairlineThickness` | 1 on all sides | Card border |
| `ShellStatusBorderThickness` | 0,1,0,0 | Status top divider |
| `ShellHeaderBorderThickness` | 0,0,0,1 | Workspace header divider |
| `ShellInspectorBorderThickness` | 1,0,0,0 | Session inspector divider |
| `ShellProjectsBorderThickness` | 0,0,1,0 | Workspace Projects panel divider |
| `ShellCardCornerRadius` | 8 | Empty-state card corners |
| `ShellContentMaximumWidth` | 720 | Readable destination content width, including outer padding |
| `ShellWindowInitialWidth` / `ShellWindowInitialHeight` | 1440 / 900 | Initial client area, showing the three concept columns |
| `ShellWindowMinimumWidth` / `ShellWindowMinimumHeight` | 640 / 480 | Minimum outer window dimensions |
| `ShellNavigationWidth` | 280 | App navigation overlay with destination labels |
| `ShellNavigationCompactWidth` | 48 | Compact navigation rail |
| `ShellProjectsPanelWidth` | 240 | Fixed content width of the Workspace-only project/session panel |
| `ShellProjectsInlineThreshold` | 1000 | At or above this Workspace width, open Projects inline by default |
| `ShellInspectorWidth` | 320 | Session-info pane, bounded by available workspace width |
| `ShellInspectorInlineThreshold` | 1000 | At or above this workspace width, show the inspector inline by default |

Thickness tokens intentionally repeat spacing-unit values: XAML requires typed `Thickness` resources, while `StackPanel.Spacing` requires a double. Structural grid star/auto sizing, status row index, heading levels, and native control internals are not design tokens. Do not replace native templates just to match shell spacing values.

## Shell regions and responsive behavior

- Native title bar: application identity and Windows window controls.
- `ShellNavigation`: a compact app-navigation rail containing Workspace and Connection only. Its native toggle opens destination labels in an overlay. It contains no project/session content, so collapsing it cannot squeeze project text into the rail. Settings and back navigation remain hidden.
- `ProjectsLayout`: a left-hand native `SplitView` inside `WorkspaceView`, containing the project/session empty state in `ProjectsRegion`. The panel is part of Workspace and disappears when Connection is selected. The panel's content keeps a fixed 240-DIP width while the pane animates, preventing text reflow during collapse.
- `ShellContentHost`: the selected destination's view. Connection retains its bounded, scrollable setup placeholder. Workspace fills the available height and width with the regions below.
- `WorkspaceLayout`: a native right-hand `SplitView` separates the central workspace from session info. The central grid has an auto-height header, a flexible vertically scrollable conversation region, and an auto-height bottom task-input region. These rows let future messages scroll independently of the header and composer.
- Workspace header: destination identity, explicit no-project/no-session state, and independent Projects and Session info toggles. Closing either panel returns focus to its toggle. Later session title/path and model/agent/execution controls belong here; connection readiness stays separate.
- Task-input region: a styled, noninteractive reservation for the future composer. It explains why input is unavailable, with no send, attach, or model actions implying implemented behavior.
- Session-info pane: scrollable session, provider/model/agent, context, and activity sections. Values explicitly show that no session/model/provider/agent is selected. No fabricated counts, token usage, cost, or system prompt are shown.
- Connection status: a separate auto-height bottom row outside navigation. It stays visible across destinations and when the pane is expanded or collapsed. The full text is `Connection: Disconnected`; color alone never carries state. It is a polite accessibility live region for future state changes.

`NavigationView` uses `LeftCompact` at all widths. It starts closed as a 48-DIP icon rail; opening the destination labels overlays the content rather than resizing it. Selecting a destination closes the overlay. The toggle stays inside the rail and cannot overlap a destination heading; no minimal-mode content inset is needed.

Projects defaults to inline/open at 1000 DIPs of Workspace width after the app rail. Below this threshold it defaults to closed/overlay, accessible through the Projects toggle. The panel remains independently collapsible at desktop sizes. Crossing the threshold chooses the responsive default; resizing within the same range preserves the user's choice. Neither switching destinations nor collapsing app navigation changes project content width.

At 1000 DIPs of workspace width remaining after the Projects panel, session info defaults to inline/open, leaving at least 680 DIPs for the central workspace. Below that threshold it defaults to closed/overlay so the central content retains its width. The native Session info toggle can open or close it at either size. Pane width is capped at 320 DIPs and leaves at least the compact-rail width of workspace visible. Crossing the threshold resets the pane to its appropriate default; ordinary resizing within the same range preserves the user's open/closed choice. Both project and inspector panels can scroll with enlarged text.

Startup puts programmatic focus on the selected Workspace destination. Keyboard focus visuals and subsequent Tab/arrow-key navigation remain native WinUI behavior.

Initial client dimensions and outer minimum dimensions are converted to physical pixels using `XamlRoot.RasterizationScale`. Minimum constraints are refreshed when that scale changes (for example, moving between monitors). Standard `OverlappedPresenter` constraints preserve native resizing; no resize interception or custom title bar is used. Destinations scroll vertically when enlarged text exceeds the available height. Text scaling remains enabled, including in the status strip.

## WI-002 integration and extension conventions

`MainWindow` creates `WorkspaceView` and `ConnectionView` once and switches `ShellContentHost.Content` from `NavigationView.SelectionChanged`. There is no navigation service or business state in the shell. WI-002 replaces the contents of `Views/ConnectionView.xaml` and connects an App presentation model to its connection use case. This view is the integration point, not a second window or another destination.

Later task work replaces the Workspace conversation empty state and task-input reservation in their existing grid rows. Project/session navigation replaces `ProjectsRegion`; session metadata populates the existing right-hand pane. This preserves the concept's region boundaries as functionality grows, without requiring a replacement shell or introducing a generalized plugin framework.

WI-002 should bind `ConnectionStatusText` to the same presentation state used by the connection view, updating visible text, accessible name, and semantic brush together on the UI thread. Its initial state must remain Disconnected. Core abstractions and Infrastructure implementations continue to own connection behavior; the shell must not acquire HTTP or persistence details. Future session execution state belongs in the workspace, independently of connection status.

Feature views should consume the shared text/card styles, spacing resources, and semantic brushes. Use `StaticResource` for sizes and styles and `ThemeResource` for brushes, including brush setters in styles. Introduce a semantic `Shell...` token when a repeated visual value or meaningful role is needed; document the platform mapping and high-contrast behavior here in the same change. Add navigation destinations only when their feature is usable.

## Verification

Implementation and actual verification evidence are recorded in WI-003. Resource definitions and a successful build do not establish that theme, scaling, or assistive-technology acceptance checks have passed.

Platform references: [XAML theme resources](https://learn.microsoft.com/en-us/windows/apps/develop/platform/xaml/xaml-theme-resources), [contrast themes](https://learn.microsoft.com/en-us/windows/apps/design/accessibility/high-contrast-themes), and [OverlappedPresenter minimum width](https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.ui.windowing.overlappedpresenter.preferredminimumwidth).
