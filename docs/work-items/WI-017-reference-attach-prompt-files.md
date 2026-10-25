# WI-017 Reference and attach files to prompts

## Details

- Type: Implementation
- Related requirements: [MSG-02 and interaction reliability](../requirements/overview.md)
- Priority: P1
- Dependencies: [WI-015](WI-015-browse-preview-project-files.md), [WI-008](WI-008-implement-live-task-workflow.md), [WI-009](WI-009-implement-session-recovery.md)
- Status: See the [active work item index](README.md)

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

- [ ] A supported server file can be added from Files, inspected/removed in the composer, submitted and observed in server history/provider input.
- [ ] A local file follows a verified content-delivery contract, or the unavailable path is explicitly explained with pinned evidence; local paths are never silently treated as remote files.
- [ ] Unsupported types, missing files and oversized content fail with actionable feedback while preserving usable draft text/attachments.
- [ ] Attachments stay scoped to their draft and do not leak across connections/projects/sessions; failed or uncertain sends preserve the submitted payload identity without automatic resend.
- [ ] Settled admission clears only the submitted draft version, preserving edits made after submission; history does not duplicate attachment rendering on refresh.
- [ ] Contract probes, automated checks, x64 build and native composer/history acceptance pass for the documented supported subset.

## Verification requirements

Apply the [shared checks](README.md#shared-execution-and-verification-requirements). Test typed payload serialization, escaping/media bounds, draft ownership/versioning, file read failures, navigation races and lost acknowledgements. Use harmless disposable files and the deterministic provider fixture to inspect actual received attachments without paid inference. Exercise native picker cancel, add/remove, validation, navigation, reconnect and keyboard access. If server restrictions materially prevent MSG-02, record the blocker before claiming completion.

## Planning record

Defined on 2026-10-25. No implementation or runtime contract verification has started. Documentation validation is recorded in the active index.
