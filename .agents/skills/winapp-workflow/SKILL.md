---
name: winapp-workflow
description: Develop and verify Windows app UI changes with winapp CLI sample/API discovery, launch, UI Automation, screenshots, and crash diagnostics. Use for WinUI/XAML, Windows API, startup, and native UI verification work; not for UI-independent logic changes.
---

# Winapp Workflow

Use the installed `winapp` CLI to ground UI implementation and collect observable verification evidence. Run examples in PowerShell from the target repository. Follow applicable repository instructions and existing build/test requirements; use a work item only when the task or repository workflow calls for one.

Before running project commands, identify the executable app project, its framework, packaging model, supported architecture, and build configuration. Set these variables to actual values from that project; the paths and architecture below are illustrative:

```powershell
$appProject = './src/MyApp/MyApp.csproj'
$appProjectDirectory = Split-Path -Parent $appProject
$appArchitecture = 'x64'
```

The launch examples below target a .NET app project. For another framework or a build-output folder, consult `winapp run --help` and use the supported target form and the repository's build process.

## Discover Before Implementing

Check `winapp --version` and relevant command `--help` when options are uncertain. The CLI is in preview; these examples were checked against 0.7.1. Use `winapp --cli-schema` only when structured command discovery is useful.

Search for a suitable control, then fetch a scenario returned by that search:

```powershell
winapp find-ui "TabView" --max 1 --json
winapp find-ui --id gallery-tabview-1 --json
```

Samples are references, not proof that their APIs or dependencies exist in the target project. Choose samples compatible with its framework and dependencies (for example, XAML versus Reactor), and adapt them to its existing styles and architecture.

Resolve APIs against the executable app project explicitly, rather than an unrelated library or test project:

```powershell
winapp find-api "NavigationView" --project-dir $appProjectDirectory --json
winapp find-api check-property Microsoft.UI.Xaml.Controls.Grid RowSpacing --project-dir $appProjectDirectory --json
winapp find-api members Microsoft.UI.Xaml.Controls.Grid --project-dir $appProjectDirectory --json
```

The index depends on restored packages; use the normal project restore/build if assets are missing. Read `scope`, `caveats`, and missing-symbol results. An SDK-scope answer is not evidence about the project's references. If the CLI falls back to metadata from an SDK newer than the project's target, results can include unavailable APIs; confirm target/minimum-OS compatibility and compile rather than treating a successful lookup as sufficient validation.

## Build and Launch

Run the repository's relevant build and test commands. For an interactive smoke check, launch the app project explicitly and retain the returned PID:

```powershell
winapp run $appProject --arch $appArchitecture --detach --json
```

After edits, prefer a building `winapp run` when supported by the target. Use `--no-build` only after verifying that a successful build produced the current binary at the exact output path used by that launch, with matching configuration, Platform, and runtime identifier. Matching architecture alone is insufficient: solution builds and CLI launches can resolve different output directories, such as `bin/x64/Debug/...` versus `bin/Debug/...`, and reuse a stale binary. Check the build log/output path before reusing outputs; a successful launch does not prove the latest changes were loaded.

For packaged apps, launch can register a development package and install a missing matching Windows App Runtime. Preserve the project's existing packaging model, manifest, and references. Do not run `winapp init`, introduce a new template, or add packages merely to verify an already configured app. Do not use `--clean` for routine checks: it deletes saved application data, including settings needed for restoration scenarios.

## Verify Observable UI Behavior

Scope every command to the returned PID (or a specific window handle when multiple windows need disambiguation). Replace the example PID below with the actual value:

```powershell
$appProcessId = 12345
winapp ui inspect -a $appProcessId --interactive --json
```

`winapp ui inspect` exposes the UI Automation accessibility tree, not the live XAML visual tree. Layout panels, grid row definitions, and template parts such as raw-accessibility chevrons may be absent; `--interactive` also filters out noninteractive elements. For layout bugs, inspect the relevant XAML or code-created controls and, when needed, the control template in the project's resolved WinUI package (for example, `Microsoft.WinUI/Themes/generic.xaml`). Compare UIA bounds with screenshots to verify the result. If live XAML visual-tree inspection is required, use an available XAML diagnostics tool and report whether it was actually attached; do not describe a UIA dump as live XAML inspection.

Read the current tree before choosing selectors. Use an exposed AutomationId or a unique accessible name; inspect again after navigation or layout changes. Prefer UIA `invoke`, `set-value`, `get-value`, `get-property`, and `wait-for` over coordinate clicks and fixed delays. Consult each verb's help for its options. Add stable `AutomationProperties.AutomationId` values if relevant controls cannot be selected reliably; preserve useful accessible names.

Choose checks that prove the requested behavior, such as affected navigation, expansion/collapse, error feedback, or state restoration. Backend-dependent scenarios require a controlled compatible service or fixture; opening the shell alone does not validate them. Avoid submitting real transactions or changing saved service connections merely to test navigation.

Capture visual evidence when layout, themes, or XAML changed:

```powershell
$evidenceDirectory = Join-Path $env:TEMP ('winapp-evidence-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $evidenceDirectory -Force | Out-Null
winapp ui screenshot -a $appProcessId -o (Join-Path $evidenceDirectory 'app.png') --json
```

Inspect the resulting image as well as UIA state; neither alone proves all behavior. Input-injecting commands require an unlocked interactive desktop, and screenshot capture may restore or foreground a window. Report unavailable desktop checks as unverified. For explicitly selected clean-environment checks, consult the Sandbox guide and keep `--on sandbox` on both launch and subsequent UI commands; host PIDs and guest PIDs are separate scopes.

## Diagnose and Record

For startup or WinUI crashes, run a separate diagnostic launch:

```powershell
winapp run $appProject --arch $appArchitecture --debug-output
```

This captures debug messages and exceptions and performs WinUI stowed-exception triage on crashes. It occupies the debugger slot; do not combine it with another attached debugger or `--json`. Add `--symbols` when native stack detail is needed; debugger components and symbols may download on first use.

Record actual commands, observed results, evidence paths, metadata caveats, and remaining manual checks in the task handoff or an existing work item when applicable. Keep screenshots and UI dumps out of commits unless deliberately needed as repository evidence, and avoid recording credentials or private user content. Shut down instances started for verification without terminating an unrelated user instance. Package removal, certificate installation, release signing, and CI setup are separate tasks rather than routine UI verification steps.

## References

- [WinUI workflow](https://learn.microsoft.com/en-us/windows/apps/dev-tools/winapp-cli/guides/winui)
- [CLI reference](https://learn.microsoft.com/en-us/windows/apps/dev-tools/winapp-cli/usage)
- [UI Automation](https://learn.microsoft.com/en-us/windows/apps/dev-tools/winapp-cli/ui-automation)
- [Windows Sandbox execution](https://learn.microsoft.com/en-us/windows/apps/dev-tools/winapp-cli/sandbox-execution)
