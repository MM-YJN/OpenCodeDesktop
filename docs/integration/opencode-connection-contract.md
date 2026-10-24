# Initial OpenCode v2 connection contract

Verified on 2026-10-24 for WI-001. This baseline covers connection readiness only; it does not validate sessions, prompts, events, or the other P0 requirements.

## Supported target and boundary

The initial target is the official npm artifact **`@opencode/cli-windows-x64@2.1.4`**. It is obtainable without a global installation, runs natively on this Windows x64 environment, and exposes its own OpenAPI document. The CLI reports `opencode v2.1.4`; its running server reports `version: "2.1.4"`. Use the artifact hashes in [the provenance record](contracts/README.md) to reproduce this exact target. The GitHub v1 release stream and `@opencode-ai/sdk/v2` are not this target.

Initial client support is deliberately limited to an existing server at a validated **`http://127.0.0.1:<port>`** origin and a server password supplied manually or retrieved through the explicit local environment workflow in WI-010. The username is `opencode`. Credentials are transient; persistence, pairing, process startup, remote hosts, TLS, reverse proxies, IPv6, and unauthenticated deployments are deferred. WI-010 adds bounded read-only installed-service discovery using `--version`, `service status` and `service get password`, followed by an authenticated identity probe; detection does not add profiles or switch servers, and the explicit local action retrieves/revalidates the credential again. This boundary requires no accounts or provider credentials. A `localhost` request timed out in this environment, while the explicit IPv4 endpoint worked; no universal claim about hostname support follows from that observation.

The client validates and trims the address before sending credentials. Accept an absolute HTTP URI with host exactly `127.0.0.1`, an explicit port from 1 through 65535, and an empty or `/` path. Normalize the optional trailing slash. Reject user information, query strings, fragments, other schemes/hosts, and non-root paths. Per WI-006 acceptance feedback, a missing or whitespace-only profile address uses the Windows v2 service default `http://127.0.0.1:28894`; new-profile editors prefill that value. Nonempty input must still contain an explicit valid port. Do not infer missing portions of a nonempty address or follow redirects. These are initial client design decisions, not server protocol restrictions.

## Request and readiness

```http
GET /api/info HTTP/1.1
Host: 127.0.0.1:<port>
Accept: application/json
Authorization: Basic <base64 of UTF-8 "opencode:<password>">
```

No request body, project directory, pairing call, or account configuration is needed. Encode credentials only in the header. Do not put them in URLs, diagnostics, or saved settings. Basic encoding does not encrypt credentials; the initial boundary uses local loopback HTTP.

The successful response observed was HTTP `200`, `Content-Type: application/json`, with this sanitized body:

```json
{
  "version": "2.1.4",
  "pid": 0,
  "urls": ["http://127.0.0.1:<port>"],
  "paths": { "tmp": "<isolated-root>\\tmp\\opencode" },
  "capabilities": { "persistentPty": true }
}
```

PID `0`, the port placeholder, and the temporary path replace observed machine-specific values. The verifier checked the actual PID against the process it launched before sanitizing it. The schema requires `version` (string), `pid` (nonnegative integer), `urls` (array of strings), and `paths.tmp` (string). `capabilities` is optional/nullable. Preserve the original schema in the snapshot; the client may ignore unknown additional properties for forward parsing, without expanding the version allowlist.

Readiness for this increment means **HTTP 200 + valid required response fields + exact supported version `2.1.4`** using the supplied credentials. It authorizes attempting subsequent calls under their separately verified contracts. It does not imply a selected project, configured model/provider, or successful future execution. Do not switch to a URL returned in `urls` or treat `paths.tmp` as a local client path.

The pinned source implements readiness through HTTP status, not a JSON `ready` field: `/api/info` returns `503` while starting/stopping and `500` after initialization failure, with the identity body; starting/stopping include `Retry-After: 1`. These branches are **source-supported, not runtime-observed** in this investigation: the verification run's first probe was already `200`. The static OpenAPI operation lists only `200`, `400`, and `401`, so transport handling must also cover these lifecycle statuses.

## Authentication

The CLI foreground server uses `OPENCODE_PASSWORD` (legacy alias `OPENCODE_SERVER_PASSWORD`); when no password is supplied it generates one. The CLI therefore does not provide an unauthenticated baseline. Its server username is fixed to `opencode`.

