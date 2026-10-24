# OpenCode Desktop Requirements

Status: Initial draft for discussion

Last updated: 2026-10-24

## Purpose

OpenCode Desktop is a native Windows task workspace for OpenCode v2, built with WinUI 3 and C#/.NET. It should let developers start coding tasks, follow agent execution, respond to requests, and review results through a coherent desktop experience.

This document records the initial product direction derived from the official v2 API documentation. Priorities are proposed scope, not a claim that features have been implemented or validated against a running server. Detailed contracts and acceptance tests must be confirmed against a selected OpenCode release before implementation.

## Product Goals

- Provide a complete coding-task workflow, including permissions and agent questions.
- Make execution state, tool activity, and failures understandable.
- Preserve continuity when users switch sessions, restart the app, or reconnect.
- Use OpenCode for agent execution, model access, and tools while focusing this client on Windows interaction and task presentation.
- Support server-owned projects and paths without assuming that every file exists on the client machine.
- Allow users to connect to different OpenCode server backends and switch the active backend. Make server identity visible and keep project lists and sessions scoped to their owning connection. Persist connections and their project lists, protect saved credentials, and automatically restore the last active workspace on launch. Remote-host support remains outside the established connection boundary.

## Workspace Layout

The [desktop concept](desktop-concept.png) establishes the intended workspace layout: a collapsible project/session panel, a central task header, conversation and composer, and a collapsible, resizable Details pane. There is no separate application navigation rail. Settings is accessible from the projects-panel footer; when Projects is hidden, reopen it to reach Settings. The workspace header contains Projects, Details, and Terminal without a duplicate Settings entry. Connections and Extensions are sections inside Settings, alongside Models & Providers, Permissions, Appearance, and Notifications. Returning from Settings restores the workspace context. Opening or closing Details animates only that pane, without a fly-in transition on the main conversation or composer.

Details has Info, Files, and Changes tabs. Info contains session metadata, context, instructions, usage, and activity. Files combines navigation/search with a selected-file preview. Changes combines a changed-file list with an expanded diff and an explicit comparison selector. Conversation file references and change summaries open the corresponding Details tab. Each tab retains its selection and scroll position; the pane supports expand/restore for larger previews. File previews and diffs live in Details rather than below the conversation. A collapsible terminal surface can occupy the bottom of the central workspace. Connection status and pending-attention access remain visible outside Settings.

On first launch, or after an explicit disconnect, the app opens with an empty, disconnected workspace and guidance to Settings > Connections. With saved active connection information, launch automatically reconnects and restores the last selected project/session after validation and authoritative server refresh. Saved project lists may be shown while disconnected, with connection state clear; live sessions and conversation history require a real connection. Do not populate the workspace, Details, terminal, composer or attention surfaces with illustrative data. Features without a live integration remain explicitly unavailable.

## Integration Direction

Use the OpenCode v2 HTTP API and event stream through Infrastructure implementations. The official v2 network client is `@opencode/client`; legacy examples using `@opencode-ai/sdk/v2` must not be treated as the OpenCode 2 contract.

The preferred C# integration is direct HTTP communication with an independent OpenCode service. Embedding the JavaScript/Bun host is outside the initial scope. Connect to existing services directly. Connections also detects a supported installed Windows service, verifies its local credential, and offers explicit add/connect without password copy/paste. Automatic service startup remains later work.

The published HTTP surface is marked experimental. Select a supported server version and retain the corresponding API contract before building the integration. Exact authentication, event payloads, error formats, and operation semantics require runtime verification.

## API Capability Map

The following resource groups describe the documented capabilities relevant to planning. They are not a promise to expose every operation in the UI.

| Area | Relevant API resources or capabilities | Potential client features |
| --- | --- | --- |
| Connectivity | `server`, `location`, pairing, configuration reload | Server connection and project directory selection |
| Projects | `project`, `worktree` | Project navigation and isolated task workspaces |
| Sessions | List, create, update, archive, delete, fork, move | Task history and session organization |
| Execution | Prompt, commands, skills, input inbox, interrupt, wait | Task submission and execution control |
| Context and history | Messages, logs, instructions, compaction, turn diffs, staged revert | Execution inspection and history review |
| Live updates | `event` | Streaming messages and activity |
| User interaction | `permission`, `form` | Permission decisions and answers to agent questions |
| Models and accounts | `model`, `provider`, `integration`, `credential` | Model selection and provider authentication |
| Files and version control | `filesystem`, `vcs` | File navigation and change review |
| Terminals | `pty`, `persistentPty`, `shell` | Interactive terminals and command output |
| Extensions | `mcp`, `plugin`, `rpc`, commands, skills, references | Extension discovery and management |
| Additional services | Configuration, voice, web search, statistics, import/export | Later enhancements |

## Functional Requirements

### P0: Complete Task Workflow

