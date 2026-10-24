# Work Items

Product requirements: [Requirements overview](../requirements/overview.md). This directory tracks independently verifiable tasks. Product requirement IDs and work item IDs are separate; each work item links to its related requirements.

This is the active index for unfinished work, in intended execution order. Done items are listed in the [completed index](completed.md), grouped by completion year. Each item appears in exactly one index; work item files and IDs remain unchanged.

## Active Work

| ID | Task | Type | Status | Dependencies |
| --- | --- | --- | --- | --- |
| [WI-012](WI-012-persist-connections-workspace.md) | Persist connections and restore the last workspace on launch | Implementation | Ready | [WI-011](WI-011-multi-project-support.md), [WI-010](WI-010-detect-local-environment.md), [WI-009](WI-009-implement-session-recovery.md) |
| [WI-013](WI-013-polish-shell-feature-ux.md) | Polish the application shell and implemented feature UX | Implementation | Ready | [WI-012](WI-012-persist-connections-workspace.md); existing shell and live workflows through WI-010 |

Next: [WI-012 Persist connections and restore the last workspace on launch](WI-012-persist-connections-workspace.md).

## Working Agreements

- Maintain current status only in the active and completed indexes: Draft → Ready → In Progress → Awaiting Acceptance → Done. Keep unfinished items here, including blocked items with an annotation.
- Read this index first, then the selected item and relevant dependencies. Consult completed history only as needed.
- Ready means the goal, scope, acceptance criteria, and dependencies are clear; execution has not necessarily started.
- Work on one item at a time. Update its status when starting. If blocked, record the reason and unblock condition in the work item and annotate the index.
- Keep requirements, acceptance checklists, key decisions, and verification records in the work item. Do not maintain daily activity logs.
- Record product decisions made during execution in the work item. Update the requirements overview when product scope changes.
- Leave incomplete criteria and criteria requiring manual confirmation unchecked. Mark an item Done only when all acceptance criteria are satisfied.
- On completion, move the row to the completed index under its completion year, record the completion date, update the item's status link, and refresh Next. Preserve the item file's path and ID. On reopening, move the row back here with the appropriate unfinished status and update its status link.
- Where practical, include deliverables and their work item updates in the same commit.
- Keep assigned IDs stable regardless of ordering or completion; never reuse them. Check both indexes and existing item files before assigning a new ID. Create separate items only for work requiring independent acceptance, and record dependencies or the parent item.
