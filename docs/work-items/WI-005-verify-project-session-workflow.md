# WI-005 Verify how users open projects and sessions

## Details

- Type: Investigation
- Related requirements: [PRJ-01, SES-01, SES-02, CON-01](../requirements/overview.md#functional-requirements)
- Priority: P0 prerequisite
- Dependencies: [WI-001](WI-001-verify-connection-contract.md), [WI-002](WI-002-implement-server-connection.md)
- Status: See the [active work item index](README.md)

## Goal

Establish a verified path from connecting to a server to opening a project, creating or selecting a session, and reading its history. Users should be able to trust that the workspace belongs to the server and directory they selected.

Today, connection management is real, but the workspace contains fictional sample projects and sessions. This investigation establishes what is needed to replace that sample experience with real server content.

## Scope

- Verify how users choose a directory on the connected server, including unavailable directories and empty projects.
- Verify listing, creating, and reopening sessions, and reading older messages when history is longer than one response.
- Verify how available models and agents can be discovered, including the experience when none is usable. Actual selection and submission belong to WI-008.
- Define what happens to the visible project, selected session, and unsent draft when users change projects or servers, disconnect, or encounter a loading failure.
- Resolve how a successful connection remains authorized for subsequent requests without asking for a password on every action. Preserve the existing in-memory credential boundary and define disconnect/switch cleanup.
- Use the accepted OpenCode 2.1.4 baseline. Record supported behavior and limitations rather than assuming the latest documentation describes this release.

## Exclusions

No production workspace implementation, task submission, live updates, automatic recovery, saved credentials, remote connections, or local server startup. No new requirement to remember server profiles across app restarts.

## Deliverables

- A concise project/session workflow contract under `docs/integration/`, with retained contract evidence and reproducible checks where needed.
- User-visible loading, empty, success, and failure scenarios for the next implementation.
- A refined [WI-006](WI-006-implement-live-projects-sessions.md), with unresolved decisions or blockers recorded before it becomes Ready.

## Acceptance criteria

- [ ] Evidence demonstrates opening an isolated server-side project, creating a session, listing it, and reopening its history.
- [ ] The behavior of older history, empty lists, invalid directories, unavailable sessions, and authentication failures is established; unobserved cases are explicitly identified.
- [ ] Model and agent availability can be determined, or the limitation and its effect on later task submission are recorded.
- [ ] The intended experience for changing servers/projects preserves ownership and drafts and never presents one server's data as another's.
- [ ] Subsequent requests have a defined authorization lifetime and cleanup behavior consistent with the current support boundary.
- [ ] WI-006 has observable acceptance criteria supported by the findings; any essential unresolved assumption has an unblock condition.

## Verification

Use a pinned, isolated service and disposable projects, with no production workspaces or provider accounts required for project/session checks. Retain sanitized observations and exact reproduction commands; clean up owned processes and temporary files. Clearly separate runtime evidence from proposed client behavior. Documentation-only changes need link and formatting checks, not an App build. Record actual results here when execution begins.
