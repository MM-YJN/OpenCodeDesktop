# WI-014 Resolve native reliability and accessibility verification gaps

## Details

- Type: Implementation / verification
- Related requirements: [Interaction and reliability requirements](../requirements/overview.md#interaction-and-reliability-requirements), existing P0 task workflow
- Priority: P0
- Dependencies: [WI-013](WI-013-polish-shell-feature-ux.md)
- Status: See the [active work item index](README.md)

## Goal and baseline

Establish reliable native interaction before extending the workspace. WI-013 was accepted with explicit limits: UIAutomation-driven tree traversal caused native XAML fail-fast crashes in the prior Windows session; Narrator, reduced motion, 100%/150% scaling and several native task actions were not fully verified. Its accepted completion remains unchanged; this item owns the follow-up evidence and any reproducible fixes.

## Scope

- Reproduce project/session tree UI Automation traversal after a clean interactive Windows session. Compare physical keyboard, UIA and Narrator behavior; collect crash diagnostics if a failure persists and fix reproducible application defects.
- Verify keyboard-only project add/select/remove, tree expansion, session selection, Settings return, pane changes and dialogs. Restore useful focus and preserve drafts/reading position.
- Exercise native send, streaming, interrupt, permission Allow once/Save allow rule/Reject, form reply/cancel, startup restoration and reconnect using an isolated pinned service and deterministic provider fixture.
- Review 100%, 150% and 200% display scaling, minimum and target effective window sizes, themes/high contrast, reduced motion, long titles/paths, multiline drafts and long/tool-heavy histories.

## Exclusions and deliverables

No new product features, visual redesign, server-version upgrade or broad analyzer cleanup. Deliver focused fixes if needed, meaningful regressions for changed behavior, and a native acceptance matrix with environment, exact reproduction steps, screenshots/UIA evidence and crash findings. Do not attribute an unreproduced crash to an environment cause without evidence.

## Acceptance criteria

- [ ] In a clean session, repeated tree traversal using physical keyboard, UIA and Narrator completes without a crash; any original failure has a documented reproduction and resolution or evidence explaining why it cannot be reproduced.
- [ ] Keyboard users can reach and operate project/session controls, removal, Settings, panes, composer and requests, with meaningful names, visible focus and useful focus return.
- [ ] Native task submission, streaming, interruption, all supported permission decisions and form reply/cancel are observed against the controlled service, with correct resulting server state and no duplicate submission.
- [ ] Startup restoration and connection-loss recovery preserve ownership, drafts and attention access; uncertain delivery is not resent automatically.
- [ ] The size/scale/theme/long-content matrix is recorded with no inaccessible essential actions; reduced-motion behavior and Narrator announcements are observed.
- [ ] Required automated checks and x64 build pass, and native evidence supports each completed criterion. Unavailable checks remain explicit acceptance gaps.

## Verification requirements

Follow the [shared checks](README.md#shared-execution-and-verification-requirements) and the linked winapp workflow. Record Windows/WinUI/tool versions, package/output identity, display settings, fixture prerequisites and crash dump findings. Obtain a clean session through the user's normal session lifecycle when needed; do not automatically log off/restart the workstation. If it is unavailable, record the blocked check and unblock condition. No real provider subscription is required. Repeat only checks affected by a fix and the final critical workflow smoke scenario.

## Planning record

Defined on 2026-10-25. No implementation or native verification has started. Documentation validation is recorded in the active index.
