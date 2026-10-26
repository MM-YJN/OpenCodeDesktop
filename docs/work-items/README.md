# Work Items

Product requirements: [Requirements overview](../requirements/overview.md). This directory tracks independently verifiable tasks. Product requirement IDs and work item IDs are separate; each work item links to its related requirements.

This is the active index for unfinished work, in intended execution order. Done items are listed in the [completed index](completed.md), grouped by completion year. Each item appears in exactly one index; work item files and IDs remain unchanged.

## Active Work

| ID | Task | Type | Status | Dependencies |
| --- | --- | --- | --- | --- |
| [WI-019](WI-019-notify-completion-attention.md) | Notify on completion and required attention | Implementation | Ready | WI-014, WI-009, WI-012 |
| [WI-020](WI-020-manage-queued-inputs.md) | Inspect and manage queued inputs | Implementation | Ready | WI-014, WI-008, WI-009 |
| [WI-021](WI-021-fork-revert-session-history.md) | Fork sessions and expose staged revert | Implementation | Ready | WI-016, WI-018, WI-020 |

Next: WI-019. WI-018 was accepted as Done by the user on 2026-10-26 with its documented verification limitations. WI-019 through WI-021 remain Ready; no subsequent item has started. Follow the row order, checking dependencies before starting each item.

## Shared execution and verification requirements

The items below continue the OpenCode v2 / server 2.1.4 baseline; they do not authorize a version migration. For new backend operations, inspect the retained [contract provenance](../integration/contracts/README.md), verify the relevant contract against the pinned isolated binary, retain the required runtime OpenAPI slice and sanitized evidence, and document observed semantics and unobserved cases within the selected item. Do not assume the latest website matches 2.1.4. Contract verification is part of each feature, not a separate investigation item.

Keep application rules and abstractions in Core, HTTP/DTO/persistence implementations in Infrastructure, and native controls/presentation in App. Preserve connection/project/session ownership, cancellation generations, drafts, protected credentials and authoritative recovery. Do not retry mutations automatically after uncertain delivery.

When executing an item, run the following checks for changed behavior and native UI; record actual commands, counts, failures/skips, evidence paths and limitations in that item. Run suites sequentially to avoid shared build-output locks. A skipped live test or unavailable manual check is not passing evidence.

```powershell
dotnet test --project tests/OpenCodeDesktop.Core.Tests/OpenCodeDesktop.Core.Tests.csproj
dotnet test --project tests/OpenCodeDesktop.Infrastructure.Tests/OpenCodeDesktop.Infrastructure.Tests.csproj
dotnet test --project tests/OpenCodeDesktop.Presentation.Tests/OpenCodeDesktop.Presentation.Tests.csproj
dotnet build OpenCodeDesktop.slnx -p:Platform=x64
git diff --check
```

Use the repository-local [winapp workflow](../../.agents/skills/winapp-workflow/SKILL.md) for native implementation and verification, including API/control discovery, current packaged-binary launch, UI Automation, screenshots and crash diagnostics as applicable. Exercise new UI in light, dark and high contrast, at 1440x900 and 640x480 effective sizes, with keyboard access and accessible names/focus. Keep fixtures disposable and screenshots/dumps outside source control; exclude credentials and private content. Preserve package locks if dependencies change. Leave an item Awaiting Acceptance while required native or user review remains outstanding.

## Planning validation (2026-10-25)

Revised WI-015 and the workspace requirements to use the approved tab-based workspace and project Files overflow-menu entry. Saved the reference image and generation prompt in docs/design and linked them from WI-015 and the design system. WI-015 remains Ready; this documentation revision does not start implementation. Verification: PowerShell checked all 28 local document links across the four updated Markdown files and confirmed the saved concept image exists; git diff --check passed. No application build, behavior tests or runtime checks were run for this documentation-only revision.

Created WI-014 through WI-021 in the recommended order and marked them Ready without starting execution. Verified eight unique new IDs against both indexes and existing filenames, required definition sections, unchecked acceptance criteria and 56 local link targets. `git diff --check` passed. Requirements and completed-item records were unchanged. No application build, behavior tests, runtime probes or native checks were run for this documentation-only planning change.

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
