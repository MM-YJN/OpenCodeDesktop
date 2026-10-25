# WI-017 Reference and attach files to prompts

## Details

- Type: Implementation
- Related requirements: [MSG-02 and interaction reliability](../requirements/overview.md)
- Priority: P1
- Dependencies: [WI-015](WI-015-browse-preview-project-files.md), [WI-008](WI-008-implement-live-task-workflow.md), [WI-009](WI-009-implement-session-recovery.md)
- Status: See the [completed work item index](completed.md)

## Goal and baseline

Let users include verified file inputs in tasks. Current submissions contain only a stable ID and text; file inputs must share the same ownership and uncertain-delivery guarantees.

## Scope

- Verify the pinned prompt file-attachment contract, supported server references and inline/local content forms, media types and limits. Define a supported subset and user-visible bounds from that evidence; do not assume a local path is accessible to the server.
- Add attachment chips/list to the composer with full identity, remove action and validation feedback. Provide Add to prompt from Files for supported server references and a local file picker for verified local-content forms. If the pinned API cannot accept local content, record the evidence and explain that limitation explicitly rather than pretending a path upload succeeded.
- Retain text and attachments together in memory per draft/session through navigation and recoverable failures. Bind submitted inputs to the stable submission ID, connection, project and session.
- Send only on explicit submission, reconcile admitted attachments/history without duplicate sends, and render supported historical attachments safely.

## Exclusions and deliverables

No arbitrary URL fetching, drag/drop, clipboard image capture, media editing, persisted draft content or new upload service. Deliver contract/evidence, typed submission/draft changes, transport mapping, composer/history presentation and meaningful tests.

## Acceptance criteria

- [x] A supported server file can be added from Files, inspected/removed in the composer, submitted and observed in server history/provider input.
- [x] A local file follows a verified content-delivery contract, or the unavailable path is explicitly explained with pinned evidence; local paths are never silently treated as remote files.
- [x] Unsupported types, missing files and oversized content fail with actionable feedback while preserving usable draft text/attachments.
- [x] Attachments stay scoped to their draft and do not leak across connections/projects/sessions; failed or uncertain sends preserve the submitted payload identity without automatic resend.
- [x] Settled admission clears only the submitted draft version, preserving edits made after submission; history does not duplicate attachment rendering on refresh.
- [ ] Contract probes, automated checks, x64 build and native composer/history acceptance pass for the documented supported subset.

## Verification requirements

