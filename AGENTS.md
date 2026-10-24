# Repository Guide

OpenCode Desktop is a native Windows client for OpenCode, built with WinUI 3 and C#/.NET. This guide defines module responsibilities and the intended dependency boundaries as the application grows.

## Modules

### `src/OpenCodeDesktop`

The Windows presentation layer and application entry point.

- Own application startup, window lifecycle, XAML views, view models, navigation, themes, and visual assets.
- Translate user actions into application operations and present results and errors.
- Compose services and connect Core abstractions to Infrastructure implementations at startup.
- Own Windows-specific UI integration, manifests, packaging, and publish profiles.
- Keep business rules and transport or persistence details out of views and code-behind.

App references Core and Infrastructure.

### `src/OpenCodeDesktop.Core`

The UI-independent application and business logic layer.

- Define application models, business rules, use cases, and interfaces for external services.
- Express operations involving OpenCode sessions, messages, and workspaces as those features are introduced.
- Remain independent of WinUI, Windows UI types, and concrete Infrastructure implementations.
- Keep HTTP transport, process management, and file access behind abstractions implemented outside this module.

Core must not reference App or Infrastructure.

### `src/OpenCodeDesktop.Infrastructure`

Implementations that communicate with external systems.

- Implement Core interfaces for OpenCode API communication and event streams.
- Own transport DTOs and map them to Core models at the boundary.
- Handle local OpenCode process management and settings or file persistence when introduced.
- Handle protocol details, serialization, resource cleanup, and external-system failures.

Infrastructure references Core and must not reference App or WinUI controls.

### `tests/OpenCodeDesktop.Core.Tests`

Fast, deterministic tests for Core behavior.

- Verify business rules, validation, and use-case behavior.
- Replace external dependencies with test doubles.
- Avoid network access, installed OpenCode services, and Windows UI dependencies.

### `tests/OpenCodeDesktop.Infrastructure.Tests`

Tests for Infrastructure implementations and their integration boundaries.

- Verify request construction, response mapping, streaming behavior, and failure handling.
- Prefer controlled HTTP handlers, local fixtures, and temporary storage.
- Keep tests requiring a real OpenCode service explicitly opt-in and document their prerequisites.
- Clean up any files or processes created by tests.

Both test projects use xUnit with Microsoft Testing Platform. Add meaningful tests as behavior is implemented.

## Dependency Direction

```text
App -> Core
App -> Infrastructure -> Core
Core.Tests -> Core
Infrastructure.Tests -> Infrastructure
```

This is the intended architecture. App is the composition root; Core defines abstractions and Infrastructure implements them. Avoid circular dependencies and do not introduce an App dependency into either library.

## Shared Configuration

- `OpenCodeDesktop.slnx`: solution membership and build platforms.
- `global.json`: selected .NET SDK and test runner.
- `Directory.Build.props`: shared build settings, analyzer configuration, and Nerdbank.GitVersioning reference.
- `version.json`: Git-based versioning and release conventions.
- `.editorconfig` and `.gitattributes`: formatting and line-ending conventions.
- Project files currently declare their own package versions. Preserve that convention unless intentionally migrating to central package management.
- Preserve package lock files and update them when dependencies change.

## Work Item Workflow

Track requirements and progress in the repository. Use [the requirements overview](docs/requirements/overview.md) for product scope, [the active work item index](docs/work-items/README.md) for unfinished work and execution order, and [the completed work item index](docs/work-items/completed.md) for Done items. Together, these indexes are the authoritative status record; each item must appear in exactly one of them. Write work items and their updates in English.

1. **Select and understand the work.** Read the active index first, then the selected work item, its related requirements, relevant dependencies, and existing code before making changes. Consult the completed index and historical work items only when needed to verify dependencies or understand relevant decisions; do not load the entire completed history by default. Follow the user's requested item; when asked to continue with the next task, use the active index's execution order and check that its dependencies are satisfied. Work on one item at a time.
2. **Make the task executable.** Before implementation, ensure the item has a clear goal, scope, exclusions, deliverables, observable acceptance criteria, and verification requirements. Use an investigation item when essential facts or external contracts must first be verified. Resolve routine implementation choices independently; ask about unresolved product decisions that materially affect scope or behavior.
3. **Record the start.** Change the index status from Ready to In Progress when execution begins. Keep status in the index rather than duplicating it in task files. Draft means the task still needs definition; Ready means it is sufficiently defined to execute.
4. **Implement within scope.** Follow the module boundaries in this guide and preserve unrelated changes. Keep internal execution steps in the work item or agent plan. Create separate work items only for independently verifiable increments, with stable IDs and explicit dependencies or parent links. Record material decisions in the work item and update the requirements overview if product scope changes.
5. **Verify and record evidence.** Run the checks appropriate to the change and the item's acceptance criteria. Record the actual commands, results, relevant manual checks, and remaining limitations in the work item. Check off only criteria supported by evidence; an unavailable check is not a passing check. Documentation-only changes do not require an App build or test run unless the item specifically requires them.
6. **Update the outcome.** Use Awaiting Acceptance when implementation is complete but required review or manual confirmation remains. Use Done only when all acceptance criteria are satisfied. When marking an item Done, move its row from the active index to the completed index under its completion year and record the completion date. Update the item's status link to the completed index and refresh the active index's next task. If blocked, record the reason and unblock condition in the item and annotate the active index without marking it complete. Summarize the deliverables, verification, and outstanding issues in the handoff.

Keep Draft, Ready, In Progress, and Awaiting Acceptance items in the active index, including blocked items with a blocker annotation. Keep that index focused on current priorities and execution order. Preserve work item filenames, paths, and IDs when archiving; move only index rows so existing links and dependencies remain valid. Never reuse an assigned ID; check both indexes and existing work item files when allocating a new one. If an item is reopened, move its row back to the active index with the appropriate unfinished status and update its status link in the same change. Do not duplicate current status fields in task files or maintain copies of the same row in both indexes.

Keep deliverables and their work item records consistent and, when committing, include related updates in the same commit where practical. Maintain concise decision and verification records rather than daily activity logs. Creating or updating a work item does not itself mean that its execution has started, and completing one item does not authorize starting unrelated work.

## Working Guidelines

- Inspect existing code before editing and preserve unrelated work.
- Place code in the module that owns its responsibility; introduce folders when there is actual code to organize.
- Follow the existing nullable-reference and formatting conventions.
- Keep the Core and Infrastructure libraries on their UI-independent target framework unless a concrete requirement justifies a change.
- Run relevant tests after behavior changes and build the Windows app after changes to XAML, startup, or packaging.

From the repository root:

```powershell
dotnet build OpenCodeDesktop.slnx -p:Platform=x64
dotnet test --project tests/OpenCodeDesktop.Core.Tests/OpenCodeDesktop.Core.Tests.csproj
dotnet test --project tests/OpenCodeDesktop.Infrastructure.Tests/OpenCodeDesktop.Infrastructure.Tests.csproj
```

Building the app requires Windows and the Windows SDK/tooling used by its project. A test run discovering zero tests is not evidence that application behavior has been validated.