| ID | Requirement |
| --- | --- |
| CON-01 | Manage multiple named existing OpenCode v2 server backends, connect using the supported authentication mechanism, and explicitly switch the active backend. Display active-server identity, readiness, and actionable errors; a failed or cancelled switch retains the previous active backend. Preserve the verified local-address boundary. New profiles default to the Windows v2 service address `http://127.0.0.1:28894`; an omitted or blank address uses that default, while an explicit address overrides it. |
| CON-02 | Detect and communicate an unsupported server version or contract instead of silently continuing with incompatible behavior. |
| CON-04 | Persist connection profiles, protected credentials and each connection's project list/navigation selections. On launch automatically reconnect to the last active connection and restore its validated project/session using authoritative server state. Respect explicit disconnect and provide actionable recovery for unavailable servers, invalid credentials or missing selections. |
| PRJ-01 | Maintain a project list per connection, add validated server-side directories, switch between listed projects, and remove navigation entries without deleting server work. Show the active server and directory clearly and retain each project's session selection and in-memory drafts during navigation. |
| SES-01 | List sessions, create a session, switch between sessions, and load message history with pagination where required. |
| SES-02 | Select an available model and agent for a task and show the current selection. |
| MSG-01 | Submit a text prompt and render user messages, assistant content, code blocks, tool activity, and execution errors. |
| EXE-01 | Distinguish submission from completion and represent queued, running, waiting for user input, completed, interrupted, and failed states where supported by the contract. |
| EXE-02 | Interrupt active execution and reconcile the resulting state with the server. |
| INT-01 | Display pending permission requests and submit supported decisions with their scope visible. |
| INT-02 | Render agent forms and submit answers, including validation and cancellation or expiry states. |
| SYN-01 | Consume live events without blocking UI interaction and apply updates to the correct session. |
| SYN-02 | Reconnect after transport failure and re-query authoritative session, message, execution, and pending-interaction state. |
| SYN-03 | Restore access to existing server-side sessions after application restart. |

### P1: Daily Use

| ID | Requirement |
| --- | --- |
| CON-03 | Detect a running installed compatible local service and retrieve its password transiently for an explicit Add local environment action. Preserve manual connection entry. Starting a local service when needed remains later scope, with clear ownership of service lifecycle. |
| SES-03 | Search, rename, archive, and restore sessions. |
| SES-04 | Fork a session from supported history boundaries and expose supported revert operations with clear effects. |
| MSG-02 | Attach or reference files using the supported v2 input contract. |
| EXE-03 | Display queued inputs and allow supported queue updates or cancellation. |
| REV-01 | Browse project files and preview relevant content. |
| REV-02 | Review repository changes and session-turn diffs with their comparison basis clearly identified. |
| WIN-01 | Notify users when a task completes or requires attention, subject to notification preferences. |

### P2: Advanced Capabilities

- Create, list, refresh, and remove worktrees for isolated tasks.
- Provide interactive and persistent terminal experiences.
- Manage provider connections and supported credential flows.
- Inspect and manage MCP servers and plugins.
- Expose commands, skills, and references through suitable discovery UI.
- Add usage statistics and session import/export.
- Evaluate voice interaction and additional configuration UI when core workflows are established.

## Interaction and Reliability Requirements

- Keep connection status separate from session execution status.
- Keep pending permissions and forms discoverable after session navigation and reconnection.
- Treat the server as the authority for conversation history and execution state; local preferences must not become a conflicting session database.
- Preserve unsent input during recoverable failures and session navigation.
- Avoid blind retries of prompts or other mutations when delivery is uncertain; reconcile with server state before retrying.
- Handle unknown event or message variants gracefully and retain enough diagnostics to investigate compatibility issues.
- Clearly distinguish local client paths from remote server paths. Local file-opening actions must account for path accessibility.
- Protect stored authentication material using an appropriate Windows mechanism and exclude secrets from diagnostic logs.
- Support keyboard navigation, accessible names, readable focus states, and long or streaming conversations without freezing the UI.

Official client documentation describes events as live-only, without replay or automatic reconnection. Recovery therefore requires explicit resubscription and state reconciliation; reopening the stream alone is insufficient.

## Initial End-to-End Acceptance Scenario

1. Connect to a compatible OpenCode server and select a project directory.
2. Create a session and select an available model and agent.
3. Submit a coding request and observe message and tool updates.
4. Respond to a permission request or agent form when one occurs.
5. Observe task completion, interruption, or a meaningful error.
6. Restart the client and reopen the session with current history and pending interactions restored.
7. Simulate a connection failure during execution and verify recovery without duplicate prompt submission.

P1 extends this scenario with file references and a review of the resulting changes.

## Deferred Scope

- A full IDE with advanced code editing and language-service integration.
- Reimplementing the OpenCode agent runtime or provider execution pipeline.
- Complete UI coverage of every API operation in the first release.
- OpenCode v1 compatibility unless a separate requirement is established.

The v2 migration documentation states that v2 does not currently run language servers or provide LSP tools or diagnostics. Initial diagnostic workflows should rely on project build, lint, typecheck, and test commands.

## Open Questions and Validation Work

- Which additional releases should be validated beyond the accepted [WI-001 connection baseline](../work-items/WI-001-verify-connection-contract.md)?
- Basic server-password authentication on explicit Windows IPv4 loopback is verified. What are the supported pairing, service-registration, and remote/TLS connection contracts for later increments?
- Which event types and identifiers are needed to reconcile execution, tool activity, and messages reliably?
- What are the supported form types, permission decision scopes, and expiry semantics?
- How are input inbox entries correlated with resulting messages after uncertain delivery?
- What are the attachment limits, supported media types, and remote-file handling rules?
- What are the precise semantics of staged revert, session movement, and worktree removal?
- Should local service startup be included in the first public release, and should remote connections be a primary scenario?

The next integration milestone is a small verified P0 workflow covering connection, session creation, prompt submission, events, permissions or forms, and recovery against a pinned server version.

## Official References

These references informed the initial investigation on 2026-10-24 and may evolve independently of this document.

- [OpenCode v2 API reference](https://opencode.ai/v2/docs/api)
- [OpenCode v2 OpenAPI definition](https://opencode.ai/v2/openapi.json)
- [OpenCode v2 network client, events, and local service integration](https://opencode.ai/v2/docs/build/client/)
- [Migration from v1, including API changes and LSP limitations](https://opencode.ai/v2/docs/migrate-v1)
- [Embedded SDK overview](https://dev.opencode.ai/v2/docs/build/sdk/)
