# WI-001 Establish and verify the initial OpenCode v2 connection contract

## Details

- Type: Investigation
- Related requirements: [CON-01, CON-02](../requirements/overview.md#functional-requirements)
- Priority: Prerequisite for P0
- Dependencies: None
- Status: See the [work item index](README.md)

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

- [ ] The target version or commit is precisely identifiable. Contract records include the source, retrieval date, and evidence linking the contract to the server version.
- [ ] Documentation includes reproducible steps to start and clean up an isolated test service, and records the environment actually used.
- [ ] Sanitized evidence shows the actual connection probe request and successful response, with an explicit definition of readiness for subsequent API calls.
- [ ] Expected client behavior is defined separately for invalid addresses, connection refusal, and request timeout. Observed results are distinguished from design decisions.
- [ ] Authentication requirements and authentication failure behavior are supported by evidence. If authenticated deployments are excluded initially, the support boundary and rationale are explicit.
- [ ] The compatibility decision has a documented basis. If the protocol cannot reliably report a version, HTTP success alone is not treated as proof of compatibility; an alternative strategy and remaining limitations are documented.
- [ ] Unverified key assumptions are explicit. Questions essential to implementing the connection feature are resolved; otherwise, the item remains blocked.
- [ ] A subsequent connection implementation work item exists with scope, observable acceptance criteria, module responsibilities, and verification requirements.

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

Not started. On completion, record the target version, contract paths, verification commands and results, key decisions, remaining limitations, and follow-up work item ID.

## Blockers

No confirmed blockers. Availability of the target version and runtime environment will be established during execution.
