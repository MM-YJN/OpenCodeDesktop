# WI-018 Search, rename, archive and restore sessions

## Details

- Type: Implementation
- Related requirements: [SES-03, SES-01 and PRJ-01](../requirements/overview.md#functional-requirements)
- Priority: P1
- Dependencies: [WI-014](WI-014-verify-native-reliability-accessibility.md), [WI-011](WI-011-multi-project-support.md), [WI-012](WI-012-persist-connections-workspace.md)
- Status: See the [active work item index](README.md)

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

- [ ] Search and pagination remain scoped to the owning connection/project/filter, including no matches and matching older sessions.
- [ ] Renaming updates authoritative tree/header metadata; invalid/failed/uncertain operations retain usable state and do not blindly retry.
- [ ] Archive hides an eligible session from active history, preserves server content and follows the defined selection/draft behavior; restore makes it available again.
- [ ] Known running/waiting/uncertain sessions cannot be archived through this UI; concurrent changes and stale responses are reconciled without lost attention access.
- [ ] Navigation, reconnect and restart handle renamed/archived/missing saved sessions with clear feedback and no cross-project results.
- [ ] Contract probes, automated checks, x64 build and native keyboard/search/archive acceptance pass.

## Verification requirements

Apply the [shared checks](README.md#shared-execution-and-verification-requirements). Probe temporary sessions with distinct titles, multiple pages and archive states. Test encoding/filter cursors, rename validation, races, selection persistence, draft retention and failed mutations. Native checks cover context actions, focus after rename/archive/restore, empty search, long titles and pending-attention navigation. Verify archive leaves messages/files intact through authoritative reads.

## Planning record

Defined on 2026-10-25. No implementation or runtime contract verification has started. Documentation validation is recorded in the active index.
