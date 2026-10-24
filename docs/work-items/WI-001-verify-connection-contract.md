# WI-001 Establish and verify the initial OpenCode v2 connection contract

## Details

- Type: Investigation
- Related requirements: [CON-01, CON-02](../requirements/overview.md#functional-requirements)
- Priority: Prerequisite for P0
- Dependencies: None
- Status: See the [completed work item index](completed.md#2026)

## Background and Goal

As of 2026-10-24, the App contains only an empty main window, Core and Infrastructure have no business implementations, and neither test project contains test code. The requirements overview calls for connecting to an existing OpenCode v2 service, but the supported server version, authentication mechanism, and compatibility checks remain undecided.

Establish a verified protocol baseline for the first connection feature so that subsequent implementation does not depend on guessed endpoints or v1 examples. This work item does not itself complete CON-01 or CON-02.

## Scope

- Select an obtainable, pinnable OpenCode v2 version as the initial integration target. Record the rationale, official source, and exact version or commit identifier.
- Retain the corresponding API contract and establish that it matches the running server version. Record any discrepancy or inability to establish that relationship as a blocker.
- Verify the minimum connection readiness request: method, path, address rules, authentication requirements, response structure, and failure semantics.
- Determine whether a reliable version or compatibility detection mechanism exists. Distinguish server reachability, successful authentication, and contract compatibility.
- Verify the minimum connection flow against an isolated local test service. Record reproducible commands and sanitized request and response evidence.
- Define the next connection implementation work item based on the findings, including the initial address and authentication support boundaries.

## Out of Scope

- Connection UI, a production HTTP client, or settings persistence.
- Sessions, prompts, event streams, permissions, forms, or reconnection.
- Automatic installation, discovery, or startup of OpenCode within the desktop application.
- Support commitments covering all v2 versions or remote deployment configurations.

## Deliverables

- `docs/integration/opencode-connection-contract.md`: supported target, official references, connection and authentication contract, compatibility checks, verification steps and results, and known limitations.
- `docs/integration/contracts/`: the minimum necessary contract snapshots for the target version, with provenance notes. Preserve original protocol fields; exclude credentials and machine-specific private paths.
- Completed acceptance and verification records in this work item, plus a subsequent connection implementation work item registered in the index with a dependency on this item.
- Updates to the requirements overview wherever findings change an existing assumption.

Create these directories and files when producing the actual deliverables. Empty placeholders do not constitute completion.

## Acceptance Criteria

- [x] The target version or commit is precisely identifiable. Contract records include the source, retrieval date, and evidence linking the contract to the server version.
- [x] Documentation includes reproducible steps to start and clean up an isolated test service, and records the environment actually used.
- [x] Sanitized evidence shows the actual connection probe request and successful response, with an explicit definition of readiness for subsequent API calls.
- [x] Expected client behavior is defined separately for invalid addresses, connection refusal, and request timeout. Observed results are distinguished from design decisions.
- [x] Authentication requirements and authentication failure behavior are supported by evidence. If authenticated deployments are excluded initially, the support boundary and rationale are explicit.
- [x] The compatibility decision has a documented basis. If the protocol cannot reliably report a version, HTTP success alone is not treated as proof of compatibility; an alternative strategy and remaining limitations are documented.
- [x] Unverified key assumptions are explicit. Questions essential to implementing the connection feature are resolved; otherwise, the item remains blocked.
- [x] A subsequent connection implementation work item exists with scope, observable acceptance criteria, module responsibilities, and verification requirements.

## Execution Constraints

- Use official documentation, source code, and contracts for the selected version, checked against runtime results. Links in the requirements overview are investigation starting points.
- Do not assume that legacy SDK examples or the latest website contract apply to the selected version.
- Do not require production services, real projects, or real accounts for verification.
- Document how to run temporary scripts or probes. Avoid unnecessary runtime dependencies if they are committed to the repository.
- Track and clean up service processes and temporary files created by this work.
- If a usable v2 version or verification environment cannot be obtained, record the attempts, blocker, and unblock condition. Do not fabricate responses or silently switch to v1.

## Verification Approach

Protocol evidence and reproducible requests against a running service are the primary verification method. Documentation-only changes do not require an App build or runs of empty test projects. If execution introduces code, perform and record the relevant checks required by AGENTS.md.

## Implementation and Verification Record

### Deliverables and decisions

The accepted initial [verified connection baseline](../integration/opencode-connection-contract.md) is official Windows x64 CLI `2.1.4`, an existing `http://127.0.0.1:<explicit-port>` server, and Basic authentication with username `opencode` and a transient server password. Readiness requires the expected response shape and exact version, not HTTP success alone. Remote/TLS/hostname support, pairing, discovery, and credential persistence remain deferred. Business-operation contracts still require verification as their features are introduced.

- [Connection contract](../integration/opencode-connection-contract.md), [provenance and hashes](../integration/contracts/README.md), [minimum runtime OpenAPI snapshot](../integration/contracts/opencode-2.1.4-connection.openapi.json), [sanitized evidence](../integration/contracts/opencode-2.1.4-connection-evidence.json), and [reproducible verifier](../integration/verify-connection.ps1).
- Target: official npm `@opencode/cli-windows-x64@2.1.4`, pinned by archive and executable hashes. The runtime reports `2.1.4` and its actual PID matched the launched process. Its connection contract matches the frozen official source at `4617210822bdbb31e5749751afbfb4584ccedac8`. No exact binary build commit is claimed because package metadata lacks `gitHead`.
- Initial support: explicit numeric IPv4 loopback HTTP origin, fixed username `opencode`, transient password, exact server version and required response-shape checks. Remote/TLS/hostname support and other authentication flows are excluded from this increment.
- Runtime Basic authentication contradicts the OpenAPI operation's `security: []` annotation. Preserve the original annotation and document actual behavior. OpenAPI `info.version: 0.0.1` is not the server release version. Readiness uses HTTP status; there is no `ready` body field.
- [WI-002](WI-002-implement-server-connection.md) defines the connection implementation, module ownership, acceptance criteria, and testing. It has not started. Requirements link this accepted baseline and identify remaining remote/registration questions.

### Verification performed on 2026-10-24

Environment: Windows `10.0.26300`, x64, PowerShell `7.6.7`; no provider credentials, production projects, global CLI installation, or shared service changes. Downloads and loopback probes required execution outside the initial network-restricted sandbox. Initial sandbox registry/DNS/socket failures were environment failures, not server behavior.

1. Retrieved exact npm metadata with `Invoke-RestMethod https://registry.npmjs.org/@opencode%2fcli-windows-x64/2.1.4`; downloaded its tarball using `Invoke-WebRequest` and extracted with `tar -xzf`. `Get-FileHash` confirmed SHA-256 identities in the provenance record and the archive SHA-1 matched npm metadata.
2. Ran the extracted executable with isolated XDG paths: `opencode.exe --version` returned `opencode v2.1.4`; `serve --help` confirmed foreground hostname/port flags; `debug paths` confirmed isolated storage locations.
3. Ran `./docs/integration/verify-connection.ps1 -ExecutablePath '<temporary-download>/package/bin/opencode.exe' -OutputDirectory '<temporary-download>/verification'` against the pinned artifact. Final run exited 0 and reported `Verified OpenCode 2.1.4`. The script verified runtime PID/path identity, HTTP 200 for correct Basic credentials, HTTP 401 for missing/wrong-password/wrong-username/Bearer credentials, exact runtime connection-contract equality, a controlled 300 ms timeout, and connection refusal after killing the owned process. The committed JSON contains the final run's actual results.
4. Retrieved the frozen source OpenAPI and compared its `/api/info` operation and three referenced schemas with the runtime-served document: equal, including ordering. Retained only this closed subset without changing protocol fields. The verifier repeats structural equality against the committed snapshot.
5. `localhost` timed out in this environment. Startup polling first observed HTTP 200; lifecycle 503/500 branches are documented from pinned source and explicitly not claimed as runtime-verified. Invalid-address validation, cancellation UX, unsupported-version handling, and malformed-response handling are client design requirements for WI-002, not implemented behavior.
6. Parsed `verify-connection.ps1` with `System.Management.Automation.Language.Parser.ParseFile`: no syntax errors. Parsed both retained JSON files with `ConvertFrom-Json`, checked repository-local Markdown links, and ran `git diff --check`: all passed. The snapshot's recorded SHA-256 remained unchanged after line-ending normalization.
7. Checked `Get-Process -Name opencode` for executable paths inside the owned download directory: none remained. Validated the exact absolute cleanup target and removed the dedicated temporary download directory, including binaries, logs, full schemas, and intermediate probe output. Only sanitized deliverables remain in the repository.

The verifier terminates its owned server and TCP fixture and deletes its temporary runtime directory in `finally`; download/output cleanup is documented. No App/Core/Infrastructure production code was changed. App build and empty xUnit project runs were not applicable; the introduced verification script was exercised against the real pinned service instead.

### Handoff

Investigation evidence satisfies the technical checklist above. The user accepted completion on 2026-10-24, including the exact target and initial address/authentication boundary, and requested that WI-001 be marked Done in the index. WI-002's dependency is satisfied; its execution has not started. This item does not complete CON-01 or CON-02.

## Blockers

No implementation blockers for the defined initial connection increment. Other versions, remote/TLS/IPv6/hostname connections, pairing/session-token expiry, and lifecycle error injection remain outside the verified runtime boundary.
