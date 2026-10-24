# OpenCode connection contract provenance

Retrieved and verified on 2026-10-24 for WI-001. Protocol data originates from the official MIT-licensed [anomalyco/opencode repository](https://github.com/anomalyco/opencode/blob/4617210822bdbb31e5749751afbfb4584ccedac8/LICENSE).

## Artifact identity

| Artifact | Exact source | SHA-256 |
| --- | --- | --- |
| Windows x64 CLI archive, 2.1.4 | `https://registry.npmjs.org/@opencode/cli-windows-x64/-/cli-windows-x64-2.1.4.tgz` | `CDD10A40552893D72E5A6920D63CEBFF1E9C056A2D28F77CF617267D453720C1` |
| Extracted `package/bin/opencode.exe` | Archive above | `B12B0613E9B4A491E5B12D90A5E9BBAE6D261FE9688C82BF73125A036963D399` |
| Corroborating TypeScript client, 2.1.4 | `https://registry.npmjs.org/@opencode/client/-/client-2.1.4.tgz` | `BF888E996A3CC8E2884CA1D5503B00DE4D8253AF012FACC62305C9156451E197` |
| Frozen source OpenAPI | `https://raw.githubusercontent.com/anomalyco/opencode/4617210822bdbb31e5749751afbfb4584ccedac8/packages/protocol/openapi.json` | `22B7B57412C1730D8C73CA452633AF46314DC3B28A312982BC858C431CC71B10` |

The CLI archive SHA-1 was also checked against npm metadata: `545a4b1d783e5330a1c2a424888dc7c812ad5c6e`. The package metadata has no `gitHead`; do not identify the source commit above as the proven build commit.

## Retained files and derivation

- `opencode-2.1.4-connection.openapi.json` is a subset of the JSON returned by the running pinned binary's authenticated `GET /openapi.json`. Retained root fields are `openapi` and `info`; the only path is `/api/info`; schemas are `ServerInfo`, `InvalidRequestErrorEncoded`, and `UnauthorizedErrorEncoded`. These schemas have no further references. Every retained protocol field and value is unchanged, including `security: []` and schema `additionalProperties` rules. Formatting alone was changed to two-space JSON with a final newline. SHA-256 of the committed CRLF file: `8AE561011017330B7E660FBDACB911751907D03164DA72EA8B6289C05FE7B600`.
- The same subset extracted from the frozen source OpenAPI compared equal, including ordering, to the runtime subset. The verifier independently parses and compares the running server's subset to the committed snapshot, ignoring JSON object property order. This is the direct connection between server artifact and retained contract; it does not rely on the website's latest document.
- `opencode-2.1.4-connection-evidence.json` records actual probe outcomes and the environment. Credentials are absent; PID, port, and temporary path are replaced. The actual runtime PID matched the owned process. The verifier's controlled timeout fixture is a TCP listener, not an OpenCode response.

The full OpenAPI document and downloaded binaries are temporary verification inputs, not repository deliverables. Use [the reproducible verifier](../verify-connection.ps1) and [connection contract instructions](../opencode-connection-contract.md#reproduction-and-cleanup) to obtain them again. See the contract document for authentication annotations missing from OpenAPI and lifecycle statuses implemented outside the declared operation.
