# OpenCode 2.1.4 project and session workflow

Verified on 2026-10-24 for [WI-005](../work-items/WI-005-verify-project-session-workflow.md). This contract separates observed server behavior from intended client behavior. Production implementation belongs to [WI-006](../work-items/WI-006-implement-live-projects-sessions.md).

## Baseline and retained evidence

Use the executable and local loopback/Basic-authentication boundary established by the [connection contract](opencode-connection-contract.md). The executable SHA-256 is checked before launch. The [runtime OpenAPI slice](contracts/opencode-2.1.4-project-session.openapi.json) retains ten paths and their transitive closure of 97 schemas. The [sanitized observations](contracts/opencode-2.1.4-project-session-evidence.json) contain 31 cases from an isolated owned server. No provider was invoked, no production directory was opened, and no installed service was reused.

The runtime contract is authoritative for this baseline. Unlike the connection slice, this wider slice **does not equal** the frozen source at `4617210822bdbb31e5749751afbfb4584ccedac8`. Differences include session/project archive parameters, the `idle` message filter, import accepting older transcript arrays, and model/message schemas. Do not derive this implementation from that source revision or a legacy SDK. OpenAPI still declares `security: []` despite runtime Basic authentication. The verifier compares future runtime slices with the retained runtime snapshot.

## Observed requests and responses

All requests use `Accept: application/json` and per-request `Authorization: Basic <UTF-8 opencode:password>`. POST requests use JSON. Encode each query value and path segment; never concatenate an unescaped directory or cursor. `location[directory]` is a deep-object query key, encoded as `location%5Bdirectory%5D`. A path containing spaces and Chinese characters round-tripped successfully.

| Operation | Request | Observed result |
| --- | --- | --- |
| Resolve server directory | `GET /api/location?location[directory]=<path>` | 200 with `{directory, project:{id,directory,canonical}}`; missing path returns 404 `LocationNotFoundError` |
| Validate resolved directory | `GET /api/fs/list?location[directory]=<resolved-directory>` | 200 with `{location,data:[entries]}` for a directory; an ordinary file returns 404 `FileNotFoundError` |
| List that directory's sessions | `GET /api/session?directory=<resolved-directory>&limit=50&order=desc` | 200 `{data:[Session.Info],cursor:{previous,next}}`; empty directory has `data:[]`, both cursors null |
| Create root session | `POST /api/session` with `{"location":{"directory":"<resolved-directory>"},"title":"<title>"}` | 200 `{data:Session.Info}`; no agent or model required |
| Reopen session metadata | `GET /api/session/<id>` | 200 `{data:Session.Info}` including `location.directory` and `projectID`; missing ID returns 404 `SessionNotFoundError` |
| Read messages | `GET /api/session/<id>/message?limit=50&order=desc` | 200 `{data:[Session.Message.Info],cursor:{previous,next}}`; new session has empty history; missing ID returns 404 |
| Discover models | `GET /api/model?location[directory]=<resolved-directory>` | 200 `{location,data:[Model.Info]}`; isolated run returned ten models |
| Discover default model | `GET /api/model/default?location[directory]=<resolved-directory>` | 200 `{location,data:Model.Info}` in the isolated run |
| Discover agents | `GET /api/agent?location[directory]=<resolved-directory>` | 200 `{location,data:[Agent.Info]}`; seven agents including visible primary agents `build` and `plan` |

Location resolution alone does not validate directory type: it returned 200 for an ordinary file. Perform resolution followed by filesystem listing before promoting a project. Listing is a validation call, not a file-browser feature. Do not open server paths using client filesystem APIs or a Windows folder picker. The server owns path normalization; use its returned directory in subsequent requests and ownership keys. An unavailable/inaccessible listing must not be represented as an empty project.

Two different empty Git roots both resolved to project ID `global`. Directory-filtered sessions remained separate. Therefore project ID is metadata, not a unique workspace ownership key. Use the active backend identity and resolved directory. Session GET and message GET are keyed by session ID rather than a location query: verify session metadata belongs to the selected directory before displaying its history. The API can return a session from another directory when requested by its ID; the client must enforce its own navigation boundary.

