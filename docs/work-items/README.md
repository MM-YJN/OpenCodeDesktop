# Work Items

Product requirements: [Requirements overview](../requirements/overview.md). This directory tracks independently verifiable tasks. Product requirement IDs and work item IDs are separate; each work item links to its related requirements.

This is the active index for unfinished work, in intended execution order. Done items are listed in the [completed index](completed.md), grouped by completion year. Each item appears in exactly one index; work item files and IDs remain unchanged.

## Active Work

| ID | Task | Type | Status | Dependencies |
| --- | --- | --- | --- | --- |
| [WI-006](WI-006-implement-live-projects-sessions.md) | Open real projects and manage sessions | Implementation | Awaiting Acceptance | [WI-005](WI-005-verify-project-session-workflow.md), [WI-004](WI-004-implement-interactive-workspace-layout.md) |
| [WI-008](WI-008-implement-live-task-workflow.md) | Run tasks and respond to agent requests | Implementation | Ready — blocked pending WI-006 native acceptance | [WI-007](WI-007-verify-task-execution-workflow.md), [WI-006](WI-006-implement-live-projects-sessions.md) |
| [WI-009](WI-009-implement-session-recovery.md) | Recover connections and reopen existing work | Implementation | Ready | [WI-008](WI-008-implement-live-task-workflow.md) |

Current: WI-006 remains Awaiting Acceptance for native UI/manual checks. WI-007's isolated task-workflow investigation is complete; the user explicitly selected it while WI-006 acceptance remained open. Next: accept WI-006, then implement WI-008 using the verified task contract. WI-008 and WI-009 are defined and unstarted; completing the investigation does not authorize starting them.

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
