# WI-002 Connect to the pinned existing OpenCode server

## Details

- Type: Implementation
- Related requirements: CON-01, CON-02
- Priority: P0
- Dependencies: WI-001 completed and its target/support boundary accepted; WI-003 application shell completed and accepted; WI-004 interactive layout and Settings navigation
- Status: See the [work item index](README.md)

## Goal and contract

Allow a user to enter an existing local OpenCode server address and password, explicitly connect, and see readiness or an actionable failure without blocking the WinUI thread. Implement only the accepted [2.1.4 connection contract](../integration/opencode-connection-contract.md).

## Scope and responsibilities

- Core: UI-independent address validation, connection result/state models, supported-version policy, and an external connection service abstraction. Keep transport details outside Core.
- Infrastructure: injected `HttpClient` implementation; Basic authentication with username `opencode`; `GET /api/info`; DTO validation and mapping; five-second total deadline, cancellation, failure classification, and resource cleanup. Disable automatic redirects. Do not log credentials or treat returned server paths as client paths.
- App: replace the WI-004 Settings > Connections preview with address input, masked password input, connect/cancel actions, disconnected/connecting/ready/failure presentation, and actionable messages. Do not restore a standalone Connection navigation destination. Reuse the shell's design tokens and update its persistent connection-status region. Compose implementations at startup. Keep the last entered address available for correction and credentials only in memory for the active connection flow. Keep offline layout-preview state separate from real connection state.
- Tests: Core validation/policy with no network; Infrastructure controlled HTTP handlers for request construction, response parsing, cancellation, timeout, and error mapping. The real-service verification is opt-in.

The supported origin is `http://127.0.0.1:<explicit-port>` with optional trailing slash. A successful probe requires HTTP 200, required fields of the correct types, and exact version `2.1.4`. Ignore unknown response properties without accepting another version. One attempt runs at a time; cancellation and completion cannot overwrite the result of a newer attempt.

## Exclusions

No discovery, installation, server startup, persistence, pairing, credential storage, unauthenticated mode, hostname/IPv6/remote/TLS support, project selection, sessions, prompts, events, or automatic retries/reconnection. This increment implements the connection slice of CON-01/CON-02; it does not prove compatibility of later business operations.

## Deliverables

Core connection abstractions and rules, Infrastructure client implementation, minimal accessible WinUI connection view and composition, meaningful tests, and verification records in this item. Update the integration contract only when new evidence justifies a documented change.

## Observable acceptance criteria

- [ ] Accepted address inputs normalize consistently; unsupported or malformed inputs produce validation messages before any network request. Reject user information, non-root paths, queries, fragments, implicit/zero/out-of-range ports, and unsupported hosts or schemes.
- [ ] An explicit connect action sends exactly the documented GET, Accept header, and Basic header without a body or credentials in the URL. Redirects are not followed.
- [ ] The pinned response produces Ready and displays the entered server origin and reported version. No project or model readiness is implied.
- [ ] Other versions, missing required fields, wrong field types, malformed/non-JSON bodies, and non-200 statuses never produce Ready. Unknown extra fields do not fail an otherwise valid pinned response.
- [ ] Validation failure, authentication failure, connection refusal, deadline timeout, service starting/unavailable, initialization failure, and incompatible response have distinct actionable results. Handle other HTTP/transport failures without an unhandled exception.
- [ ] The view stays responsive; cancel ends an attempt without reporting a timeout. Repeat connect actions cannot create overlapping attempts or stale state updates.
- [ ] Password input is masked; no credentials are persisted or included in diagnostic output. Address and messages have accessible names and keyboard operation.
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

Not started. WI-001 and WI-003 were accepted on 2026-10-24. The subsequently agreed shell removes application navigation and moves Connections into Settings; WI-004 now precedes this item and supplies that presentation surface. The accepted connection contract and implementation scope are otherwise unchanged.
