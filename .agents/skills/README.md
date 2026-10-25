# Repository Skills

The 21 Microsoft skills in this directory are vendored unchanged from [microsoft/winappCli](https://github.com/microsoft/winappCli) at commit [`6564e7adba6d339f2b06868baca050e54687a2b3`](https://github.com/microsoft/winappCli/commit/6564e7adba6d339f2b06868baca050e54687a2b3), imported on 2026-10-25. Their supporting reference files are included. The upstream MIT license is preserved in [upstream-LICENSE](upstream-LICENSE).

| Upstream directory at the pinned commit | Imported skill directories |
|---|---|
| [plugins/winapp/skills](https://github.com/microsoft/winappCli/tree/6564e7adba6d339f2b06868baca050e54687a2b3/plugins/winapp/skills) | `winapp-find-api`, `winapp-find-ui`, `winapp-frameworks`, `winapp-identity`, `winapp-manifest`, `winapp-maui`, `winapp-package`, `winapp-performance`, `winapp-sandbox`, `winapp-setup`, `winapp-signing`, `winapp-troubleshoot`, `winapp-ui-automation`, `winui-devtools` |
| [plugins/winui/agent-plugin/skills](https://github.com/microsoft/winappCli/tree/6564e7adba6d339f2b06868baca050e54687a2b3/plugins/winui/agent-plugin/skills) | `winui-code-review`, `winui-design`, `winui-dev-workflow`, `winui-packaging`, `winui-setup`, `winui-ui-testing`, `winui-wpf-migration` |

## Local Workflow Assessment

Keep [winapp-workflow](winapp-workflow/SKILL.md) as a small repository integration guide. Upstream now covers command discovery, API/control lookup, packaged launch, crash diagnosis, UIA testing, screenshots, and live XAML inspection. The local skill delegates those procedures and retains the repository's safeguards for exact build-output reuse, minimum-OS/API compatibility, saved settings and service connections, controlled backend fixtures, and verification evidence. It remains the entry point linked by `AGENTS.md` and the work item index.

## Updating the Import

Resolve the desired upstream commit and enumerate both source directories. Use the skill-installer helper with `--repo microsoft/winappCli --ref <commit> --dest <temporary-directory> --path <each-source-skill-directory>`; it expects individual directories containing `SKILL.md`, not either parent collection. Review and replace only the imported directories, preserving the local `winapp-workflow` skill. Include supporting files and refresh the upstream license, commit, date, and inventory here. Verify imported files against that commit and validate skill frontmatter and local reference links. Review upstream changes against repository instructions before adopting new workflow requirements.

These are instruction/reference files; importing them does not install WinApp CLI, Windows SDKs, or a plugin runtime. Skill discovery should pick them up on the next turn. Documentation-only updates do not require an app build or test run.
