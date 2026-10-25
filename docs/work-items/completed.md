# Completed Work Items

This index records Done items, grouped by completion year. Read the [active index](README.md) first for unfinished work and execution order; consult this history when checking dependencies or relevant decisions. Each item appears in exactly one index. Work item files retain their original paths and IDs.

## 2026

| ID | Task | Type | Status | Dependencies | Completed on |
| --- | --- | --- | --- | --- | --- |
| [WI-001](WI-001-verify-connection-contract.md) | Establish and verify the initial OpenCode v2 connection contract | Investigation | Done | None | 2026-10-24 |
| [WI-003](WI-003-implement-application-shell.md) | Define design tokens and implement the application shell | Implementation | Done | None | 2026-10-24 |
| [WI-004](WI-004-implement-interactive-workspace-layout.md) | Implement the interactive workspace layout and Settings navigation | Implementation | Done | [WI-003](WI-003-implement-application-shell.md) | 2026-10-24 |
| [WI-002](WI-002-implement-server-connection.md) | Connect to and switch between OpenCode server backends | Implementation | Done | [WI-001](WI-001-verify-connection-contract.md), [WI-003](WI-003-implement-application-shell.md), [WI-004](WI-004-implement-interactive-workspace-layout.md) | 2026-10-24 |
| [WI-005](WI-005-verify-project-session-workflow.md) | Verify how users open projects and sessions | Investigation | Done | [WI-001](WI-001-verify-connection-contract.md), [WI-002](WI-002-implement-server-connection.md) | 2026-10-24 |
| [WI-010](WI-010-detect-local-environment.md) | Detect and add a running local OpenCode environment | Implementation | Done | WI-002, WI-005; WI-006 connection lifetime | 2026-10-24 |
| [WI-007](WI-007-verify-task-execution-workflow.md) | Verify the complete task execution workflow | Investigation | Done | [WI-006](WI-006-implement-live-projects-sessions.md) (isolated investigation explicitly selected before native acceptance) | 2026-10-24 |
| [WI-006](WI-006-implement-live-projects-sessions.md) | Open real projects and manage sessions | Implementation | Done | [WI-005](WI-005-verify-project-session-workflow.md), [WI-004](WI-004-implement-interactive-workspace-layout.md) | 2026-10-24 |
| [WI-008](WI-008-implement-live-task-workflow.md) | Run tasks and respond to agent requests | Implementation | Done | [WI-007](WI-007-verify-task-execution-workflow.md), [WI-006](WI-006-implement-live-projects-sessions.md) | 2026-10-24 |
| [WI-009](WI-009-implement-session-recovery.md) | Recover connections and reopen existing work | Implementation | Done | [WI-008](WI-008-implement-live-task-workflow.md) | 2026-10-24 |
| [WI-011](WI-011-multi-project-support.md) | Manage a project list for each connection | Implementation | Done | [WI-006](WI-006-implement-live-projects-sessions.md), [WI-009](WI-009-implement-session-recovery.md) | 2026-10-24 |
| [WI-012](WI-012-persist-connections-workspace.md) | Persist connections and restore the last workspace on launch | Implementation | Done | [WI-011](WI-011-multi-project-support.md), [WI-010](WI-010-detect-local-environment.md), [WI-009](WI-009-implement-session-recovery.md) | 2026-10-25 |
| [WI-013](WI-013-polish-shell-feature-ux.md) | Polish the application shell and implemented feature UX | Implementation | Done | [WI-012](WI-012-persist-connections-workspace.md); existing shell and live workflows through WI-010 | 2026-10-25 |
| [WI-014](WI-014-verify-native-reliability-accessibility.md) | Resolve native reliability and accessibility verification gaps | Implementation / verification | Done | WI-013 | 2026-10-25 |
| [WI-015](WI-015-browse-preview-project-files.md) | Browse and preview project files | Implementation | Done | WI-014, WI-011 | 2026-10-25 |