Runtime probes confirmed that missing credentials, a wrong password, a wrong username, and a Bearer header all produce:

```http
HTTP/1.1 401 Unauthorized
Content-Type: application/json
WWW-Authenticate: Basic realm="Secure Area"

{"_tag":"UnauthorizedError","message":"Authentication required"}
```

Valid Basic credentials produce the readiness response. The server source also accepts pairing-issued session tokens as Basic passwords, browser session cookies, and an `auth_token` query parameter; those flows are outside initial support and were not verified. The website's generic Bearer header example is not evidence that this CLI accepts Bearer authentication.

**Contract discrepancy:** the runtime OpenAPI operation contains `security: []` and no applicable security scheme, despite the observed Basic authentication requirement. Keep this field unchanged in the retained snapshot and document Basic authentication here instead of generating an anonymous client from that annotation.

## Compatibility and client outcomes

`/api/info.version` supplies a runtime version. The OpenAPI `info.version` is **`0.0.1`**, a contract metadata value, not the installed CLI version. There is no verified negotiated compatibility range or protocol revision. Initial policy is an exact `2.1.4` allowlist plus response-shape validation. Another version, including another v2 patch, requires an explicit baseline review; a response with version `unknown` is unsupported.

The runtime-served connection operation and all referenced schemas matched the frozen source contract at commit `4617210822bdbb31e5749751afbfb4584ccedac8`. The verifier also checks that slice against the committed snapshot. This links the retained contract directly to the running artifact without assuming that current website documentation matches it. Package metadata did not supply `gitHead`, so the source commit is corroboration, not a claim about the binary's exact build commit.

The application need not fetch OpenAPI on each connection. Version plus shape is a conservative support decision, not cryptographic identification or proof of every API contract. A modified server can report the same version; later features must validate their own responses. HTTP 200 by itself never establishes compatibility.

| Condition | Expected client outcome | Evidence basis |
| --- | --- | --- |
| Invalid or outside-scope address | Reject locally before any request; explain the accepted format | Client design; deterministic validation tests required in WI-002 |
| Socket connection refused | Show that no service is accepting connections at the address/port | Observed after terminating the owned server: `SocketError.ConnectionRefused` |
| Request deadline elapsed | Show timeout, keep address/input available for manual retry | Observed using a loopback TCP fixture that accepted a connection but sent no HTTP response |
| User cancellation | Stop the attempt without showing timeout; remain disconnected on an initial attempt, or retain the existing active backend when cancelling a switch | Client design; covered by WI-002 controlled tests |
| HTTP 401 | Authentication failed; allow password correction without logging it | Runtime-observed for four invalid/missing authentication cases |
| HTTP 200 with unsupported version | Unsupported server; display reported and supported versions | Version policy; WI-002 fixture test required |
| HTTP 200 with malformed/missing required fields, non-JSON, or wrong types | Invalid/incompatible response; never enter Ready | Client design informed by retained schema; WI-002 fixture tests required |
| HTTP 503 / 500 | Service unavailable / failed to initialize; never enter Ready | Pinned lifecycle source; these states were not induced in the runtime run |
| HTTP 400, 404, other errors, or redirect | Explain HTTP/protocol failure; never enter Ready or follow a redirect | 400 declared in contract; remaining handling is client design |

Use a five-second total deadline for a connection attempt, including response reading. This is a client design value; the controlled fixture used a shorter 300 ms deadline to verify cancellation of a stalled HTTP exchange. Do not retry automatically in this increment. The UI must remain responsive and allow another attempt after completion or cancellation.

## Reproduction and cleanup

Actual environment: Windows `10.0.26300`, x64; PowerShell `7.6.7`. No Bun installation, provider accounts, production service, or real project was used. Node `22.23.1` was present but is not required by the verifier. Windows `tar.exe` extracts the npm artifact. Downloading requires network access; local probes require loopback socket access. Network restrictions in the initial sandbox prevented registry access and local requests; these operations succeeded outside that restriction.

From the repository root in PowerShell 7:

