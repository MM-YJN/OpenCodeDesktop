# WI-002 Connect to and switch between OpenCode server backends

## Details

- Type: Implementation
- Related requirements: CON-01, CON-02
- Priority: P0
- Dependencies: WI-001 completed and its target/support boundary accepted; WI-003 application shell completed and accepted; WI-004 interactive layout and Settings navigation
- Status: See the [completed work item index](completed.md)

## Goal and contract

Allow a user to manage multiple named local OpenCode backends, explicitly connect or switch using a password, and see readiness or an actionable failure without blocking the WinUI thread. Implement the accepted [connection concept](../design/server-connections-concept.png) and only the verified [2.1.4 connection contract](../integration/opencode-connection-contract.md). The user approved this expanded scope on 2026-10-24.

## Scope and responsibilities

- Core: UI-independent address validation, connection result/state models, supported-version policy, and an external connection service abstraction. Keep transport details outside Core.
- Infrastructure: injected `HttpClient` implementation; Basic authentication with username `opencode`; `GET /api/info`; DTO validation and mapping; five-second total deadline, cancellation, failure classification, and resource cleanup. Disable automatic redirects. Do not log credentials or treat returned server paths as client paths.
- App: replace the WI-004 Settings > Connections preview with address input, masked password input, connect/cancel actions, disconnected/connecting/ready/failure presentation, and actionable messages. Do not restore a standalone Connection navigation destination. Reuse the shell's design tokens and update its persistent connection-status region. Compose implementations at startup. Keep the last entered address available for correction and credentials only in memory for the active connection flow. Keep offline layout-preview state separate from real connection state.
- Tests: Core validation/policy with no network; Infrastructure controlled HTTP handlers for request construction, response parsing, cancellation, timeout, and error mapping. The real-service verification is opt-in.

### Multiple-backend behavior

- Maintain named profiles (stable ID, name, normalized origin) for this app lifetime only. Start with no configured profiles; show an actionable empty state. Add, edit, and remove inactive profiles without persisting settings or passwords. Reject empty names and duplicate origins. Disconnect before editing or removing an active profile.
- Add a quick selector above Projects showing the active server and origin, configured targets, and Manage connections. Choosing an inactive target opens its Connections editor for password entry; it never sends credentials automatically. Settings contains the profile list, active state, editor, explicit Connect & switch, cancel, and disconnect actions.
- Keep exactly one active backend. Probe a replacement before promoting it; failure, validation errors, and cancellation retain the previous active backend. Show target progress/error separately from active status, and disable profile mutations and additional connect actions during an attempt. Cancel/shutdown invalidate late completion; no automatic retry.
- Clear password controls at completion, editor changes, and leaving Settings. Do not copy passwords into profiles, state notifications, status text, or diagnostics. In-flight credentials exist only for the probe.
- Preserve the independent fictional sample workspace and drafts; clearly label it as sample data even after successful server readiness. No sample project, task, permission, or execution is attached to a live server. Actual server-scoped projects/sessions and switching with live running tasks require later integration contracts.
- Follow the system theme and native accessible controls while matching the concept's hierarchy, spacing, cards, and active-state emphasis. Keep status and pending attention visible in Settings. Remote/TLS support and profile persistence remain excluded.

The supported origin is `http://127.0.0.1:<explicit-port>` with optional trailing slash. A successful probe requires HTTP 200, required fields of the correct types, and exact version `2.1.4`. Ignore unknown response properties without accepting another version. One attempt runs at a time; cancellation and completion cannot overwrite the result of a newer attempt.

## Exclusions

No discovery, installation, server startup, persistence, pairing, credential storage, unauthenticated mode, hostname/IPv6/remote/TLS support, project selection, sessions, prompts, events, or automatic retries/reconnection. This increment implements the connection slice of CON-01/CON-02; it does not prove compatibility of later business operations.

## Deliverables

