# WI-005 Verify how users open projects and sessions

## Details

- Type: Investigation
- Related requirements: [PRJ-01, SES-01, SES-02, CON-01](../requirements/overview.md#functional-requirements)
- Priority: P0 prerequisite
- Dependencies: [WI-001](WI-001-verify-connection-contract.md), [WI-002](WI-002-implement-server-connection.md)
- Status: See the [completed work item index](completed.md)

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

- [x] Evidence demonstrates opening an isolated server-side project, creating a session, listing it, and reopening its history.
- [x] The behavior of older history, empty lists, invalid directories, unavailable sessions, and authentication failures is established; unobserved cases are explicitly identified.
- [x] Model and agent availability can be determined, or the limitation and its effect on later task submission are recorded.
- [x] The intended experience for changing servers/projects preserves ownership and drafts and never presents one server's data as another's.
- [x] Subsequent requests have a defined authorization lifetime and cleanup behavior consistent with the current support boundary.
- [x] WI-006 has observable acceptance criteria supported by the findings; any essential unresolved assumption has an unblock condition.

## Verification

Use a pinned, isolated service and disposable projects, with no production workspaces or provider accounts required for project/session checks. Retain sanitized observations and exact reproduction commands; clean up owned processes and temporary files. Clearly separate runtime evidence from proposed client behavior. Documentation-only changes need link and formatting checks, not an App build. Record actual results here when execution begins.

## Findings and decisions (2026-10-24)

Delivered the [workflow contract](../integration/opencode-project-session-contract.md), [reproducible verifier](../integration/verify-project-session.ps1), [runtime schema slice](../integration/contracts/opencode-2.1.4-project-session.openapi.json), and [31-case sanitized evidence](../integration/contracts/opencode-2.1.4-project-session-evidence.json). Refined WI-006's implementation boundary, acceptance scenarios and verification requirements; it is Ready, with no essential unresolved decision. No production workspace implementation was started.

Material findings:

- Opening uses location resolution followed by filesystem directory validation; session listing uses a separate directory query. The server accepts a file as a location, but filesystem listing correctly rejects it. Unicode/spaces round-trip.
- Empty Git roots share project ID `global`; directory-filtered sessions remain separate. Ownership must include backend and resolved directory, with session metadata validation before history.
- Sessions can be created/reopened without models. Message history is paged; a final nonempty page still supplies a next cursor. Five imported synthetic messages were read in pages of 2/2/1/0 without loss/duplication. Import is a disposable verifier fixture, not a production feature or task-execution proof.
- Missing paths/sessions, empty lists, invalid cursors and missing/wrong Basic authentication have observed results. Models and agents can be discovered, but listed models do not establish successful inference. Empty discovery, access-denied paths, aliases, concurrent mutation, credential rotation and actual assistant/tool execution remain explicitly unobserved.
- The wider runtime schema differs from the frozen source used in WI-001. Retained runtime fields, rather than that source revision, define WI-006's baseline. The verifier detects later runtime drift.
- The client will retain only an active authenticated context in memory, promote switch candidates atomically, release old credentials on successful switch/disconnect/401/disposal, and require password entry on return. Owned navigation/drafts survive for the app lifetime with generation guards and revalidation. These are proposed client rules, not completed UI behavior.

## Verification evidence (2026-10-24)

Exact runtime command from repository root (PowerShell 7):

```powershell
./docs/integration/verify-project-session.ps1 -ExecutablePath C:/Users/jinyi/AppData/Local/Temp/ocd-wi002-cli/package/bin/opencode.exe -OutputDirectory C:/Users/jinyi/AppData/Local/Temp/ocd-wi005-results
git diff --check
```

The final verifier passed **31 cases**, exact executable hash, runtime 2.1.4/PID/temporary-path identity, runtime-to-retained-snapshot structural equality, directory/session isolation, creation/list/reopen, history/session pagination, errors and discovery. Repeated successful runs established reproducibility; the last generated evidence is retained. PowerShell AST parsing reported no syntax errors. JSON parsing/reference validation resolved all references across ten paths and 97 schemas. Sanitization checks found no user home path, owned root identifier, API key or authorization header in retained observations; opaque cursor values are replaced. A temporary-root inventory after the final run found zero owned scratch directories. Owned process trees were terminated in `finally`; no verifier service remains.

Exploratory assertions exposed and were corrected for server behavior: empty-root project IDs are shared; synthetic inbox items are not projected history; final nonempty history pages retain cursors; file paths pass location resolution. Final checks retain these findings rather than assuming the original hypotheses. A comparison to the frozen source also failed equality for this wider slice; differences are documented in the contract/provenance, not represented as a passing source match.

Markdown local-link targets and formatting were checked. App builds and application tests were not run: this item changes documentation, contract fixtures and an opt-in investigation script only. Runtime fixture checks do not validate WI-006 UI behavior or actual inference.

Completed on **2026-10-24**. Moved this row to the completed index and made WI-006 Ready without starting it. All investigation acceptance criteria have evidence or the explicit limitation records they require; no manual UI acceptance is required for this item.
