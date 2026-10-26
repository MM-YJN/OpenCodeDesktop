# WI-018 Search, rename, archive and restore sessions

## Details

- Type: Implementation
- Related requirements: [SES-03, SES-01 and PRJ-01](../requirements/overview.md#functional-requirements)
- Priority: P1
- Dependencies: [WI-014](WI-014-verify-native-reliability-accessibility.md), [WI-011](WI-011-multi-project-support.md), [WI-012](WI-012-persist-connections-workspace.md)
- Status: See the [completed work item index](completed.md)

## Goal and baseline

Make real project histories manageable without deleting server work. The tree currently supports listing, pagination, creation and selection but exposes no search/rename/archive/restore operations.

## Scope

- Verify pinned session search, archive filters and update semantics, pagination under filters, title validation, active-session archive effects and update events.
- Add project-scoped search, rename, active/archived history selection, archive and restore actions with full session identity and useful keyboard access.
- After archiving a selected inactive session, clear active selection to that project's new-session draft while retaining in-memory session drafts for explicit reopening/restoration. Keep unrelated selection unchanged. Archiving does not delete files/history or imply interruption.
- Initially disable archive while the client knows execution, unresolved delivery or pending requests remain; revalidate before mutation and reconcile server races. Preserve global attention discovery even for archived sessions.
- Reconcile refresh/restart selections authoritatively; hidden or unavailable saved selections must lead to explicit guidance, not a silent unrelated selection. Apply generation guards and preserve search/pagination ownership.

## Exclusions and deliverables

No session deletion, bulk actions, custom ordering/tags, cross-project moves or fork/revert. Deliver contract/evidence, Core organization behavior, Infrastructure queries/mutations, tree/search UI and regressions.

## Acceptance criteria

- [x] Search and pagination remain scoped to the owning connection/project/filter, including no matches and matching older sessions.
- [x] Renaming updates authoritative tree/header metadata; invalid/failed/uncertain operations retain usable state and do not blindly retry.
- [x] Archive hides an eligible session from active history, preserves server content and follows the defined selection/draft behavior; restore makes it available again.
- [x] Known running/waiting/uncertain sessions cannot be archived through this UI; concurrent changes and stale responses are reconciled without lost attention access.
- [x] Navigation, reconnect and restart handle renamed/archived/missing saved sessions with clear feedback and no cross-project results.
- [ ] Contract probes, automated checks, x64 build and native keyboard/search/archive acceptance pass.

## Verification requirements

Apply the [shared checks](README.md#shared-execution-and-verification-requirements). Probe temporary sessions with distinct titles, multiple pages and archive states. Test encoding/filter cursors, rename validation, races, selection persistence, draft retention and failed mutations. Native checks cover context actions, focus after rename/archive/restore, empty search, long titles and pending-attention navigation. Verify archive leaves messages/files intact through authoritative reads.

## Planning record

Defined on 2026-10-25. No implementation or runtime contract verification has started. Documentation validation is recorded in the active index.

## Implementation decisions (2026-10-26)

- Core owns filters, title validation, mutation gates, identity checks, reconciliation and selection/draft rules. Infrastructure implements the organization interface and encoded queries/PATCH requests. App owns project Search/history menus, session overflow/context menus and native dialogs. Dependencies and package versions are unchanged.
- Search is submitted explicitly from each project's menu and filters server titles. Active/archived mode and search are independent per connection/project and retained in memory, not persisted. A filter change resets pagination and cancels its previous read. Late filter pages, navigation transactions and mutations cannot replace a different owner. Open conversation metadata is separate from filtered tree results; searching does not change the selection or draft.
- Client titles require 1–500 UTF-16 characters, nonblank and without control characters. Authoritative GETs update tree/header/tab metadata. Updates are serialized per project. Once a mutation is dispatched, an unconfirmed result disables further changes for that session until an explicit refresh or authoritative event reconciliation. No mutation is retried automatically.
- Archive requires a healthy attention/recovery state, rejects known running/loading/busy/waiting/queued/uncertain states, then rechecks session ownership and server active/inbox/permission/form state immediately before PATCH. The server offers no atomic eligibility precondition: a concurrent start can still race the check. Post-update task hydration and global attention discovery preserve access; archive never sends interrupt or delete.
- Archiving the selected session clears its confirmed navigation identity and opens that project's existing new-session draft. Its session draft and attachments remain in memory. Archiving another project's/session's history keeps the current selection. Archived sessions can be explicitly reopened for attention or restored. Refresh/reconnect/restart resolve saved IDs authoritatively; archived/missing selections show guidance instead of choosing another conversation. Session rename/archive/unarchive events trigger guarded REST refreshes.
- Native testing found a crash when UIA invoked a second organization dialog over an open search dialog. Organization dialogs now serialize through a guard. Filter changes also explicitly reveal the project after the result arrives. Truncated project guidance is exposed in accessible names and tooltips.

## Pinned contract and evidence

The [verifier](../integration/verify-session-organization.ps1) checks the retained 2.1.4 executable SHA-256, isolates settings/storage/credentials, starts a deterministic loopback provider, drains SSE and cleans up its owned processes/scratch. The [runtime slice](../integration/contracts/opencode-2.1.4-organization.openapi.json) retains four complete path objects and 71 transitive schemas. Repeated runs compared structurally equal. The [sanitized evidence](../integration/contracts/opencode-2.1.4-organization-evidence.json) contains 36 checked cases and 36 events from the final run. Bodies are parsed before sanitization; temporary paths and opaque cursors are replaced. No credentials, private content or provider settings are retained.

Observed semantics:

- `GET /api/session` supports directory, title search, archive boolean and opaque filtered pagination. Tests cover Unicode, spaces, hash/percent encoding, a matching older page and no matches.
- `PATCH /api/session/{sessionID}` returns 204 for title/archive changes. `time.archived` indicates archive; restore removes it. Repeating archive succeeds without another archive event. Empty string title returns 204 but leaves the previous title unchanged; numeric title returns 400. The client deliberately validates before sending.
- Rename/archive/restore emit `session.renamed`, `session.archived` and `session.unarchived`. Archiving a genuinely running stalled fixture leaves it running. A separate explicit interrupt cleans up that fixture. An archived session's pending ask-rule permission remains discoverable through the global location endpoint and can be answered.
- Archive preserves imported nonempty message history and an owned project file. Missing-session updates return 404. Remote/POSIX behavior and atomic race prevention remain unverified/unavailable; the client does not claim atomicity.

Reproduction: `docs/integration/verify-session-organization.ps1 -ExecutablePath "$env:TEMP/ocd-wi002-cli/package/bin/opencode.exe" -OutputDirectory "$env:TEMP/opencode/wi018/probes"`.

## Verification record

Suites ran sequentially; final behavior totals are **348 passed, 0 failed, 1 skipped**:

- `dotnet test --project tests/OpenCodeDesktop.Core.Tests/OpenCodeDesktop.Core.Tests.csproj`: 185 passed. Includes filter/cursor ownership, late responses, filter changes during navigation, late mutations after reconnect, authoritative/uncertain rename, archive gates, raced attention, draft retention, external metadata events and archived/missing selection recovery.
- With `OPENCODE_CONNECTION_TEST_EXECUTABLE` and `OPENCODE_TASK_TEST_EXECUTABLE` set to the hash-checked isolated 2.1.4 executable, `dotnet test --project tests/OpenCodeDesktop.Infrastructure.Tests/OpenCodeDesktop.Infrastructure.Tests.csproj`: 133 passed, 1 unrelated installed-environment discovery opt-in skipped. Includes encoded filters/cursors, archive timestamp mapping, narrow PATCH bodies, lost acknowledgement/no retry and the existing real-server workflows. Earlier default runs skipped four opt-ins; those were not passing live evidence.
- `dotnet test --project tests/OpenCodeDesktop.Presentation.Tests/OpenCodeDesktop.Presentation.Tests.csproj`: 30 passed, including active/archived no-match scope labels.
- `dotnet build OpenCodeDesktop.slnx -p:Platform=x64`: passed, 0 errors. Existing analyzer/style warnings and additional style/complexity warnings remain; no analyzer package was added solely for verification.
- `git diff --check`: passed. Launch-generated runtime lock changes are restored; no dependency change is intended.

### Native evidence and outstanding acceptance

`winapp run ./src/OpenCodeDesktop/OpenCodeDesktop.csproj --arch x64 --detach --json` built and launched the packaged binary explicitly. Evidence is outside Git at `%TEMP%/opencode/wi018/native`; build/test/launch logs are `%TEMP%/wi018-*.log`. A disposable pinned server held 54 distinct sessions. Original settings were backed up byte-for-byte before fixture setup, with a DPAPI-protected fixture credential.

UIA verified searching an older session, authoritative Unicode rename in header/tab, no matches without changing the open conversation, whitespace-only title disabling Rename, archive to the project draft, archived history/restore, and recovery of the unchanged unsent session draft. It also verified Enter opens session actions. Evidence includes `renamed.json`, `no-matches.json`, `invalid-rename.json`, `keyboard-menu.json`, `final-archived.json`, `final-restored.json` and `final-restored-draft.txt`. A packaged restart reopened renamed metadata; in-memory drafts intentionally do not survive restart. Final-source PID 48692 verified Enter submits the title search and closes its dialog, with the filtered row and accessible scope visible (`final-source.json`). AutoSuggestBox consumes Enter, so its QuerySubmitted event explicitly completes the search dialog.

Dark-theme screenshots were captured and inspected. Final-source `final-compact.png` is 1280×960 physical at 200% scaling (640×480 effective outer size), with search, archive toggle and both dialog actions visible. `final-wide.png` is 2880×1800 physical (1440×900 effective outer size), showing the filtered tree, header and tab. Exact client-area sizing acceptance, light/high-contrast visual checks, complete keyboard/Narrator coverage, long-title native review and the full native reconnect/race matrix remain outstanding. UIA names/bounds and automated regressions do not replace these checks. The installed CLI lacks `devtools`, so no live XAML attachment was available. Unsupported selector attempts and the pre-fix dialog crash are not passing evidence.

Cleanup completed: original workspace settings were restored byte-for-byte, owned app/server processes stopped, and the validated temporary fixture root removed. `native/restoration-result.json` records all four passing checks. Newly created encrypted fixture credentials were retired to temporary evidence; no fixture profile or credential remains in app storage. The unrelated pre-existing WI-017 document edit was preserved. Generated runtime-identifier lock changes were restored after the final launch. Markdown links and `git diff --check` passed.

The final verification criterion remains unchecked because the documented native review has not been performed. No subsequent work item is started.

## Completion acceptance (2026-10-26)

Marked Done by explicit user acceptance with the documented verification limitations. Moved the row to the completed index, updated the status link and set WI-019 as next without starting it. Unperformed checks remain unverified; acceptance does not establish that they passed. This documentation-only update verified unique index membership, local links and `git diff --check`; no application build, tests or native checks were rerun.
