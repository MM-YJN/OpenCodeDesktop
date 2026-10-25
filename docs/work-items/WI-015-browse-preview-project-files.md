# WI-015 Browse and preview project files

## Details

- Type: Implementation
- Related requirements: [REV-01 and workspace layout](../requirements/overview.md)
- Priority: P1
- Dependencies: [WI-014](WI-014-verify-native-reliability-accessibility.md), [WI-011](WI-011-multi-project-support.md)
- Status: See the [active work item index](README.md)

## Goal and baseline

Enable Details > Files so users can navigate, find and read server-owned project files. The current Details pane shows real Info metadata but disables Files; Infrastructure only uses directory listing to validate a project.

## Scope

- Verify pinned filesystem list/find/read operations, location/path encoding, response types, size/range behavior, missing/denied paths and binary content before choosing preview bounds.
- Add read-only directory navigation, filename search, refresh and selected-file text preview inside Details. Show the server/project and full selected path clearly; preserve server path semantics rather than resolving through the client filesystem.
- Define and document explicit request/content limits. Show empty/loading/error, binary/unsupported and truncated/too-large states honestly; cancel obsolete reads/searches and offer retry.
- Retain Files selection and scroll in memory per connection/project through pane/tab changes and navigation. Conversation references with a verified file identity open the matching preview; ambiguous references remain non-actionable.

## Exclusions and deliverables

No editor/write operation, local filesystem mirroring, downloads, attachment submission, language services or restart persistence of preview state. Deliver a verified contract slice/evidence, Core file abstractions/state, Infrastructure mapping, native Files UI and behavior/presentation tests.

## Acceptance criteria

- [ ] Users list directories, search files and select/read text files through the connected server, including spaces/Unicode and nested paths.
- [ ] Preview limits and binary/unsupported/missing/denied cases have clear truthful states without freezing the UI or exposing transport internals.
- [ ] Rapid searches, file selection and connection/project switches cannot apply obsolete results to another workspace.
- [ ] Files selection/scroll survives pane/tab changes and returning to the same project during the app lifetime; verified conversation file references open Files correctly.
- [ ] No server file mutation occurs; client path assumptions do not reject valid server paths or open unrelated local files.
- [ ] Contract probes, automated checks, x64 build and native keyboard/layout acceptance are recorded and pass.

## Verification requirements

Apply the [shared checks](README.md#shared-execution-and-verification-requirements). Test path/query encoding, content mapping and limits with controlled handlers; test navigation ownership, cancellation, stale responses and retained state in Core/presentation. Probe disposable directories against pinned 2.1.4 with text, Unicode, empty, binary, oversized and missing fixtures. Verify a large directory/search result and bounded long preview remain responsive in the native pane. Record any unsupported server behavior rather than substituting local reads.

## Planning record

Defined on 2026-10-25. No implementation or runtime contract verification has started. Documentation validation is recorded in the active index.
