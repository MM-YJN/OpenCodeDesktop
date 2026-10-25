---
name: winapp-workflow
description: Apply OpenCode Desktop's repository-specific build, launch, and evidence requirements for WinUI/XAML, Windows API, startup, and native UI verification work. Use alongside the vendored Microsoft skills; not for UI-independent logic changes.
---

# OpenCode Desktop Winapp Workflow

Use this as the repository integration guide for the vendored Microsoft skills. Read only the skills relevant to the task:

| Task | Skill |
|---|---|
| Control samples and API discovery | [winapp-find-ui](../winapp-find-ui/SKILL.md), [winapp-find-api](../winapp-find-api/SKILL.md) |
| XAML layout, styling, themes, accessibility | [winui-design](../winui-design/SKILL.md) |
| Build, packaged launch, startup crashes | [winui-dev-workflow](../winui-dev-workflow/SKILL.md), [winapp-troubleshoot](../winapp-troubleshoot/SKILL.md) |
| UIA actions, screenshots, end-to-end verification | [winapp-ui-automation](../winapp-ui-automation/SKILL.md), [winui-ui-testing](../winui-ui-testing/SKILL.md) |
| Live XAML tree, properties, bindings, source | [winui-devtools](../winui-devtools/SKILL.md) |
| Explicit clean-environment checks | [winapp-sandbox](../winapp-sandbox/SKILL.md) |

Follow `AGENTS.md` for module boundaries, build/test commands, and when work items apply. The upstream skills are general guidance; preserve this existing application's architecture and packaging. Do not scaffold, initialize, add dependencies, install certificates, or change release configuration merely to perform a smoke check.

## Project and API Scope

Run from the repository root and identify the current app configuration from `src/OpenCodeDesktop/OpenCodeDesktop.csproj`, `global.json`, and `Directory.Build.props`. Scope API discovery to the executable app rather than Core, Infrastructure, or tests:

```powershell
winapp find-api NavigationView --project-dir ./src/OpenCodeDesktop --json
```

Read metadata `scope`, `caveats`, and missing-symbol results. SDK fallback, especially to a newer SDK, does not prove availability in this project's references or on its minimum OS. Choose samples compatible with the project's XAML framework and dependencies, then compile the result.

## Build and Launch the Current Binary

Run the repository's relevant build/tests. Launch the app project explicitly, retaining the returned PID and execution target:

```powershell
winapp run ./src/OpenCodeDesktop/OpenCodeDesktop.csproj --arch x64 --detach --json
```

Check installed command help when options are uncertain. Prefer a building launch after edits. Use `--no-build` only when a successful build produced the current binary at the exact launch path with matching configuration, Platform, and runtime identifier. A solution build under `bin/x64/Debug/...` may differ from a CLI launch under `bin/Debug/...`; matching architecture and a successful launch alone do not prove the latest changes loaded.

Preserve saved settings and service connections. Avoid `--clean` for routine verification; it deletes application data needed by restoration scenarios. Close only instances started for verification.

## Verify and Record Evidence

Use current selectors and keep PID/HWND and execution target together. UIA inspection is the accessibility tree; layout panels and template parts can be absent. Use `winui-devtools` for actual live XAML inspection, and report whether attachment succeeded. When runtime inspection is unavailable, compare source/templates, UIA bounds, and screenshots and state the limitation.

Choose checks that prove the requested behavior. Backend-dependent scenarios require a controlled compatible service or fixture; opening the shell alone does not validate them. Avoid real transactions or changing saved service connections solely to test navigation. Keep fixtures disposable and inspect captured images as well as UIA state.

Record actual commands, observed results, evidence paths, metadata caveats, and remaining checks in the handoff or the selected work item. Keep screenshots, dumps, credentials, and private content out of commits. An unavailable interactive desktop or runtime attachment is an unverified check, not a pass.