```powershell
$probeRoot = Join-Path ([IO.Path]::GetTempPath()) ('ocd-wi001-download-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory $probeRoot | Out-Null
$archive = Join-Path $probeRoot 'cli.tgz'
Invoke-WebRequest 'https://registry.npmjs.org/@opencode/cli-windows-x64/-/cli-windows-x64-2.1.4.tgz' -OutFile $archive
if ((Get-FileHash $archive -Algorithm SHA256).Hash -ne 'CDD10A40552893D72E5A6920D63CEBFF1E9C056A2D28F77CF617267D453720C1') { throw 'Archive hash mismatch' }
tar -xzf $archive -C $probeRoot
if ($LASTEXITCODE -ne 0) { throw 'Archive extraction failed' }
./docs/integration/verify-connection.ps1 -ExecutablePath "$probeRoot/package/bin/opencode.exe" -OutputDirectory "$probeRoot/results"
Get-Content "$probeRoot/results/connection-evidence.json"
```

The verifier checks the executable SHA-256 before starting it. It launches `serve --hostname 127.0.0.1 --port 0` with a private workspace, generated disposable password, isolated `XDG_DATA_HOME`, `XDG_CONFIG_HOME`, `XDG_CACHE_HOME`, `XDG_STATE_HOME`, `TEMP`, `TMP`, `TMPDIR`, `OPENCODE_TEST_HOME`, and `OPENCODE_CONFIG_DIR`. It disables project configuration and models fetching. It does not use `--service`, register a background service, or alter global settings. It owns the process directly, checks PID and temporary path, captures sanitized evidence and the runtime OpenAPI document in the output directory, and kills the process tree and removes its separate scratch directory in `finally`. Output files remain available for review.

After saving any desired results, clean up only the download directory created above:

```powershell
$cleanupTarget = [IO.Path]::GetFullPath($probeRoot)
$temporaryParent = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
if (-not $cleanupTarget.StartsWith($temporaryParent, [StringComparison]::OrdinalIgnoreCase) -or
    [IO.Path]::GetFileName($cleanupTarget) -notmatch '^ocd-wi001-download-[0-9a-f]{32}$') { throw 'Unexpected cleanup path' }
Remove-Item -LiteralPath $cleanupTarget -Recurse -Force
```

## Results and remaining limits

See [sanitized runtime evidence](contracts/opencode-2.1.4-connection-evidence.json) and [snapshot provenance](contracts/README.md). The verifier passed authentication, version/PID identity, isolated temporary path, runtime contract equality, controlled timeout, and post-shutdown refusal checks. `localhost` timed out and is recorded separately from the supported numeric loopback origin. No root cause for that hostname timeout is claimed.

Unverified areas are explicitly excluded or assigned future checks: lifecycle `503`/`500` branches, remote/TLS deployments, pairing and session-token expiry, unauthenticated embedded configurations, other versions/platforms, and readiness of individual business operations. These do not block the narrowly defined initial connection increment. The target and support boundary were accepted through WI-001 on 2026-10-24; [WI-002](../work-items/WI-002-implement-server-connection.md) is ready to start.

## Official sources

- [v2 installation documentation](https://opencode.ai/v2/docs), naming `@opencode/cli`; [exact Windows package metadata](https://registry.npmjs.org/@opencode%2fcli-windows-x64/2.1.4).
- [Frozen OpenAPI source](https://github.com/anomalyco/opencode/blob/4617210822bdbb31e5749751afbfb4584ccedac8/packages/protocol/openapi.json).
- [CLI startup and environment isolation](https://github.com/anomalyco/opencode/blob/4617210822bdbb31e5749751afbfb4584ccedac8/packages/cli/src/server-process.ts), [password environment variables](https://github.com/anomalyco/opencode/blob/4617210822bdbb31e5749751afbfb4584ccedac8/packages/cli/src/env.ts).
- [Lifecycle dispatch and readiness status](https://github.com/anomalyco/opencode/blob/4617210822bdbb31e5749751afbfb4584ccedac8/packages/server/src/process.ts), [ready response handler](https://github.com/anomalyco/opencode/blob/4617210822bdbb31e5749751afbfb4584ccedac8/packages/server/src/handlers/server.ts).
- [Authentication parsing and rejection](https://github.com/anomalyco/opencode/blob/4617210822bdbb31e5749751afbfb4584ccedac8/packages/server/src/middleware/authorization.ts), [password and session verification](https://github.com/anomalyco/opencode/blob/4617210822bdbb31e5749751afbfb4584ccedac8/packages/server/src/auth.ts).