Core connection abstractions and rules, Infrastructure client implementation, minimal accessible WinUI connection view and composition, meaningful tests, and verification records in this item. Update the integration contract only when new evidence justifies a documented change.

Retain the concept PNG and its generation prompt in `docs/design/`; update the design system to describe the implemented selector, profile lifecycle, and switch semantics.

## Observable acceptance criteria

- [x] Accepted address inputs normalize consistently; unsupported or malformed inputs produce validation messages before any network request. Reject user information, non-root paths, queries, fragments, implicit/zero/out-of-range ports, and unsupported hosts or schemes.
- [x] An explicit connect action sends exactly the documented GET, Accept header, and Basic header without a body or credentials in the URL. Redirects are not followed.
- [x] The pinned response produces Ready and displays the entered server origin and reported version. No project or model readiness is implied.
- [x] Other versions, missing required fields, wrong field types, malformed/non-JSON bodies, and non-200 statuses never produce Ready. Unknown extra fields do not fail an otherwise valid pinned response.
- [x] Validation failure, authentication failure, connection refusal, deadline timeout, service starting/unavailable, initialization failure, and incompatible response have distinct actionable results. Handle other HTTP/transport failures without an unhandled exception.
- [ ] The view stays responsive; cancel ends an attempt without reporting a timeout. Repeat connect actions cannot create overlapping attempts or stale state updates.
- [ ] Password input is masked; no credentials are persisted or included in diagnostic output. Address and messages have accessible names and keyboard operation.
- [x] Users can add, edit, remove, and select multiple named in-memory profiles. Empty names, invalid origins, and duplicate origins are rejected; active profiles require disconnect before edit/removal.
- [ ] The Projects selector and Settings list agree on the active backend and show its origin/version. Selecting a target opens password entry. Successful switching promotes only the target; failed/cancelled switching retains the previous backend and sample drafts.
- [ ] Connecting disables overlapping attempts and profile mutation; shutdown/cancellation prevents late promotion. Password controls clear after completion, navigation away, and editor changes. Restarting starts with no profiles or credentials.
- [ ] Core and Infrastructure tests pass with nonzero test discovery; the x64 Windows App builds; required manual interaction checks and an opt-in connection against the pinned isolated server are recorded.

## Verification requirements

Use controlled HTTP handlers for 200 valid/invalid bodies, 401, 400/404, 500/503, redirects, unsupported version, stalled response/body reads, cancellation, and transport exceptions. Verify secret exclusion through the actual diagnostics path if diagnostics are introduced. Core tests cover the complete address boundary and version policy.

```powershell
dotnet test --project tests/OpenCodeDesktop.Core.Tests/OpenCodeDesktop.Core.Tests.csproj
dotnet test --project tests/OpenCodeDesktop.Infrastructure.Tests/OpenCodeDesktop.Infrastructure.Tests.csproj
dotnet build OpenCodeDesktop.slnx -p:Platform=x64
```

Record manual keyboard, masked input, responsive connect/cancel, correction/retry, and readiness/error presentation checks. Reuse the WI-001 isolated setup for the opt-in live-server check; do not require provider accounts or production projects.

## Implementation and verification record

### Multi-backend concept review (2026-10-24)

The user requested considering multiple OpenCode server backends and switching between them before implementation. Reviewed the original `docs/requirements/desktop-concept.png`, current XAML/design-system documentation, and the existing WI-004 PaneMotion review build's workspace and Settings > Connections screens. The running review build uses dark native WinUI controls and a disabled single-address connection form.

Created [a light-theme connection concept](../design/server-connections-concept.png) using the original PNG as its visual reference. It proposes a quick server selector above Projects, management inside Settings > Connections, explicit active-server identity, and a Connect & switch action that retains the current backend until the replacement is ready. The image uses illustrative local endpoints on ports 4096 and 4097; it is not implementation or live-connection evidence. The [generation prompt](../design/server-connections-concept.prompt.txt) is retained for iteration.