Apply the [shared checks](README.md#shared-execution-and-verification-requirements). Test typed payload serialization, escaping/media bounds, draft ownership/versioning, file read failures, navigation races and lost acknowledgements. Use harmless disposable files and the deterministic provider fixture to inspect actual received attachments without paid inference. Exercise native picker cancel, add/remove, validation, navigation, reconnect and keyboard access. If server restrictions materially prevent MSG-02, record the blocker before claiming completion.

## Planning record

Defined on 2026-10-25. The original planning/documentation validation is recorded in the active index; implementation and verification are recorded below.

## Implementation decisions (2026-10-25)

Dependencies WI-015, WI-008 and WI-009 are Done in the completed index. This increment retains the pinned 2.1.4 baseline and existing connection/session ownership and recovery architecture.

- Core adds immutable validated attachments, versioned in-memory drafts and attachment-aware submissions. Text and attachments transfer together when a draft becomes a session. Every send captures one stable input ID and a read-only attachment snapshot. HTTP acknowledgement or later authoritative inbox/history admission consumes only the recorded draft version; even editing text away and back or removing/re-adding a file preserves the later version. No automatic prompt retry is added.
- The supported subset is four UTF-8 text files, 256 KiB each, with extensions txt/md/cs/json/xml/yaml/yml/js/ts/css/html/py/csv/log. Invalid UTF-8, NUL-containing binary content, unsupported extensions, read failures and bounds produce actionable messages. Local file contents are captured when attached, retained only in memory and submitted as `data:text/plain;base64,...`; their local paths are display identity only. Infrastructure owns bounded asynchronous local file reads behind a Core interface.
- Files provides Add to prompt. It targets that project's current session/draft when selected, otherwise opens a new-session draft for the owning project. Selection/read/picker continuations check ownership/generations before attaching. Server references require a supported complete preview, including the existing 100,000-character preview ceiling, and are revalidated for existence/type/size before sending. A file can still change between this validation and server admission; server references intentionally use the contents read by the server at admission.
- Composer chips expose source/name, a full selectable identity in a flyout, and a named remove action. The local picker action is in the toolbar. Chips scroll horizontally and the composer retains keyboard access in compact windows. History projects inline content to non-actionable labels and preserves verified server references without decoding/fetching content or inventing paths. Admission/inbox response caps are 4 MiB so four maximum inline inputs fit; history retains its existing 8 MiB cap.

## Contract and automated evidence

See [attachment provenance](../integration/contracts/README.md#prompt-attachments-wi-017), the [runtime slice](../integration/contracts/opencode-2.1.4-attachments.openapi.json), [sanitized evidence](../integration/contracts/opencode-2.1.4-attachments-evidence.json), [verifier](../integration/verify-attachments.ps1) and [provider fixture](../integration/attachment-provider-fixture.mjs).

The hash-checked runtime accepts prompt `files: [{uri,name}]`. Server `file:` sources become history files with source type `uri`; local data URIs become `inline`. The deterministic provider received the actual synthetic contents for both forms and every supported server extension. Missing files returned 400 before admission. Empty inline content and four 256 KiB inline files were admitted. OpenAPI declares no file-count/MIME/byte maximum: the documented bounds are client policy, not an inferred server maximum. Remote/POSIX behavior, non-text media and concurrent file changes at admission remain unobserved.

Commands were run sequentially from the repository root on Windows x64. Logs and disposable native evidence are outside source control under `%TEMP%/opencode/wi017` and `%TEMP%/wi017-*.log`.

| Check | Result |
| --- | --- |
| `pwsh -File docs/integration/verify-attachments.ps1 -ExecutablePath "$env:TEMP/ocd-wi002-cli/package/bin/opencode.exe" -OutputDirectory "$env:TEMP/opencode/wi017/probes-extensions"` | Passed, including provider-content assertions and the client maximum payload. Repeated runs structurally matched the retained runtime slice. |
| `dotnet test --project tests/OpenCodeDesktop.Core.Tests/OpenCodeDesktop.Core.Tests.csproj` | 168 passed, 0 failed/skipped. `wi017-core-current.log`. |
| `dotnet test --project tests/OpenCodeDesktop.Infrastructure.Tests/OpenCodeDesktop.Infrastructure.Tests.csproj` | 130 passed, 0 failed, 1 unrelated installed-service discovery opt-in skipped. Both pinned connection/task executable opt-ins enabled. `wi017-infrastructure-current.log`. |
| `dotnet test --project tests/OpenCodeDesktop.Presentation.Tests/OpenCodeDesktop.Presentation.Tests.csproj` | 28 passed, 0 failed/skipped, including attachment-only send availability. `wi017-presentation-final.log`. |
| `dotnet build OpenCodeDesktop.slnx -p:Platform=x64` | Passed with 0 errors; analyzer/style warnings remain. `wi017-build-final.log`. |
| `winapp run ./src/OpenCodeDesktop/OpenCodeDesktop.csproj --arch x64 --detach --json` | Building packaged launches passed, including final-source PID 39812 on target local. `wi017-launch-current.log`. |

New coverage includes typed payload serialization/escaping, strict content and file/count bounds, captured local content despite subsequent file edits, missing/locked reads, truncated/changed server files, immutable submissions after draft edits/removal, picker-read navigation races, draft transfer to a new session, late admission without resend, restored backend delivery state, and isolation across connections/projects/sessions/reconnect. History checks exclude inline base64 content and verify one server reference. The maximum-payload regression covers both admission and inbox reconciliation above the previous 1 MiB response cap.

Initial test attempts exposed incomplete synthetic history/session response envelopes and an obsolete assertion that excluded inline labels; those fixtures/assertions were corrected. Native harness attempts also exposed asynchronous-render timing and the `get-value` result's `text` field; subsequent assertions use populated snapshots. These failed attempts are not passing evidence.

## Native verification and remaining acceptance

An owned isolated pinned server and loopback provider were used with harmless disposable files. Original settings were backed up before adding a temporary profile with a DPAPI-protected credential. No real provider or private project was used.

Native UIA verified picker open/cancel, local text attachment, local unsupported/oversized rejection without text loss, server preview/Add to prompt, server oversized/unsupported rejection, remove/re-add, and text/attachment retention through Files/session-tab navigation. Submission reached Completed; provider capture contained both `LOCAL_NATIVE_MARKER_017` and `SERVER_NATIVE_MARKER_017`. Settled admission cleared text and attachments. History displayed one local label and one actionable server reference; a subsequent packaged restart retained one copy of each. Evidence includes `native/submission-result.json`, `native/completed-verified.json`, `native/current-local-rejected.json`, `native/current-local-size-rejected.json`, `native/oversize-rejected.json`, `native/unsupported-rejected.json` and `native/final-source.json`.

Compact inspection exposed excessive height from full paths. The final layout uses a horizontal chip strip with identity/remove flyouts, a toolbar picker action and a smaller compact editor minimum. Final-source PID 39812 verified full server identity in the flyout (`native/final-identity-settled.json`), removal/re-addition, and keyboard Ctrl+Enter submission of both attachments. `native/final-submission-result.json` confirms final prompt/provider contents and an empty admitted draft. At a 1280x960 physical outer window on the 200% display (640x480 effective outer size), UIA measured a 32-DIP chip strip, 48-DIP editor, 32-DIP picker and 36-DIP Send action; all were exposed in the compact composer. Tab from the editor reached model and agent controls. Final `--capture-screen` images `native/final-compact.png` and `native/final-wide.png` (2880x1800 physical / 1440x900 effective outer size) were usable and inspected in dark theme. The earlier plain capture `native/current-compact.png` was black and is not passing visual evidence. Exact client-area dimensions, light/high-contrast visual acceptance, complete keyboard/Narrator confirmation, and the full native reconnect/navigation-race matrix remain outstanding. UIA bounds do not replace those checks. The installed WinApp CLI does not expose `devtools`, so no live XAML attachment was available. No WinUIAnalyzer dependency or system theme/tooling changes were introduced for verification.

The user explicitly accepted this item as Done on 2026-10-26 with the documented verification and cleanup limitations. Unperformed checks remain unverified; acceptance does not establish that they passed. No subsequent work item is started.

Final restoration: original workspace settings were restored byte-for-byte, owned app/server/provider processes stopped, and the fixture's rotated DPAPI credential moved out of app credential storage into temporary evidence. `native/restoration-result.json` records the checks. Automatic approval review rejected deletion of the owned temporary scratch directory with “blocked by policy” and no detailed reason; that inert directory and the retired encrypted fixture credential remain under `%TEMP%`. No running test service or temporary app profile remains.

`git diff --check` passed. Changed text files were also checked for trailing whitespace and local Markdown link targets. Launch-generated runtime-identifier lock-file changes were restored; no dependency or package-lock change is part of this item. Final totals: 326 tests passed, 0 failed, 1 unrelated opt-in skipped.

## Completion acceptance (2026-10-26)

Marked Done by explicit user acceptance. Moved the row to the completed index, retained the unchecked native verification criterion and recorded limitations, and set WI-018 as next without starting it. This documentation-only update verified unique index membership, the status link and `git diff --check`; no application build, tests or native checks were rerun.
