# WI-015 Browse and preview project files

## Details

- Type: Implementation
- Related requirements: [REV-01, SES-01 and workspace layout](../requirements/overview.md)
- Design reference: [tab-based workspace and project Files concept](../design/project-files-tabs-concept.png) ([generation prompt](../design/project-files-tabs-concept.prompt.txt))
- Priority: P1
- Dependencies: [WI-014](WI-014-verify-native-reliability-accessibility.md), [WI-011](WI-011-multi-project-support.md)
- Status: See the [active work item index](README.md)

## Goal and baseline

Introduce a tab-based central workspace where each open session and each project Files view occupies a tab. Users open Files through the project's overflow menu to navigate, find and read server-owned project files independently of any session. The approved concept above supersedes the earlier Details > Files placement. The current application has one central session view and disabled Files inside Details; Infrastructure only uses directory listing to validate a project. The concept illustrates the target design, not implemented behavior or runtime evidence.

## Scope

- Verify pinned filesystem list/find/read operations, location/path encoding, response types, size/range behavior, missing/denied paths and binary content before choosing preview bounds.
- Add a central workspace tab strip below the application title bar. Opening a session from the project tree opens or activates its existing session tab; each tab hosts its task header, conversation, pending interactions and composer. Use distinguishable titles, session/Files icons, active state and close actions. Preserve existing attention navigation by activating the owning session tab.
- Add Files to each project's existing overflow menu; do not add an inline Files button. Open or activate one Files tab per connection and server-resolved directory, including projects with no sessions. Project IDs alone are not unique workspace ownership keys.
- Put read-only directory navigation, filename search and refresh beside the selected-file text preview in the Files tab. Show the server/project, server directory, full selected path and read-only state clearly; preserve server path semantics rather than resolving through the client filesystem. Do not add nested preview tabs.
- Remove Files from session Details. Keep Info and Changes there; hide session Details, terminal controls and composer while a Files tab is active. Keep Projects and global connection/attention access available. Switch through tabs instead of a Back to conversation action.
- Define and document explicit request/content limits. Show empty/loading/error, binary/unsupported and truncated/too-large states honestly; cancel obsolete reads/searches and offer retry.
- Retain Files navigation, search, selection and scroll in memory per connection/resolved directory through tab switches, closing/reopening its view and session/project navigation. Preserve each session's draft and reading position when switching tabs. Closing a tab closes only its view: it does not remove the project, delete/archive a session, discard a draft or interrupt execution. When the active tab closes, activate an adjacent remaining tab or show a neutral workspace if none remain.
- Conversation references with a verified file identity open or activate the owning project's Files tab and select the matching preview; ambiguous references remain non-actionable. Keep tabs/results scoped to their owning backend and directory; invalidate obsolete loads and hide previous-backend content on connection changes.

## Exclusions and deliverables

No editor/write operation, local filesystem mirroring, downloads, attachment submission, language services, tab detaching/separate windows, tab reordering, or restart persistence of open tabs/preview state. Preserve existing validated project/session restoration. Deliver a verified contract slice/evidence, Core file and workspace-tab ownership/state abstractions, Infrastructure mapping, native workspace tabs and Files UI, and behavior/presentation tests. The concept's sample content and colors do not replace real server data or system theme resources.

## Acceptance criteria

- [ ] Users list directories, search files and select/read text files through the connected server, including spaces/Unicode and nested paths.
- [ ] The central workspace has distinct closable session and project Files tabs; selecting an already-open session or project Files entry activates its tab without duplicates. Tab titles identify their context.
- [ ] Each project's overflow menu offers Files, including for projects with no sessions. Files opens a project-scoped directory/search and read-only preview view; session Details no longer contains Files.
- [ ] Switching/closing/reopening tabs preserves session drafts/reading position and project Files state during the app lifetime. Closing a view does not delete/archive server work, remove a project or interrupt execution; closing the last tab leaves a usable neutral workspace.
- [ ] Preview limits and binary/unsupported/missing/denied cases have clear truthful states without freezing the UI or exposing transport internals.
- [ ] Rapid searches, file selection and tab/connection/project switches cannot apply obsolete results to another workspace or expose another backend's tab content.
- [ ] Verified conversation file references activate the owning project's Files tab and matching preview; ambiguous references remain non-actionable. Attention navigation activates the owning session tab without losing Files state.
- [ ] Files tabs have no session Details pane or composer; session Info/Changes and global connection/attention access remain usable in their intended contexts.
- [ ] No server file mutation occurs; client path assumptions do not reject valid server paths or open unrelated local files.
- [ ] Contract probes, automated checks, x64 build and native keyboard/layout acceptance are recorded and pass.

## Verification requirements

Apply the [shared checks](README.md#shared-execution-and-verification-requirements). Test path/query encoding, content mapping and limits with controlled handlers; test tab identity/reuse, active-tab close/fallback, no-session Files access, draft/reading-state retention, navigation ownership, cancellation and stale responses in Core/presentation. Include two projects sharing a server project ID and multiple connections. Probe disposable directories against pinned 2.1.4 with text, Unicode, empty, binary, oversized and missing fixtures. Verify a large directory/search result and bounded long preview remain responsive in the native Files tab.

Native acceptance must exercise project-menu keyboard access, session/Files tab selection and closing, overflowing/long tab titles at minimum window size, focus restoration, reopening retained views, verified conversation references, and pending-attention navigation while Files is active. Compare the layout with the approved concept, including Files in the overflow menu, the central tab strip, directory/preview split, project/server labels and absence of Files in Details. Record any unsupported server behavior rather than substituting local reads.

## Planning record

Defined on 2026-10-25. No implementation or runtime contract verification has started. Documentation validation is recorded in the active index.

Revised on 2026-10-25 following approval of the tab-based concept. WI-015 includes the session/Files workspace-tab foundation and project Files menu entry; file browsing is scoped to connection/resolved directory rather than session. The reference image and prompt are saved under docs/design. This revision changes the planned scope only; acceptance criteria remain unchecked.
