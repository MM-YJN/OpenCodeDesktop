# WI-010 Detect and add a running local OpenCode environment

## Details

- Type: Implementation
- Related requirements: CON-01, CON-03
- Dependencies: WI-002, WI-005; uses the WI-006 authenticated connection lifetime.
- Status: See the [completed work item index](completed.md).

## Goal and scope

On the Connections page, detect an installed, already-running supported local OpenCode service, retrieve its service password in memory, and verify its identity/authentication. Offer an explicit Add local environment action that creates or reuses its profile and connects without password copy/paste. Keep existing manual connection entry available.

Infrastructure owns executable discovery, bounded CLI commands and authenticated probes. Core owns detection/action state, duplicate profile handling and atomic connection promotion. App presents loading, detected, unavailable, failed and retry states. Detect on page activation and revalidate the current service address/password when the explicit action runs. Never retain detection passwords in presentation state, profiles, logs or storage. Cancel and invalidate late work on page exit and shutdown. Backend operations must not block the UI thread.

CLI evidence from this PC: desktop-bundled `opencode-cli.exe` reports `opencode v2.1.4`; `service status` returns the current local origin; `service get password` returns a password which authenticated `/api/info` successfully. Only these read-only commands and `--version` are permitted. Command output is untrusted and bounded; validate the version and loopback origin before using a retrieved password.

## Exclusions

Starting/stopping/reconfiguring services, installation, remote discovery, scanning arbitrary ports, pairing, credential persistence, and automatic background reconnection. Missing/inaccessible installations remain manually connectable. Do not inspect process environments or dump full service configuration.

## Acceptance and verification

- [x] Opening Connections shows asynchronous detection with an explicit refresh action and safe unavailable/error states. Accepted by the user on 2026-10-24.
- [x] Only a running supported service with a verified credential offers Add local environment; detection alone does not create profiles or switch servers.
- [x] The action retrieves/revalidates the service credential again, adds or reuses the matching profile and connects without exposing the password in the UI.
- [x] Existing connections survive failed/cancelled local switching; repeated actions cannot create duplicate profiles or overlapping attempts.
- [x] Navigation/shutdown cancels detection and prevents stale results from repopulating the page or starting a connection.
- [x] CLI commands have a total deadline, bounded stdout/stderr, hidden windows, argument-list construction, cancellation and cleanup.
- [x] Core/Infrastructure/Presentation tests pass with nonzero discovery and the x64 solution builds. Record a read-only discovery check against this PC's service and native UI limitations.

## Verification record

Implemented on 2026-10-24. `LocalEnvironment` owns safe metadata, busy/action state, duplicate-address reuse and generation/cancellation guards. Infrastructure searches the supported-version desktop bundle and standard per-user npm package locations, without launching PATH wrappers or scanning ports. Each executable must report the pinned version before any service configuration command is issued. The service origin must pass the existing loopback validator before any retrieved credential is used. Detection validates through `/api/info` then drops its temporary password; the explicit action retrieves and probes it again, and uses the existing atomic connection workflow. No inactive credential is retained. Manual connection entry remains available.

Connections activates detection on page entry, shows progress and safe status in a Local environment card, offers explicit refresh, and shows Add local environment only after successful detection. Already-connected candidates are labelled and disabled. The action reuses an existing matching profile or creates Local OpenCode, then connects with the retrieved credential without populating the PasswordBox. Leaving Settings/Connections and window shutdown cancel and invalidate work. Core/UI continuations stay on the owning synchronization context; executable discovery, process start/kill/wait, CLI output reads and HTTP probes run off the UI thread. Cancellation queues process termination to a worker so navigation does not synchronously kill a process tree on the UI thread.

Bounds: a 15-second total page operation; 10 seconds for each discovery/credential refresh; 5 seconds per CLI command; at most 8 deduplicated known candidates; at most 16,384 characters independently on stdout and stderr; 4,096 characters for the retrieved password; and a bounded 2-second exit wait after killing an owned helper. Only `--version`, `service status` and `service get password` are allowed by the command runner. It never dumps configuration, starts/stops/reconfigures a service, follows advertised non-loopback origins, or logs command output/errors. A controlled test-only CLI project exercises real subprocess cleanup without modifying a real service. The fixture has no external packages and its lock file is retained.

| Command | Result |
| --- | --- |
| `dotnet test --project tests/OpenCodeDesktop.Core.Tests/OpenCodeDesktop.Core.Tests.csproj` | Passed 56/56, no skips. New cases cover asynchronous loading, detection without profile mutation, credential refresh, duplicate profile reuse, unavailable/changed services, failed/cancelled switching, overlapping actions, navigation, late results, disposal and safe errors. |
| `$env:OPENCODE_LOCAL_DISCOVERY_TEST='1'; $env:OPENCODE_CONNECTION_TEST_EXECUTABLE=(Resolve-Path 'artifacts/wi006/runtime/package/bin/opencode.exe').Path; dotnet test --project tests/OpenCodeDesktop.Infrastructure.Tests/OpenCodeDesktop.Infrastructure.Tests.csproj` | Passed 68/68, no skips. Includes read-only discovery against this PC's installed service; existing isolated pinned-service cases; exact read-only command ordering; unsupported/stopped/remote/changed endpoints; password rejection; deadlines/cancellation; successful/failed CLI exits; stalled processes; stdout/stderr overflow and verification that owned helper processes exited. Mutating commands and full configuration dumps are rejected. |
| `dotnet test --project tests/OpenCodeDesktop.Presentation.Tests/OpenCodeDesktop.Presentation.Tests.csproj` | Passed 1/1, no skips (existing live workspace presentation regression). New local detection/action state is exercised through Core tests; native UI rendering is not claimed as tested. |
| `dotnet build OpenCodeDesktop.slnx -p:Platform=x64` | Passed, zero errors. Final incremental build reported one xUnit1051 test cancellation warning; earlier full builds also reported existing fixture/analyzer warnings and cancellation warnings on older connection tests. |
| `git diff --check` | Passed. |

Logs are under ignored `artifacts/wi010/` and contain no service credential output. The installed-service test found supported OpenCode 2.1.4 and independently refreshed/verified its service password; no profile was added to another app and the service was not reconfigured. Controlled subprocess tests left no fixture processes running. Local service discovery is Windows-specific; portable/custom CLI paths, system-wide npm layouts, inaccessible service credentials and unsupported versions fall back to manual connection entry.

The user accepted the implementation and explicitly requested completion on 2026-10-24, closing the remaining UI acceptance criterion. Native keyboard traversal, live-region delivery, page layout and the visible Add local environment interaction were not independently exercised by the agent; user acceptance does not change that verification limitation. No task-execution work was started. This documentation-only completion update required no additional build or test run; `git diff --check` passed.
