# WI-004 Implement the interactive workspace layout and Settings navigation

## Details

- Type: Implementation
- Related requirements: [Workspace Layout and interaction requirements](../requirements/overview.md#workspace-layout); presentation surfaces for PRJ-01, SES-01/02/03, MSG-01/02, EXE-01/02/03, INT-01/02, REV-01/02, and P2 tools/settings. This item does not complete their live-service behavior.
- Priority: P0 shell foundation
- Dependencies: [WI-003](WI-003-implement-application-shell.md), completed
- Status: See the [work item index](README.md)

## Goal

Deliver a runnable WinUI shell whose principal components have real layout, navigation, selection, and local interaction. A user must be able to launch without an OpenCode server and try the workspace, Details tabs, terminal surface, and Settings using explicitly labeled in-memory sample content. Empty boxes, static screenshots, and inert buttons alone do not satisfy this item. Later work supplies actual API operations and richer rendering.

## Design and agreed boundaries

Use the replaced [desktop concept](../requirements/desktop-concept.png), the [workspace requirements](../requirements/overview.md#workspace-layout), and existing [design tokens](../design/design-system.md). The image shows a representative Changes-tab state; the written scope below also defines the other tabs and Settings. Preserve the native Windows title bar and existing light/dark/high-contrast foundation. Match hierarchy and interaction rather than generated pixel details or example model names.

- Remove the outer application navigation rail. Projects/sessions are the only left workspace panel.
- Keep the central header, independently scrolling conversation, pending-input area, and anchored composer.
- Replace Session info with a right-hand Details pane containing Info, Files, and Changes tabs. File previews and expanded diffs belong here, never in a bottom preview/diff panel.
- Put Connections and Extensions inside Settings. Settings is reached from the projects-panel footer, with a header entry available when Projects is hidden. Settings replaces the workspace content and has an explicit return action.
- Keep connection status and Needs attention reachable outside Settings. Connection state and session execution state have separate labels.
- Both side panels collapse independently. Details is resizable and has expand/restore; narrow layouts use overlays or a full-content Details view without squeezing text into unusable columns. Terminal output alone may use a collapsible bottom surface.

## Scope and component interactions

### 1. Preview boundary and sample state

Normal startup remains disconnected with truthful empty states and a visible **Preview sample workspace** entry. Entering preview displays a persistent **Layout preview - sample data** indicator. Supply at least two sample projects, several sessions with distinct execution states, a multi-message conversation with code and tool activity, a pending permission, a question form, files, changes, and terminal output. An **Exit preview** action restores the disconnected shell. A reset action restores sample fixtures for repeatable review.

Keep preview state in App presentation models/view models, separate from real connection state and future service composition. Switching sessions preserves independent drafts, tab selection, selected file/diff, and scroll position within the running app. Samples never trigger HTTP, process execution, credential persistence, repository changes, provider authentication, or actual permission decisions. No fake success is shown as a live result.

### 2. Projects and sessions

Implement project expand/collapse, project/session search with clear and no-results states, session selection, and a New task action creating/selecting an in-memory draft. Project controls expose a local sample directory/worktree selector. A session menu provides preview rename, fork, archive/restore, and delete with observable local effects; cancel leaves state unchanged. Keep archived sessions reachable through a filter. Other future operations may be omitted rather than exposed as inert buttons.

### 3. Header, conversation, and composer

Show selected session title, server-side directory, sample branch/worktree, and execution state. Provide Projects, Details, Terminal, and Settings access. Render selectable sample text, a code block, expandable tool cards, a clickable file reference, and a clickable change summary.

Composer controls include multiline draft input, attachment/reference sample picker with removable chips, model/agent selectors, and command/skill discovery with sample entries. Submit appends a clearly simulated local message; no agent response is implied. Show a pending-input list with queue/steer selection and cancel actions. A preview interrupt action changes only the sample execution state. Disabled actions explain their prerequisite; enabled actions must have an observable effect.

Permission cards expose their scope and allow/reject choices; question cards support input, validation, submit, and cancel. Local responses update those cards and attention counts. Needs attention opens a cross-session list; choosing a request selects its owning session and reveals the relevant card. Reset restores requests for another trial.

### 4. Details pane

- **Info:** sample session/provider/model/agent metadata, context usage, activity, usage/cost, and instructions. Support expandable sections and a local instructions editor with save/cancel feedback clearly scoped to preview.
- **Files:** searchable sample file navigation and a selected-file text/code preview with path and line numbers. Conversation file references open this tab and select the referenced file. Unsupported/binary and missing-file samples have explicit states; do not read actual server paths through local file APIs.
- **Changes:** selectable changed-file list and a readable unified diff with additions/deletions identified by more than color. Comparison choices are Session turn, Working copy, and Branch, each with a visible basis and separate sample results. Conversation change summaries select the relevant diff. Represent no-changes state. No commit, push, or real revert action is required.
- Preserve each tab's selected item and scroll position across tab and session navigation. Closing/reopening Details preserves context. Expand makes Details occupy the available workspace content area; restore returns to the previous split width. Both actions retain the active tab and content.

### 5. Terminal surface

Provide a collapsed-by-default, independently scrollable bottom terminal panel within the central workspace. Include terminal selection, sample output, clear/reset output, and close/reopen. An optional input field must label its interaction as simulated. Do not start a PTY or shell process. Closing the surface restores conversation space; file previews and diffs remain in Details.

### 6. Settings

Provide section navigation for **Connections**, **Models & Providers**, **Extensions**, **Permissions**, **Appearance**, and **Notifications**. Connections has the address/password form layout and explicit unavailable/live-integration explanation; do not accept or save real secrets in preview. Extensions includes MCP and plugin sample lists with selection and detail/status surfaces. The other sections provide representative selectors, toggles, or list/detail controls with local preview feedback. Appearance can continue following the system theme; theme persistence is deferred. Mark service-dependent actions unavailable with an explanation rather than simulating real authentication or updates.

All sections must be reachable, scroll correctly, and offer usable controls rather than headings alone. Returning to Workspace restores the selected session, draft, open panels, and Details context. Do not implement live connection behavior here; [WI-002](WI-002-implement-server-connection.md) replaces the Connections preview afterward.

## Responsibilities and exclusions

App owns views, reusable presentation controls, view models, sample fixtures, in-memory navigation, and composition seams. Core and Infrastructure remain UI-independent; do not introduce business or transport abstractions solely for sample behavior. Reuse/update semantic resources and native controls rather than hardcoding independent styling per component.

Excluded: all live OpenCode operations, real filesystem browsing/writing, terminal execution, streaming/recovery, credential storage, persisted preferences, full editor/LSP, full Git management, production Markdown/diff virtualization, and complete coverage of every API operation. The layout must leave clear extension points without implementing those workflows.

## Deliverables

1. Revised runnable shell and functional local preview covering every component above.
2. Maintainable App views/view models and repeatable sample fixtures, with no new backend dependency.
3. Updated design-system documentation reflecting actual regions, sizing tokens, responsive thresholds, Settings navigation, and WI-002 connection integration points. Remove obsolete navigation resources when unused.
4. A short repeatable launch/preview walkthrough and actual verification evidence in this work item. Save screenshots outside the repository and record their paths; the concept remains the repository's design image.

## Observable acceptance criteria

- [ ] App launches without a server, has no application navigation rail, and enters/exits/resets an unmistakably labeled sample workspace without network/process/file mutations or a false Connected state.
- [ ] Project grouping, search/no-results, session switching, sample directory/worktree selection, New task, and session-menu operations work locally; drafts remain independent between sessions.
- [ ] Header, conversation, tool expansion, attachment chips, model/agent selectors, command/skill picker, simulated submission/interrupt, and queue/steer/cancel controls have observable local behavior.
- [ ] Permission and form interactions, validation/cancellation, attention counts, and cross-session attention navigation can be exercised repeatedly using reset.
- [ ] Info, Files, and Changes tabs work; file references and change summaries navigate to the correct content; comparisons identify their basis; file/diff empty and unavailable states are inspectable.
- [ ] Tab/session selection and scroll state survive navigation; Projects and Details collapse/reopen independently; Details resize and expand/restore work without losing content or focus.
- [ ] Terminal opens, selects sample output, clears/resets, and closes without executing commands or taking ownership of previews/diffs.
- [ ] All six Settings sections are reachable with representative controls; Connections and Extensions have no outer navigation entries; return restores workspace state; Settings stays accessible with Projects hidden.
- [ ] At initial 1440 x 900 DIPs and existing 640 x 480 outer minimum, primary actions remain reachable, overlays dismiss, independent scrolling works, and long paths/diffs do not squeeze sidebar text or overlap the composer/status area.
- [ ] Keyboard traversal/activation, tab and list navigation, focus recovery, accessible names, non-color status/diff cues, system light/dark/high contrast, and 100%/150%/200% display scaling plus enlarged text are checked with actual results recorded.
- [ ] Normal x64 solution build passes; a documented app launch route works; runtime walkthrough covers the interactions above. Required review is accepted before Done; unavailable checks remain unchecked and implementation-complete work stays Awaiting Acceptance.

## Verification requirements

```powershell
dotnet build OpenCodeDesktop.slnx -p:Platform=x64
git diff --check
```

Run the built app using a working packaged development deployment. If normal activation fails, record the error and exact temporary self-contained/unpackaged build/launch command used (see WI-003 precedent); do not silently change repository packaging or treat a build as a successful launch.

Walkthrough: disconnected startup -> preview -> project/search/session navigation -> edit draft and switch away/back -> create/rename/fork/archive/restore/delete sample session -> expand tool card -> reply to permission/form through attention list -> use composer selectors/attachments/commands/queue -> open referenced file -> switch/select diff comparisons -> Info -> resize/expand/restore/close/reopen Details -> sample terminal -> every Settings section -> return with context intact -> narrow-window and keyboard checks -> exit/reset preview. Repeat theme and scaling checks, recording exact environment, observed results, external screenshot paths, and limitations.

Inspect preview actions for accidental service/process/file operations and real-secret retention. Core/Infrastructure test runs are unnecessary for App-only layout work; run their meaningful tests if their behavior changes. Add focused tests only for substantive state logic introduced, such as per-session draft isolation or attention ownership, rather than tests mirroring XAML. Zero discovered tests do not establish behavior validation.

## Planning decisions and evidence

- 2026-10-24: User approved removal of outer navigation, Settings ownership of Connections/Extensions, and Info/Files/Changes within the right Details pane. User requested an updated concept and a work item that yields a runnable, navigable component layout before backend detail is filled in.
- 2026-10-24: Replaced the concept using built-in image generation with the previous PNG as edit reference. The selected image shows Changes active, a file list/diff within Details, the projects-panel Settings entry, and persistent status/attention. Exact generation prompt: [desktop-concept-prompt.txt](../design/desktop-concept-prompt.txt). It is a design illustration, not implementation or runtime evidence.
- 2026-10-24: Place this item before WI-002 and update WI-002 to integrate Connections inside Settings. Explicit offline preview reconciles hands-on layout review with truthful disconnected behavior.

## Implementation and verification record

Implementation has not started. Creating this definition and replacing the concept do not execute this work item. No application build or runtime acceptance is claimed by this planning change.