Session creation and reopening required no model/provider credentials. Session IDs, titles, timestamps, cost/tokens and location come from the server; do not manufacture a session from a successful empty HTTP response. A new session may have no title, agent, model, outcome, or messages. Render an appropriate fallback title and empty conversation.

## Pagination and historical content

Both list APIs return cursors in the body, not HTTP headers. First request uses an explicit size and order. To load older content after `order=desc`, follow `cursor.next` with the same size and directory filter (for sessions), omitting `order`. Cursors are opaque: URL-encode and pass them unchanged. Do not decode them for application logic, log them, or carry them to another backend/directory/session. Invalid cursors produced 400 `InvalidCursorError` in both APIs.

The runtime message sequence with page size two was `[5,4]`, `[3,2]`, `[1]`, then `[]`. All five fixture message IDs were distinct and complete. Every nonempty page, including `[1]`, supplied a next cursor. **A non-null cursor does not prove another item exists.** Stop on an empty page or missing next cursor. Detect repeated cursors/no progress defensively and report incompatible pagination rather than loop. Preserve already loaded content and draft on older-page failure; retry the same cursor. Deduplicate by message ID and display chronological order by reversing each descending page and prepending older pages. Avoid reordering by timestamp alone.

For reproduction, history is seeded through `POST /api/experimental/session/import` with five projected synthetic messages and copied session metadata under a new ID. This is a verifier fixture only; import is excluded from the application increment. An exploratory synthetic submission with `resume:false` returned inbox items but no projected history, so it is not used in the retained checks. No prompt/execution semantics are established here. The fixture verifies retrieval and paging, not assistant/tool execution. The retained union includes user, synthetic, system, assistant, switch, compaction, skill, shell and idle variants; unknown variants need a readable fallback rather than failing the whole history. Recorded history must not imply currently running work.

## Model and agent discovery limits

Model discovery is possible without a provider account; the isolated server supplies bundled models, including a default. An entry or `enabled:true` is not evidence of successful inference or account authorization. Discovery can precede plugin settlement, as the runtime operation describes. In WI-008, offer non-hidden agents whose mode is `primary` or `all`; exclude subagent-only entries from direct task selection. Consider model `enabled`, status and capabilities, and validate actual submission separately in WI-007.

Empty model/agent lists, no usable primary agent, default-model failure, provider authorization, plugin settlement, and inference were **not induced**. Client behavior for no usable selection is to explain that task submission is unavailable and allow explicit discovery retry while preserving project navigation, history and drafts. Do not invent a fallback model or agent. WI-006 does not implement these selectors.

## Intended client state and authorization lifetime

The following rules are design decisions for WI-006, not observations of an implemented UI.

Credentials stay in memory only. The current `ServerConnections.ConnectAsync` probes then discards the password; WI-006 must add an authenticated connection context. Infrastructure owns the HTTP/authentication implementation behind Core abstractions; passwords must not become fields on public profile, active-server, view-model or diagnostic records. Each request is bound to an immutable validated origin and the credential for that connection generation. Use per-request headers, no redirects/cookies/proxy, and never follow advertised origins. An active context retains the credential until successful switching, disconnect, authorization rejection, or disposal. Dropping references is required; guaranteed zeroing of managed strings is not claimed.

Probe a switch candidate separately. Promote its connection context only after readiness succeeds, then cancel/dispose old requests and release the old credential. Failed/cancelled switching disposes the candidate and retains the previous active credential, workspace and draft. Returning to a previously used server requires explicit authentication again (manual password entry, or the revalidated WI-010 local environment action); workspace context and drafts can survive in memory without retaining inactive credentials. Editing/removing a profile discards its old workspace context; a revised origin must never inherit old ownership. No automatic reconnection, saved credentials, token exchange, server profile persistence or expiry negotiation is introduced.

Missing credentials on session listing and wrong credentials on history produced 401 `UnauthorizedError`. On a later 401, invalidate and dispose the active authenticated context, stop live actions and require explicit reconnect through Settings. Preserve drafts and separately owned in-memory navigation state. Do not repeatedly retry with a rejected password. Basic authentication is supplied for every call; there is no verified server-side login session or expiration guarantee for these credentials. Rotation/expiry while connected was not induced.

