# WI-006 Open real projects and manage sessions

## Details

- Type: Implementation
- Related requirements: [PRJ-01, SES-01, CON-01](../requirements/overview.md#functional-requirements)
- Priority: P0
- Dependencies: [WI-005](WI-005-verify-project-session-workflow.md), [WI-004](WI-004-implement-interactive-workspace-layout.md)
- Status: See the [active work item index](README.md)

## Goal

After connecting, users can open a real project on that server, create a session, and return to previous conversations. The workspace clearly identifies whose work is being shown and where it lives.

## Scope

- Let users select a server-side project directory using the workflow verified in WI-005. Keep the active server and directory visible.
- Show that project's sessions and let users create and select a session.
- Load conversation history and provide access to older messages where required. Show recorded messages and activity without implying that historical work is currently running.
- Provide understandable loading, empty, unavailable, and error states with an appropriate retry action.
- Preserve unsent drafts during session/project navigation and recoverable loading failures for the app lifetime.
- Keep each server's projects, sessions, and drafts separate. Switching or disconnecting must not leave old content labelled as belonging to the new server, and delayed loads must not replace the current selection.
- Use the authenticated connection lifetime established by WI-005. Keep illustrative sample content clearly separate from real projects and actions.

## Exclusions

Prompt submission, live execution updates, model/agent selection, permission decisions, agent answers, session search/rename/archive/fork, file browsing, and automatic reconnection. Profiles, credentials, and drafts are not persisted across restarts in this increment.

## Deliverables

A real project/session workspace integrated into the existing layout, meaningful automated coverage, and recorded manual verification. Keep user workflow rules in Core, server communication in Infrastructure, and presentation in App.

## Acceptance criteria

- [ ] A connected user can open a valid server-side directory and see its server and directory clearly.
- [ ] An empty project explains how to create a session; creating one makes it available for selection.
- [ ] Selecting an existing session displays its own history, including access to older messages when applicable.
- [ ] Loading failures explain the problem and allow recovery without losing the user's draft or displaying another session's history.
- [ ] Switching sessions and projects restores their respective unsent drafts for the app lifetime.
- [ ] Successful backend switching displays only the new backend's workspace; failed/cancelled switching retains the previous workspace. Returning to a backend restores its in-memory context where still valid.
- [ ] Disconnecting makes the disconnected state clear and prevents live workspace actions. Sample content never appears as real server data.
- [ ] Keyboard navigation can reach project selection, sessions, history, and retry actions, with readable loading/error announcements.

## Definition and verification

Before making this item Ready, incorporate WI-005's supported directory-selection and history behavior and resolve any essential product decisions. Use controlled tests for ownership, draft preservation, loading failures, and late responses. Run relevant Core, Infrastructure, and Presentation tests with nonzero discovery and build the x64 solution. Manually exercise the acceptance scenarios against an isolated pinned service, including two backends and an empty project. Record commands, outcomes, and remaining limitations here; no verification has yet been performed for this item.
