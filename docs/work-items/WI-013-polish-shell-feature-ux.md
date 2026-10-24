# WI-013 Polish the application shell and implemented feature UX

## Details

- Type: Implementation
- Related requirements: [Workspace layout](../requirements/overview.md#workspace-layout), [Interaction and reliability](../requirements/overview.md#interaction-and-reliability-requirements), and implemented connection, project, session and task workflows
- Priority: P0
- Dependencies: [WI-012](WI-012-persist-connections-workspace.md), which follows [WI-011](WI-011-multi-project-support.md); existing shell and live workflows delivered through WI-010
- Status: See the [active work item index](README.md)

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

- [ ] The running app visibly follows both concept references in hierarchy, balanced spacing, typography, subtle surfaces, restrained accents, selection and action emphasis; user review confirms a cohesive professional finish.
- [ ] Workspace, Projects, Settings/Connections, conversation, composer, permissions/forms and status/recovery surfaces share consistent resources and control states. There are no leftover rough diagnostic-control stacks or obsolete sample/increment messages in routine flows.
- [ ] Users can connect/discover a local service, switch connections, manage/select projects, create/select sessions, compose/send/interrupt supported tasks, respond to requests and recover from errors with clear primary actions and feedback.
- [ ] WI-012 startup restoration has clear progress and failure states; saved selections, project lists and protected-credential behavior remain correct.
- [ ] Empty, loading, busy, disconnected, stale/recovering, uncertain-delivery, failed and unavailable states are deliberate and truthful; successful-looking placeholder content is absent.
- [ ] Light and dark themes are reviewed in the running app; high contrast retains readable text, controls, selection and focus without conveying status through color alone.
- [ ] At 1440x900 and the supported minimum 640x480 effective window sizes, plus 100%, 150% and 200% display scaling, essential actions remain reachable and text/composer/pending requests do not overlap or become clipped. Long paths/titles, multiline drafts, long histories and large supported tool output remain usable.
- [ ] Keyboard traversal, focus return after pane/dialog/navigation changes, accessible names and announcements work for the main workflows. Reduced-motion settings are respected.
- [ ] Pane changes, session refreshes and streaming preserve drafts and appropriate reading/focus context; Settings return and Details open/close do not recreate or animate the main conversation unexpectedly.
- [ ] Relevant automated tests pass with nonzero discovery, the x64 solution builds, visual/manual evidence is recorded and required user visual acceptance is obtained.

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

## Planning record (2026-10-24)

Definition only; implementation has not started. Scheduled after WI-012 to include the project-list and restart-continuity surfaces in one cohesive polish pass. Both repository concept images were inspected and linked at the user's request. No change to product scope or the requirements overview is needed for this work-item definition.

Documentation verification: `git diff --check` passed; local link-target and whitespace checks for WI-013 passed. No app build, behavior tests or native visual acceptance checks were run for this documentation-only change.
