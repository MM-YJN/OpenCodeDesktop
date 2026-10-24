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
