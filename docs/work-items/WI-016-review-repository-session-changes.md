# WI-016 Review repository and session changes

## Details

- Type: Implementation
- Related requirements: [REV-02 and workspace layout](../requirements/overview.md)
- Priority: P1
- Dependencies: [WI-015](WI-015-browse-preview-project-files.md), [WI-008](WI-008-implement-live-task-workflow.md)
- Status: See the [active work item index](README.md)

## Goal and baseline

Enable Details > Changes to inspect task results with an explicit comparison basis. The current tab is disabled and no VCS or turn-diff transport is implemented.

## Scope

- Verify pinned VCS info/status/review-base/diff operations and session-turn diff boundaries. Record available comparison modes, default bases, untracked/binary/rename behavior and non-repository errors.
- Provide a changed-file list and expanded diff in Details, with an explicit selector distinguishing supported repository comparisons from a selected session turn/range. Show base/turn identities and do not label all working-tree changes as agent changes.
- Support refresh, added/deleted files, empty/error/unavailable states and bounded large patches. Preserve selection/scroll per comparison and workspace; expand/restore the pane for review.
- Open Changes from verified conversation change summaries and allow selecting a file for preview through WI-015. Guard loads against connection/project/session and comparison changes.

## Exclusions and deliverables

No staging, commit, discard, merge, file editing or revert mutation. Deliver pinned contract/evidence, Core review models/state, Infrastructure mappings, native diff presentation and regression tests. A readable selectable unified diff is sufficient; a full editor component is not required.

## Acceptance criteria

- [ ] Repository and session-turn comparisons are independently selectable and clearly identify their actual basis/boundaries.
- [ ] Controlled added/modified/deleted and supported rename/binary/untracked cases produce correct file lists and honest content states; non-repositories remain usable.
- [ ] Conversation change links open the correct comparison/file; tab/pane navigation retains comparison selection and reading position.
- [ ] Refresh and late responses cannot mix workspaces, turns or bases; large diffs respect documented limits and remain responsive.
- [ ] Review performs no repository mutation and makes no unsupported claim that repository changes all belong to the selected task.
- [ ] Contract probes, automated checks, x64 build and native review/accessibility evidence pass.

## Verification requirements

Apply the [shared checks](README.md#shared-execution-and-verification-requirements). Use temporary Git repositories with known commits and working changes, plus controlled session turns; compare returned results with fixture expectations. Test query encoding, mapping, comparison identity, missing bases/turns, stale responses and patch limits. Native checks cover comparison changes, selectable patch text, long paths, large patches, Files handoff and keyboard selection. Keep user repositories unchanged.

## Planning record

Defined on 2026-10-25. No implementation or runtime contract verification has started. Documentation validation is recorded in the active index.