The user subsequently approved the concept and requested implementing multiple backends and switching. The expanded scope above defines in-memory profile lifetime and atomic connection switching; real project/session switching and running-task semantics remain excluded. No remote/TLS support, profile persistence, or credential storage contract was added.

### Implementation (2026-10-24)

Implemented Core connection models, strict address normalization, exact version policy, named profiles, and a single cancellable connection attempt. Only successful probes promote the target; failed/cancelled attempts retain the prior active server, and disposal prevents late promotion. Passwords are arguments to the service only, never profile/state fields.

Implemented an injected HTTP client with per-request UTF-8 Basic authentication, no redirects/cookies/proxy, `/api/info`, five-second total deadline including body reads, a 64 KiB response limit, required-field validation, and safe client-authored outcome messages. Server paths and advertised URLs are never followed or opened locally. No diagnostics/logging path was introduced.

Implemented Settings profile cards, add/edit/remove/save, masked password entry, explicit connect/switch, progress/cancel, disconnect, active-state emphasis, and the Projects selector. The editor scrolls with its action row pinned below it. The persistent strip shows real active name/origin/version while explicitly labelling independent sample workspace data. The concept PNG and full generation prompt are saved under `docs/design/`. Updated requirements and design-system documentation.

### Verification evidence (2026-10-24)

Commands run from the repository root:

```powershell
dotnet test --project tests/OpenCodeDesktop.Core.Tests/OpenCodeDesktop.Core.Tests.csproj
dotnet test --project tests/OpenCodeDesktop.Infrastructure.Tests/OpenCodeDesktop.Infrastructure.Tests.csproj
dotnet test --project tests/OpenCodeDesktop.Presentation.Tests/OpenCodeDesktop.Presentation.Tests.csproj
dotnet build OpenCodeDesktop.slnx -p:Platform=x64 -v:minimal
git diff --check
```

- Core: **35 passed**, zero failures/skips. Coverage includes address/port boundaries, exact version policy, profile validation and identity, duplicate normalized origins, active-profile guards, successful/failed/cancelled switching, overlapping-attempt gating, late completion after cancel/disposal, missing password, and safe handling of external failures.
- Infrastructure normal run: **30 passed, 1 skipped**, zero failures. The skipped case is explicitly opt-in. Controlled tests cover GET/Accept/UTF-8 Basic/body absence, required response types, invalid/non-JSON/oversized bodies, extra fields, unsupported version, 401/400/404/403/500/503/302, refusal/transport errors, total deadline and user cancellation during headers/body, response-stream disposal, and the production client's refusal to follow a real loopback redirect.
- Infrastructure opt-in run: **31 passed**, zero failures/skips. Set `OPENCODE_CONNECTION_TEST_EXECUTABLE` to `C:/Users/jinyi/AppData/Local/Temp/ocd-wi002-cli/package/bin/opencode.exe`, then run the same Infrastructure test command. The separately downloaded archive/executable matched WI-001's SHA-256 values. `LiveConnectionTests` verifies the executable hash, launches two owned loopback servers with isolated home/config/data/cache/state/temp/workspace directories and disposable passwords, verifies readiness/version, rejects a wrong-password switch without changing active identity, switches successfully, and disconnects. It kills its owned process trees and deletes its scratch directories. It needs the pinned Windows executable but no provider account, global installation, or production workspace. Without the environment variable it skips, so ordinary tests never launch an installed server.
- Presentation: **5 passed**, zero failures/skips; sample workspace state regressions remain covered.
- x64 solution build: **0 warnings, 0 errors**. No package dependencies were added and lock files were preserved. `git diff --check` passed.

One address test exposed `Uri` omitting explicit default port 80 from display; normalized display now always includes the validated port. Initial live-test cleanup exposed delayed release of Bun's mapped DLL after process exit; cleanup normalizes owned-file attributes and retries deletion for up to two seconds. The final opt-in run passed cleanup, and the two initial failed-run scratch folders were subsequently removed. These fixes are included in the final tests.

