# WI-021 Fork sessions and expose staged revert

## Details

- Type: Implementation
- Related requirements: [SES-04, REV-02 and interaction reliability](../requirements/overview.md)
- Priority: P1
- Dependencies: [WI-016](WI-016-review-repository-session-changes.md), [WI-018](WI-018-organize-session-history.md), [WI-020](WI-020-manage-queued-inputs.md)
- Status: See the [active work item index](README.md)

## Goal and baseline

Let users branch from supported history boundaries and review/revert work with explicit effects. No fork/revert mutations currently exist in the client; Changes and queue visibility provide the context needed to explain these actions.

## Scope

- Verify pinned fork and staged revert stage/clear/commit contracts, supported message boundaries, copied history/session relationships, effects on files/history/inbox, stage persistence, conflicts and repeated/stale mutations. Record exact semantics before implementing user-facing descriptions.
- Offer Fork from supported loaded history boundaries. Create and load the new server session within its actual owning project; retain the original session/draft and select the fork only after successful authoritative loading.
- Offer staged revert only for an inactive session with no pending interactions, queued inputs or unresolved delivery, revalidated before mutation. Expose a server-backed preview in Changes showing affected turns/files and the precise effects established by probes.
- Keep stage, clear and commit distinct. Require an explicit final user action after preview for commit; do not imply that staging is non-mutating unless runtime evidence proves that. Explain any immediate stage effects before staging.
- On clear/commit/fork success, reconcile metadata, history, changes, inbox and attention. On conflicts/lost responses/reconnect, re-query staged/server state and prevent another mutation until the outcome is understood.

## Exclusions and deliverables

No cross-project session movement, worktree creation, arbitrary Git reset/checkout, automatic commit or guarantees of undo beyond the verified server contract. Deliver sanitized pinned evidence, Core history-operation models/state, Infrastructure mapping, native boundary/preview actions and meaningful regressions. If the contract cannot establish safe effects or recoverable stage identity, record a blocker rather than expose an ambiguous destructive control.

## Acceptance criteria

- [ ] Forking from each supported boundary yields the verified history/session relationship; original content/drafts remain usable and failures preserve prior selection.
- [ ] Unsupported boundaries and active/waiting/queued/uncertain states disable revert with a clear reason; server races are reconciled.
- [ ] Before stage/commit, users can see the exact target and verified effects; Changes shows an authoritative preview and exposes distinct clear/commit actions.
- [ ] Stage, clear and commit fixture outcomes match the documented file/history/inbox effects; no unrelated project/files are changed.
- [ ] Reconnect/restart, external edits, stale targets and uncertain responses cannot cause blind replay or falsely report a completed revert.
- [ ] Contract probes, controlled regressions, x64 build and native fork/preview/clear/explicit-commit acceptance pass.

## Verification requirements

Apply the [shared checks](README.md#shared-execution-and-verification-requirements). Use only disposable repositories and deterministic task histories with known contents, clean/dirty states and multiple boundaries. Capture before/stage/clear/commit file hashes, history, queue and staged-state reads; exercise external changes and stale IDs. Test mutation state machines, ownership, blocked conditions, failed fork loading and lost acknowledgements. Native checks must demonstrate intelligible effects, keyboard access, focus after actions and a reviewable preview before commit; use no real user repository for destructive probes.

## Planning record

Defined on 2026-10-25. No implementation or runtime contract verification has started. Documentation validation is recorded in the active index.
