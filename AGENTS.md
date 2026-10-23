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