### Runtime review and remaining acceptance

Used the computer-use skill with separate unpackaged/self-contained review builds, preserving repository packaging settings:

```powershell
dotnet build src/OpenCodeDesktop/OpenCodeDesktop.csproj -p:Platform=x64 -p:WindowsPackageType=None -p:WindowsAppSDKSelfContained=true -p:OutputPath=C:/Users/jinyi/AppData/Local/Temp/OpenCodeDesktop-WI002-Review/ -v:minimal
Start-Process -FilePath 'C:/Users/jinyi/AppData/Local/Temp/OpenCodeDesktop-WI002-Review/OpenCodeDesktop.exe' -WorkingDirectory 'C:/Users/jinyi/AppData/Local/Temp/OpenCodeDesktop-WI002-Review'
```

The review build passed with **0 warnings, 0 errors**. Launch via the computer-use app-ID resolver initially selected an older packaged app; its screenshots were excluded. Launching the explicit review executable with `Start-Process` produced the intended process-backed window. The helper sometimes returned a stale accessibility tree immediately after input, so later observations/screenshots established the outcomes below.

- Observed real initial connection through the form to an isolated pinned server, correct origin/version/active status, masked password entry, and an empty password field afterward. The active profile's editor was disabled until disconnect.
- Added Sandbox on a second isolated server, submitted an incorrect disposable password, observed the actionable authentication failure and the first backend remaining active, corrected the password, and observed Sandbox becoming active in both Settings and the persistent strip. Returning to the workspace preserved the exact sample draft, selected session, and two sample attention requests. No sample operations were sent to a server. The independently owned UI-review server helper later reached its bounded lifetime, exited successfully, and removed its isolated directories; no review server remains running.
- Observed fresh startup without profiles or credentials in the later review build. Verified pointer activation of the server menu and Manage connections navigation, saving an inactive profile without connecting, its Not connected menu entry, compact editor, and pinned Save/Connect/Cancel controls. A selectable TextBlock initially intercepted the selector's click; disabling selection fixed pointer activation. The final source also explicitly places the menu below the selector and uses separate lines for name and origin; this final placement refinement is build-verified, not separately runtime-verified.
- Observed masked disposable input before leaving Settings. All navigation/editor clear paths are implemented, but the complete password-clear interaction matrix has not been manually established. A later target-selection check was interrupted by concurrent user input and was not counted as a pass.

Remaining manual acceptance: full keyboard/focus traversal and target selection from the dropdown; password clearing across all editor/section/window transitions; stalled-request UI responsiveness/cancel/correction and shutdown while connecting; minimum-window/enlarged-text/scaling layout, light/high-contrast appearance, and screen-reader announcements. Controlled Core/Infrastructure tests cover cancellation, deadlines, gating, and late results but do not replace those UI checks. No OS theme/accessibility/security settings were changed. Runtime screenshots were inspected inline; no durable screenshot files were saved. The repository PNG is the accepted design concept, not runtime proof.

At implementation handoff, combined criteria requiring these manual checks remained unchecked and the item awaited user acceptance. Builds and automated tests alone did not establish those checks.

### Completion (2026-10-24)

The user explicitly requested marking WI-002 Done after the implementation handoff. This accepts closure with the verification limitations recorded above; unperformed manual checks remain documented and unchecked rather than being represented as passed. Moved the row to the completed index under 2026, dated 2026-10-24, and updated this item's status link. No subsequent work item is defined or started.

Delivered multiple in-memory local backend profiles, explicit connection and atomic switching, Settings management and Projects selector UI, safe connection-state presentation, controlled tests and an opt-in two-server check, the saved concept image/prompt, and updated design/requirements documentation. Recorded verification remains 35 Core tests, 31 Infrastructure tests including the opt-in case, and 5 presentation tests passed; the x64 build passed with zero warnings/errors. This closure changes documentation only; no build or tests were rerun.