| Trigger/state | Visible behavior and retained state |
| --- | --- |
| Connected, no project selected | Show backend name/origin and an editable server-directory text field with Open action; explain paths belong to that server |
| Opening project | Keep current project visible with its own label; show loading for the candidate; promote only after resolve, directory validation and initial session list succeed |
| Project open succeeds | Show server and resolved directory; an empty list offers Create session; remember this project in the backend's in-memory opened-project list |
| Project open fails | Show safe actionable error and retry/edit path; preserve prior selection, history and all drafts; never convert an error into empty success |
| Session selected/loading | Save outgoing draft; show selected session header and loading placeholder, with no previous session's messages under it; promote history only after metadata ownership validation |
| Empty history | Show empty conversation; retain editable draft; sending remains disabled until the later task increment |
| History load fails / session missing | Show retry or return-to-list; preserve that session's draft; never substitute another conversation |
| Create session pending | Gate duplicate create actions; select the confirmed response and refresh listing. On uncertain delivery, offer list refresh before another explicit create; never automatically repeat POST |
| Switching session/project | Store drafts by backend identity, resolved directory, and session ID; use a separate per-directory new-session draft and transfer it only after confirmed creation |
| Successful backend switch | Immediately hide old backend content, cancel/invalidate its loads, show target ownership and revalidate remembered target project/session before showing history; no target project means directory entry state |
| Failed/cancelled backend switch | Keep old active workspace and draft; display target connection failure without relabelling old content |
| Disconnect / later 401 | Save draft, cancel loads, invalidate request generation, release auth context, hide live content and show disconnected/reconnect state; retain owned navigation/drafts for explicit reconnection |
| Navigation away/shutdown/late result | Cancel where possible; apply results only if connection generation, project, session and load generation still match; shutdown disposes auth and clears in-memory state |

Retry GETs explicitly. For workspace reads use a bounded total deadline (initial client value: 15 seconds including body), cancellation and response size limits, with safe client-authored errors for timeout, transport, 400/401/404/5xx, invalid JSON and incompatible shapes. Initial limits: 1 MiB for directory validation and session lists, 8 MiB for a history page; an oversized page is a retryable size error, not silent truncation. These are client choices to verify with controlled tests, not server limits. Retain existing connection-probe policy separately.

Per WI-006 acceptance feedback, the workspace starts empty and disconnected, with guidance to Settings > Connections. Sample workspace content and actions have been removed. Connected surfaces show real content or honest unavailable placeholders, including Details, attention and terminal surfaces that have no integration yet. No live task/permission/terminal action is enabled in this increment.

## Reproduction, verification and remaining limits

From the repository root in PowerShell 7, using the separately downloaded artifact from the [connection setup](opencode-connection-contract.md#reproduction-and-cleanup):

```powershell
./docs/integration/verify-project-session.ps1 -ExecutablePath '<download-root>/package/bin/opencode.exe' -OutputDirectory '<results-directory>'
```

Requires Windows x64, PowerShell 7 and Git on PATH. The verifier starts one hidden foreground process with isolated home/config/data/cache/state/temp directories, disables project config/model fetching, and removes inherited credential environment variables. It creates two empty Git roots, checks runtime version/PID/temp ownership, and uses a generated disposable Basic password. It kills its process tree and validates its owned temporary path before recursive cleanup in `finally`, with bounded deletion retries. Evidence output remains for review. It neither mutates global settings nor deletes the downloaded executable.

The final run passed all 31 cases; a repeated run also passed runtime-to-snapshot equality and cleanup. Sanitization replaces the owned root, replaces nonempty cursors (which can embed paths), and retains only availability fields for model/agent bodies, excluding settings, headers and provider credentials. Dynamically generated session IDs and timestamps remain as disposable fixture evidence. The snapshot preserves protocol fields without sanitization or modification, including authentication annotation discrepancies.

Unobserved cases: access-denied directories, junction/symlink aliases, relative path behavior, project/session mutation by another client during paging, service restart, credential rotation, empty discovery, and real assistant/tool histories. Controlled WI-006 tests must cover ownership, missing/unknown fields, error/cancellation/size handling and races; they cannot be described as runtime evidence. No essential workflow blocker remains: missing/file paths have verified failure handling, and uncertain model usability affects later submission, not reading sessions. No App build or application tests are needed for this investigation's documentation/verifier changes.
