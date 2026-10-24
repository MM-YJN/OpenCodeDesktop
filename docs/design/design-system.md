# OpenCode Desktop design system

The [desktop concept](../requirements/desktop-concept.png) and [workspace requirements](../requirements/overview.md#workspace-layout) define the hierarchy. WI-004 replaces the WI-003 application rail with a workspace and Settings. The native title bar, system theme, window controls, and minimum-size foundation remain.

## Regions and ownership

`MainWindow` retains one `WorkspaceView` and one `SettingsView`. Settings replaces workspace content through visibility, rather than destroying and recreating the workspace. Its Return to Workspace action restores the existing view. Connection status and Needs attention occupy a separate bottom row visible in both destinations. Execution state appears in the selected session's header, independently of Disconnected connection state.

`WorkspaceView` owns a left Projects SplitView and a right Details SplitView around the central conversation. The central grid contains a header, independently scrolling conversation, optional bottom terminal, and anchored composer with its own bounded scroll region. File previews and diffs only appear in Details. Header actions provide Projects, Details, and Terminal. Settings has a single entry in the Projects footer; reopen Projects when hidden to reach it. There is no outer application navigation rail or sample-mode toolbar.

The App-only `Presentation/SampleWorkspace.cs` owns transient projects, sessions, drafts, attachments, pending inputs, attention ownership, and selected Details state. `SampleFixtures.cs` contains embedded text, diff, and terminal fixtures. It has no service composition, disk, credential, transport, or process dependency. Startup immediately selects a sample session; there is no sample-mode flag, entry/exit flow, or global reset control. Restarting the app recreates fixtures. Projects use fictional names (Cinder Orchard and Lunar Postbox), invented paths, and unrelated recipe/delivery task text. The persistent status strip identifies sample data and truthfully reports Disconnected, including while Settings is open.

`DetailsView` uses native Info, Files, and Changes Pivot tabs. Each sample session remembers its active tab, selected file, comparison, selected change, and conversation/Details vertical offsets. Closing the pane retains its view; expand/restore resizes the existing content. Text and code are selectable. Unified diffs use `+`, `-`, space prefixes and file/hunk headers so changes do not rely on color.

`SettingsView` retains a scrollable surface for each of Connections, Models & Providers, Extensions, Permissions, Appearance, and Notifications. The section selector remains outside the section scroll area. Controls report local sample feedback. Appearance continues following the system theme; no preference is persisted. Service-dependent operations have explicit explanations. Connections is a layout-only form with a read-only example address and disabled password field, so it cannot retain a real secret.

## Semantic resources

`Themes/ShellResources.xaml` is merged after WinUI control resources. Use `ThemeResource` for authored brushes and `StaticResource` for styles and dimensions. Native controls supply focus, hover, selected, disabled, and high-contrast behavior without replacement templates.

| Brush | Light/dark platform mapping | High-contrast mapping | Role |
| --- | --- | --- | --- |
| `ShellSurfaceBrush` | `SolidBackgroundFillColorBase` | `SystemColorWindowColor` | Window background |
| `ShellContentSurfaceBrush` | `LayerFillColorDefault` | `SystemColorWindowColor` | Cards |
| `ShellStatusSurfaceBrush` | `SolidBackgroundFillColorBase` | `SystemColorWindowColor` | Persistent status |
| `ShellTextPrimaryBrush` | `TextFillColorPrimary` | `SystemColorWindowTextColor` | Body and headings |
| `ShellTextSecondaryBrush` | `TextFillColorSecondary` | `SystemColorWindowTextColor` | Supporting text |
| `ShellBorderBrush` | `CardStrokeColorDefault` | `SystemColorWindowTextColor` | Dividers and cards |
| `ShellAccentBrush` | `SystemAccentColor` | `SystemColorHotlightColor` | Available emphasis token |
| `ShellDisconnectedBrush` | `TextFillColorSecondary` | `SystemColorWindowTextColor` | Disconnected label |

Sample execution and extension states are always written in text. Sample success/output never changes connection state or claims actual verification.

## Typography, spacing, and sizing

All dimensions are DIPs. System text scaling remains enabled. Shared body, secondary, heading, and title styles derive from WinUI text styles. Code uses Consolas, with selection and horizontal scrolling for long lines.

| Resources | Value / purpose |
| --- | --- |
| `ShellBodyFontSize`, `ShellHeadingFontSize`, `ShellTitleFontSize` | 14, 20, 28 |
| `ShellBodyTextStyle`, `ShellSecondaryTextStyle` | Wrapped primary and secondary body text |
| `ShellHeadingTextStyle`, `ShellTitleTextStyle` | Wrapped headings |
| `ShellSpaceSmall`, `ShellSpaceMedium`, `ShellSpaceLarge` | 8, 16, 24 |
| `ShellContentPadding`, `ShellCardPadding` | 24 all sides |
| `ShellStatusPadding` | 16 horizontal, 8 vertical |
| `ShellSidebarPadding` | 16 horizontal, 24 vertical; available foundation token |
| `ShellWorkspaceHeaderPadding` | 24 horizontal, 16 vertical; available foundation token |
| `ShellComposerMargin` | 16; available foundation token |
| `ShellHairlineThickness` | 1 all sides |
| `ShellStatusBorderThickness`, `ShellHeaderBorderThickness` | Top-only / bottom-only 1 |
| `ShellInspectorBorderThickness`, `ShellProjectsBorderThickness` | Left-only / right-only 1 |
| `ShellCardCornerRadius`, `ShellCardStyle` | 8; semantic surface, border, radius and padding |
| `ShellContentMaximumWidth` | 720; bounded connection form |
| `ShellWindowInitialWidth`, `ShellWindowInitialHeight` | 1440 × 900 client area |
| `ShellWindowMinimumWidth`, `ShellWindowMinimumHeight` | 640 × 480 outer minimum |
| `ShellProjectsPanelWidth`, `ShellProjectsInlineThreshold` | 240; inline at 1000 workspace DIPs |
| `ShellDetailsWidth`, `ShellDetailsMinimumWidth` | Initial 420; resize minimum 320 |
| `ShellInspectorInlineThreshold` | Details inline at 1000 DIPs remaining after Projects |
| `ShellConversationMinimumWidth` | 420; limit when dragging Details wider |
| `ShellComposerMaximumHeight` | 220, additionally capped at 35% of available workspace height |
| `ShellTerminalHeight` | 180, additionally capped at 25% of available workspace height |

The obsolete `ShellNavigationWidth`, `ShellNavigationCompactWidth`, and single-inspector `ShellInspectorWidth` tokens were removed. Structural grid lengths, 8-DIP resize grip, 12-DIP compact region padding, native control sizes, file-list limits, and local editor minimum/maximum heights remain view layout choices rather than separate style systems. Repeated cards and text use shared styles through `WorkspaceControls`.

## Responsive behavior and input

Projects retains a fixed 240-DIP content width during pane animation. At or above 1000 workspace DIPs it defaults to open/inline; below that threshold it defaults to closed/overlay. Crossing the threshold selects the responsive default; resizing within the same range preserves the user's choice.

Details uses the width remaining after Projects. At 1000 DIPs or above it defaults to inline/open, initially 420 wide; at smaller sizes it defaults to closed/overlay, capped to leave 48 DIPs for dismissal. Native overlay light dismissal and Close are available. Expand fills the available workspace content area; Restore reuses the previous split width. The left-edge Thumb supports pointer dragging and Left/Right keys in 24-DIP steps, bounded by Details and conversation minimums. It is disabled in overlay/expanded modes. Closing either side pane restores focus to its header toggle.

Details uses the local `PaneOnlySplitView` subclass. The bundled WinUI default template animates both `PaneTransform` and `ContentTransform` when opening/closing inline. On template application, this control removes only transition timelines targeting `ContentTransform`, retaining native pane movement/clipping, layout changes, accessibility, and light dismissal. The central workspace adjusts its available width without sliding in or recreating content. The override is scoped to Details, not Projects or other controls. When upgrading WinUI, recheck the bundled template's target names and the pane-only animation; do not assume its internal storyboard names are a stable public contract.

The header toolbar scrolls horizontally at narrow widths. Conversation, composer, terminal output, project list, Settings content, and Details tabs scroll independently. Composer and terminal height caps reserve space for conversation at small heights. Submit follows the multiline draft; optional input queue and reference/model/command controls are expandable. At very small heights or enlarged text, the composer itself scrolls to keep all controls reachable.

Session list context menus expose rename, fork, archive/restore, and delete; Shift+F10 provides the native keyboard path. Rename and delete use cancellable ContentDialogs, with delete defaulting to Cancel. Needs attention opens a cross-session dialog; selecting a request restores the conversation if Details was expanded, selects its owner, and brings the card into view. Permission and form responses affect only sample state.

Window dimensions are converted to physical pixels with `XamlRoot.RasterizationScale`. Monitor scaling changes refresh native `OverlappedPresenter` minimum constraints. There is no custom title bar or resize interception.

## WI-002 integration point

`Views/ConnectionView.xaml` is instantiated inside the Connections section of `SettingsView`; WI-002 replaces this layout-only form with real connection presentation and a use case. Do not introduce a Connection rail destination. Compose Core interfaces and Infrastructure implementations at application startup, not in sample models.

`MainWindow.ConnectionStatusText` remains the persistent connection presentation seam. WI-002 must update its visible text, accessible name, and semantic brush together on the UI thread using real connection state. Keep session execution independent and sample fixtures separate from future service composition. No real server path is read through a local file API by the sample workspace.

## Verification

Actual builds, launch commands, runtime observations, external screenshots, and remaining acceptance checks belong in [WI-004](../work-items/WI-004-implement-interactive-workspace-layout.md). Implemented theme resources and authored accessibility names do not establish manual theme, scaling, screen-reader, or keyboard acceptance.
