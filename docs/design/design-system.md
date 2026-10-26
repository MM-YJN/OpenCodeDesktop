# OpenCode Desktop design system

The [desktop concept](../requirements/desktop-concept.png), [server connections concept](server-connections-concept.png) and [workspace requirements](../requirements/overview.md#workspace-layout) define the hierarchy. The system theme, high-contrast behavior and minimum-window foundation remain. A compact custom title bar draws the brand row so the icon and title share one optical center; Windows still owns the caption buttons, drag, double-click maximize and system menu. This document describes the live application surfaces, not illustrative sample content.

## Regions and ownership

Planned WI-015 revision: the approved [tab-based workspace and project Files concept](project-files-tabs-concept.png) supersedes the earlier Files-in-Details design. Each session and each project's Files view will occupy a closable central workspace tab. Files opens from the project's overflow menu, hosts directory navigation/search and a read-only preview, and is scoped to connection/resolved directory without requiring a session. Session Details will retain Info and Changes; it is hidden while Files is active. Tabs preserve drafts and reading state during navigation. See the [workspace requirements](../requirements/overview.md#workspace-layout) and [WI-015](../work-items/WI-015-browse-preview-project-files.md) for acceptance and lifecycle rules. The implementation descriptions below record the current baseline until that work is executed.

`MainWindow` composes one `WorkspaceView` and one `SettingsView` and keeps their instances alive while the other is hidden. A compact title bar and a conditional storage/recovery banner sit above them. A persistent status strip sits below them and is visible in both destinations. The status strip owns server connection state and pending-attention access; execution state belongs to the selected session's header.

`WorkspaceView` owns the Projects SplitView, the central workspace, and the Details SplitView. Projects is a navigation surface: the server switcher, a new-session-draft action, a native project/session tree and Settings. The tree keeps every listed project as a root node; each expanded project reveals its sessions, the active project's new-session draft and a paging action. Project rows carry inline New session and More actions. The central workspace contains the task header, scrolling conversation, pending permission/question section and anchored composer. Details contains real session information and explicit unavailable states; it never invents files, diffs or terminal output.

`DetailsView` uses native Info, Files and Changes tabs. Info shows the active server, address, version, project, session, execution summary, model, agent and message state. Files and Changes are disabled in this release because no file/diff integration exists. The pane supports expand/restore, close and a left-edge resize grip.

`SettingsView` has one Return to workspace action, a compact section selector and a scrolling content area. Connections is live. Models & Providers, Extensions, Permissions, Appearance and Notifications are honest unavailable surfaces that explain the current release boundary.

`ConnectionView` owns saved-server cards, the active/inactive presentation, the context editor, local-environment detection and the save/connect/switch/disconnect flow. Cards use an accent tint and written Active state, never color alone. Inactive cards expose Connect and an overflow menu; the active card exposes View and Disconnect.

`WorkspaceView` is assembled by small presentation helpers in `Views/ShellUi.cs`. Repeated surfaces use `Themes/ShellResources.xaml`; shared message/action templates remain in `Views/WorkspaceTemplates.xaml` for simple text/card content.

## Semantic resources

`Themes/ShellResources.xaml` is merged after WinUI control resources. Use `ThemeResource` for authored brushes and `StaticResource` for styles, spacing and dimensions. Native controls supply focus, hover, selected, disabled and high-contrast behavior without replacement templates.

| Brush | Light/dark platform mapping | High-contrast mapping | Role |
| --- | --- | --- | --- |
| `ShellSurfaceBrush` | `SolidBackgroundFillColorBase` | `SystemColorWindowColor` | Window and central surface |
| `ShellSidebarSurfaceBrush` | `SolidBackgroundFillColorSecondary` | `SystemColorWindowColor` | Projects navigation |
| `ShellCardSurfaceBrush` | `CardBackgroundFillColorDefault` | `SystemColorWindowColor` | Connection and request cards |
| `ShellSubtleSurfaceBrush` | `SubtleFillColorSecondary` | `SystemColorWindowColor` | Quiet cards, badges and composer hints |
| `ShellSelectionSurfaceBrush` | `SystemFillColorAttentionBackground` | `SystemColorWindowColor` | Selected navigation row |
| `ShellCodeSurfaceBrush` | `SubtleFillColorTertiary` | `SystemColorWindowColor` | Code and tool output |
| `ShellTextPrimaryBrush` | `TextFillColorPrimary` | `SystemColorWindowTextColor` | Body, headings and selected labels |
| `ShellTextSecondaryBrush` | `TextFillColorSecondary` | `SystemColorWindowTextColor` | Supporting text |
| `ShellTextTertiaryBrush` | `TextFillColorTertiary` | `SystemColorWindowTextColor` | Captions and metadata |
| `ShellTextDisabledBrush` | `TextFillColorDisabled` | `SystemColorGrayTextColor` | Disabled text |
| `ShellBorderBrush` / `ShellDividerBrush` | `CardStrokeColorDefault` / `DividerStrokeColorDefault` | `SystemColorWindowTextColor` | Card boundaries and separators |
| `ShellAccentBrush` / `ShellAccentTextBrush` | `SystemAccentColor` | `SystemColorHotlightColor` | Primary action, selection and focus emphasis |
| `ShellConnectedBrush` / `ShellConnectedSurfaceBrush` | `SystemFillColorSuccess` / background | `SystemColorWindowTextColor` / window | Connected/active state |
| `ShellWarningBrush` / `ShellWarningSurfaceBrush` | `SystemFillColorCaution` / background | `SystemColorWindowTextColor` / window | Attention, recovery and pending requests |
| `ShellDangerBrush` / `ShellDangerSurfaceBrush` | `SystemFillColorCritical` / background | `SystemColorWindowTextColor` / window | Execution and loading failures |
| `ShellInfoBrush` / `ShellInfoSurfaceBrush` | `SystemAccentColor` / attention background | `SystemColorWindowTextColor` / window | Informational notices and questions |

The green and amber status tones are not invented brand colors. They are the Windows semantic palette: `ShellConnectedBrush` maps to `SystemFillColorSuccess` and `ShellWarningBrush` maps to `SystemFillColorCaution`, mirroring the concept's connected and attention indicators. The accent remains the Windows system accent (purple on the development machine), not the concept's teal. High contrast replaces every status hue with system window text, and status labels always include a glyph or rounded indicator plus text.

Dynamic brush assignments in code must use `ShellUi.Brush` rather than indexing `Application.Current.Resources` directly. The direct lookup returns the default theme dictionary and would keep dark-theme success green in Windows high contrast; `ShellUi.Brush` resolves the active `ThemeDictionaries` entry.

## Typography, spacing and sizing

All dimensions are DIPs. System text scaling remains enabled. The custom title bar is 48 DIP tall, using the Tall caption-button option, and hosts the brand row plus the Projects pane toggle.

| Resources | Value / purpose |
| --- | --- |
| `ShellCaptionFontSize`, `ShellTitleBarFontSize`, `ShellBodyFontSize`, `ShellSubheadingFontSize`, `ShellHeadingFontSize`, `ShellTitleFontSize` | 12, 13, 14, 16, 20, 28 |
| `ShellBodyTextStyle`, `ShellBodyStrongTextStyle`, `ShellSecondaryTextStyle`, `ShellTertiaryTextStyle`, `ShellTitleBarTextStyle` | Wrapped hierarchy for primary, supporting, metadata and title-bar text |
| `ShellHeadingTextStyle`, `ShellTitleTextStyle`, `ShellSubheadingTextStyle` | Section and task headings |
| `ShellSelectableBodyTextStyle`, `ShellCodeTextStyle`, `ShellWrappedCodeTextStyle` | Selectable conversation and monospace tool content |
| `ShellSpaceTiny`, `ShellSpaceSmall`, `ShellSpaceCompact`, `ShellSpaceMedium`, `ShellSpaceLarge`, `ShellSpaceXLarge` | 4, 8, 12, 16, 24, 32 |
| `ShellCardPadding`, `ShellCardSpaciousPadding` | 16 and 24 |
| `ShellCornerRadiusSmall`, `ShellCornerRadiusMedium`, `ShellCardCornerRadius`, `ShellCornerRadiusLarge`, `ShellCornerRadiusBadge` | 4, 6, 8, 12 and badge |
| `ShellTitleBarHeight`, `ShellTitleBarPadding` | 48; 16 DIP left inset for the brand row and Projects pane toggle |
| `ShellStatusIndicatorSize`, `ShellStatusIndicatorCornerRadius` | 8; 2. Use a rounded rectangle, never a circle/ellipse |
| `ShellContentMaximumWidth`, `ShellComposerMaximumWidth` | 760 and 860 for readable line lengths |
| `ShellWindowInitialWidth`, `ShellWindowInitialHeight` | 1440 × 900 DIP, capped to the current display work area |
| `ShellWindowMinimumWidth`, `ShellWindowMinimumHeight` | 640 × 480 DIP |
| `ShellProjectsPanelWidth`, `ShellProjectsInlineThreshold` | 288; inline at 1000 workspace DIPs |
| `ShellDetailsWidth`, `ShellDetailsMinimumWidth`, `ShellInspectorInlineThreshold` | 400, 320, inline at 1000 DIPs |
| `ShellConversationMinimumWidth` | 420; limit when dragging Details wider |
| `ShellComposerMaximumHeight` | 240, additionally capped at 40% of available workspace height |
| `ShellIconSmall`, `ShellIconMedium`, `ShellIconLarge` | 14, 16, 20 |
| `ShellTouchTarget` | 32 |
| `ShellTreeProjectRowHeight`, `ShellTreeSessionRowHeight`, `ShellTreeActionSize` | 44, 32, 28; project/session row minimums and rounded-rectangle inline action target |

Fluent MDL2 glyph strings are centralized as `ShellIcon*` resources. `ShellUi.Icon` supplies the icon font, size and alignment so future surfaces do not duplicate glyph rendering.

## Interaction and status

- Projects uses a native `TreeView`. Project nodes are roots and session/draft/paging nodes are children. The active project's folder glyph uses the accent; the selected node uses native TreeView selection and focus surfaces. Project rows show inline New session and More icon buttons using `ShellTreeActionButtonStyle` (rounded rectangle, never a circle). Hover, focus, expanded and disabled states remain native.
- Sessions load lazily for an inactive project when it is expanded. The project row shows a compact loading/empty/error line; expanding a project does not switch the active workspace. Selecting a session under another project performs staged navigation, and the active project and session are revealed after explicit navigation or attention selection. Tree expansion is retained in memory for the current connection, not persisted.
- Connection cards use `ShellCardStyle`; the active card adds an accent surface/border, a check glyph and a written Active badge. The inactive Connect action is primary; the active View action is secondary.
- The task header shows a status badge with text, icon and tone. The status strip separately shows connection identity, an 8 × 8 rounded status indicator and pending-attention count. The indicator is always paired with its written state; it is not a circle and never carries meaning alone. Connection/recovery state never replaces task execution state.
- Permission cards use warning surfaces and explicit action labels: Allow once, Save project allow rule when offered, and Reject. Question cards use accent surfaces and native field controls. Both are disabled while busy or stale.
- Delivery uncertainty is shown in the composer and in the user message; it is never treated as success and never causes a blind resend.
- Disabled or unavailable controls retain a label and tooltip explaining the release boundary. No sample content appears in routine flows.

## Responsive behavior and motion

Projects is inline/open at 1000 workspace DIPs and above; below that it defaults to closed overlay. Details uses the remaining width, defaults to inline/open at 1000 DIPs and above, and otherwise uses overlay with light dismissal. Closing either pane returns focus to its header toggle.

The window requests 1440 × 900 DIP but caps that initial size to the current display work area; the minimum is 640 × 480 DIP. The 32 DIP title bar is part of the client area. The header toolbar, project/session tree, conversation, pending requests, composer and Settings content scroll independently. The tree realizes children only for expanded projects; collapsing a project clears its realized nodes while Core retains the cached session pages. The composer is capped at 40% of available height; the pending-request section is capped at 36% of available height (maximum 320 DIP). A compact state panel is used when the conversation viewport is too short for the full empty-state illustration.

`PaneOnlySplitView` keeps native pane movement and clipping while removing the bundled template's `ContentTransform` timeline so the central conversation does not slide or reload. When Windows animations are disabled, it clears the SplitView transitions instead. When upgrading WinUI, recheck the bundled template target names and the pane-only behavior.

## Accessibility

- Headings use `AutomationProperties.HeadingLevel`; status, error and recovery text use meaningful live settings.
- Icon-only buttons have accessible names and tooltips. Project nodes expose the full server path and session nodes the full title; the active project and selected node are identified by state, not color alone.
- The project tree uses native hierarchy, expand/collapse, selection and focus behavior. Up/Down moves through visible nodes, Left/Right collapses/expands, and Enter/Space invokes the focused project, session, draft or paging action. Inline row actions have project-specific accessible names and the project overflow menu remains keyboard reachable.
- Focus returns to the pane toggle after close/expand, to the workspace toggle after return from Settings, to the connection editor after opening a profile or Add server, and to the first request action when attention is opened.
- The title bar is a drag region with the standard double-click maximize and right-click system menu. Windows still draws the minimize, maximize and close buttons, so their hit targets, keyboard access and snap behavior remain native.
- Native controls provide visible focus and keyboard traversal. Project and server overflow menus are reachable as buttons; Ctrl+Enter sends the draft. No other shortcut behavior is implied by the concepts.
- The app follows the Windows light/dark/high-contrast theme. Theme preference persistence is explicitly deferred.

## Verification

Running builds, native theme/scale checks, keyboard and screen-reader observations, screenshots and remaining acceptance checks belong in [WI-013](../work-items/WI-013-polish-shell-feature-ux.md). Resource names and authored accessibility properties do not by themselves establish manual visual or accessibility acceptance.
