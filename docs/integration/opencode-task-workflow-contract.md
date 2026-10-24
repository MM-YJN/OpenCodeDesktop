# OpenCode 2.1.4 task workflow contract

Verified on 2026-10-24 for [WI-007](../work-items/WI-007-verify-task-execution-workflow.md). Authority is the running hash-pinned Windows executable, not the latest website or legacy v1 SDK. See [artifact provenance](contracts/README.md), the [runtime OpenAPI slice](contracts/opencode-2.1.4-task-workflow.openapi.json), [sanitized observations](contracts/opencode-2.1.4-task-workflow-evidence.json), and [verifier](verify-task-workflow.ps1).

## Evidence boundary and reproduction

110 named service checks and 91 captured native events exercise the real service with a deterministic loopback Chat Completions provider. This proves service admission, execution, tool dispatch, attention, persistence reads, interruption, and event shapes with controlled provider output. It does not prove a commercial model follows instructions or validate provider onboarding. No credentials, subscription, paid inference, or installed user service are needed. The fixture asks only for a harmless read and a question; it does not execute generated shell commands or write project files.

Prerequisites: Windows x64, PowerShell 7 with .NET APIs used by the existing verifiers, Node.js with ES modules, and the separately downloaded `@opencode/cli-windows-x64` 2.1.4 executable. Download/provenance instructions are in the [connection contract](opencode-connection-contract.md#reproduction-and-cleanup). The verifier refuses another binary hash.

```powershell
pwsh -NoProfile -File docs/integration/verify-task-workflow.ps1 -ExecutablePath artifacts/wi006/runtime/package/bin/opencode.exe -OutputDirectory artifacts/wi007
```

The script starts two owned loopback processes on ephemeral ports, strips inherited credential/OpenCode variables from the service, isolates configuration/data/cache/state/home/temp, disables project configuration and model catalog fetching, and checks version/PID/temp-root identity. Only the disposable service receives its generated Basic password. The configured default and explicitly selected model point at the fixture; no real provider is selected. Model responses are projected before retention because raw model settings can contain secrets. Processes are killed and the validated owned scratch root is removed in `finally`. Output must remain outside scratch. Do not point this verifier at an existing service or actual project.

A real-model acceptance run is a separate opt-in check: provision an enabled server-side model and its credential, use a disposable workspace, and accept that inference may cost money. The desktop must explain missing access and never copy credentials into model-picker state or logs. No real-model run or native UI acceptance is claimed here.

## Selection and admission

| Operation | Verified behavior | Desktop rule |
| --- | --- | --- |
| `GET /api/model?location[directory]=…`, `/api/model/default`, `/api/agent?location[directory]=…` | Location-scoped catalog; models include `enabled`, `status`, provider ID and model ID; agents include primary/subagent mode and hidden flag. Default-model retrieval was already exercised by WI-005. | Offer enabled active models and non-hidden primary agents. Project safe identity/availability fields, omitting settings/headers/body. Explain an empty catalog. Revalidate selection before sending. |
| `POST /api/session/{id}/model` | Body `{ "model": { "id": "fixture", "providerID": "wi007" } }`, 204. Even an unknown model ID returns 204. | Selection acknowledgement is not proof of availability. Refresh metadata and catalog; display subsequent execution failure honestly. |
| `POST /api/session/{id}/agent` | Body `{ "agent": "build" }`, 204; unknown agent also returns 204. Selection writes recorded model/agent-switched messages. | Do not offer arbitrary IDs or interpret 204 as availability validation. Selection changes affect subsequent provider turns, not already running work. |
| `POST /api/session/{id}/prompt` | `{ "id": "msg_…", "text": "…", "delivery": "steer" or "queue", "resume": boolean }`; only text is required. 200 returns `data` containing inbox item identity, owning session, creation time, payload and delivery. | 200 means durable admission, not completion or even persisted conversation rendering. Allocate a stable message ID before the request and keep it with the draft/delivery record. |
| Same prompt ID repeated | Both identical and changed text returned 200 with the original item; inbox still contained one original payload. | Reusing an ID does not update input. Never silently send edited content under an old ID. This observation does not authorize automatic POST retry. |
| `GET /api/session/{id}/inbox` | Lists durable pending work. `resume:false` parks an admitted queue item without delivering it into history. Queued input survives `interrupt?resume=false`. | Distinguish queued work from delivered history. WI-008 gates additional sends while execution or unresolved delivery exists; advanced queue editing remains excluded. |

Production WI-008 uses one text send with explicit `delivery:"steer"` and a stable ID, after confirmed selection in an inactive session. Its gate covers selection mutations and send, preventing navigation/selection changes from retargeting an in-flight operation. Programmatic queued input is covered here to distinguish admission from progress; WI-008 does not need a queue editor or another resume endpoint.

## Execution and user-visible states

`GET /api/session/active` returns `data` keyed by session ID, with `{ "type":"running" }` for a foreground drain owned by this process; absent sessions are inactive. It is server-wide. It has no special waiting type. A running task awaiting permission or a question remains active. Pair this endpoint with pending requests, not just message timestamps.

**Session metadata retains the previous `outcome` and `time.idle` while a new turn is running.** The held-turn scenario observed `outcome:"failed"` and an old idle timestamp alongside an active running drain and new text deltas. Never render those metadata fields as the current outcome while active.

| State shown to user | Observable basis and interaction |
| --- | --- |
| Sending | One local mutation is in flight. Keep original text/ID and disable duplicate send. |
| Accepted / queued | Admission returned, or stable ID is found in inbox; no terminal result implied. |
| Running | Owning session appears in active map or receives an execution-start event. Text/tool progress is incremental. |
| Waiting for you | Pending permission/form belongs to the session; overlay this on execution activity. An externally created request can also exist on an inactive session, so attention alone does not imply a running agent. |
| Stopping | Interrupt is in flight; keep applying same-session events. |
| Completed / failed / interrupted | After reconciling inactive execution, use terminal session outcome and the latest recorded idle boundary/history. Fixture scenarios observed all three outcomes. An assistant finish or tool success alone does not finish the task. |
| Live state unavailable / checking | Transport broke or authoritative queries failed. Keep the last observation visibly stale; do not change it to failed/completed. |
| Delivery uncertain | Prompt acknowledgement was lost and reconciliation has not established whether its ID exists. Preserve input; do not resend. Absence from one page is insufficient evidence of rejection. |

`POST /api/experimental/session/{id}/wait` returns 204 when the loop is idle; the verifier uses it with a deadline and then checks state/history. It is not an outcome payload, and it can remain blocked while waiting for user attention. It is not required for the production UI, which uses events plus REST reconciliation. An inactive new session without a terminal boundary is simply ready, not a completed task. Historical idle boundaries describe their own turns, not later submissions.

## Conversation and native events

Authenticated `GET /api/event` returns SSE across **all locations**. This run observed `data: {JSON}` frames, comments/heartbeats and an initial `server.connected`. Parse SSE frame boundaries, not TCP chunks; tolerate comments and unknown event types. OpenAPI encodes the event payload as an opaque JSON string (`V2EventEncoded`); the native JSON structure is established by the retained runtime evidence.

Events carry `id`, `type`, and `data`; many carry `created`, `location`, and `durable:{aggregateID,seq,version}`. Some execution events have **no location**. Session ownership is in `data.sessionID` or the durable aggregate/session-created payload, not universally at the top level. Resolve known backend/session ownership before applying. Never require location on every event, infer the selected session, or assume every event has a durable sequence. Text deltas observed here have no durable sequence.

| Observed events | Meaning |
| --- | --- |
| `session.inbox.enqueued`, `.delivered`, `.cancelled` | Admission/delivery/queue removal, not completion. |
| `session.execution.started`, `.succeeded`, `.failed`, `.interrupted` | Drain lifecycle; reconcile authoritative state/history for the resulting boundary. |
| `session.step.started`, `.streamed`, `.ended`, `.failed` | Assistant provider-step lifecycle. |
| `session.text.started`, `.delta`, `.ended` | Associate by session + assistant message ID + ordinal. A delta adds `data.delta`; do not append it again after replacing with fetched history. |
| `session.tool.input.started`, `.input.ended`, `.called`, `.success` | Tool-call lifecycle associated with assistant message/tool ID. Unknown/failure variants must fall back safely and trigger refresh. |
| `permission.asked`, `.replied`; `form.created`, `.replied`, `.cancelled` | Invalidate/reconcile the owning pending-attention collection. |

History remains `GET /api/session/{id}/message` with the pagination rules from [WI-005](opencode-project-session-contract.md). Assistant content contains text (including fenced code) and tool entries. The real `read` tool projected `name`, tool ID, input, `state.status:"completed"`, content, metadata and times. Its `executed` field was false even though the completed state and returned file content proved the read; do not use that flag alone as a UI success/failure indicator. Provider rejection projected an assistant error and an idle failed entry; interruption retained partial assistant text, `finish:"error"`/`error.type:"aborted"`, then an idle interrupted entry. Render code as content, show readable tool progress/results, and avoid exposing raw provider response bodies/headers as diagnostic text.

## Permissions

Pending reads: session `GET /api/session/{id}/permission`, or location `GET /api/permission/request?location[directory]=…`. Each request includes ID, session ID, action and resources, and may include save patterns, source, metadata and message. Show action/resources and proposed save patterns before replying.

Reply with `POST /api/session/{id}/permission/{requestID}/reply`, `{ "decision":"once" | "always" | "reject", "message": optional }`, 204. Once allowed the real read to finish. Direct service-created requests additionally exercised all three decisions, native reply events, and stale reply 404. The standalone create endpoint returns the evaluation `effect:"ask"` before the decision; its response is not the eventual user's allow/deny outcome.

- **Allow once:** resolve this request; the next same-resource request asked again.
- **Save allow rule:** `always` with proposed save `*` created a project-associated saved rule (`GET /api/permission/saved`) and allowed another resource and another session in that project. Label the proposed action/pattern and project scope, not merely “always.” Cross-project isolation, restart persistence and a request without save patterns were not tested; do not promise them from this observation. Refresh saved/pending state after replying.
- **Reject:** resolve this request with the reject choice; do not imply the whole task necessarily fails. The model may handle tool rejection. Automatic rejection on disconnect is forbidden.

Already-resolved/missing permission replies return 404. Disable the action, refresh the list and say the request is no longer available. After a lost reply, query state; never replay the decision automatically. No permission TTL or timed expiry contract was established. Do not invent expiry countdowns.

## Forms and question tool

The real question tool created a session form with `metadata.kind:"question"`, an owning message/tool reference, and a string-option field `q0` with `custom:true`. Answering it resumed execution to success. Render the returned form fields; do not hard-code the question-tool input schema into the desktop.

Pending reads: `GET /api/session/{id}/form` or location `GET /api/form?location[directory]=…`. Detail `GET /api/session/{id}/form/{formID}` includes state. Reply `POST …/reply` with `{ "answer": { "fieldKey": value } }`, 204. Cancel `DELETE …/{formID}` (optional encoded `message` query), 204.

| Field contract | Runtime observations / implementation boundary |
| --- | --- |
| `string` | Required, length/pattern rejection; option value enforcement when custom is absent; string options/custom input from the real question tool. Respect declared email/URI/date/date-time formats, defaults and placeholder from retained schema; these format validators were not independently exercised here. |
| `number`, `integer` | Upper bound and fractional-integer rejection; valid number 2.5 and integer 2 accepted. Apply declared min/max and reject non-finite numeric input in the desktop. |
| `boolean` | String `"yes"` rejected; true and false are typed values, not missing answers. |
| `multiselect` | Declared options and item bounds; too many selections rejected, one allowed value accepted. Preserve array values. |
| `external` | Requires explicit `true` acknowledgement even without `required:true`. Missing acknowledgement returned 400. Show link and acknowledgement, never load/visit/approve it automatically. The fixture does not visit its `.invalid` URL. |
| Conditional/hidden fields | `when` equality false suppresses a required dependent field; equality true requires it. A `hidden:true` required field is **still required**. Preserve supplied defaults/values for hidden controls; a hidden required field without a usable value must show an unsupported-form explanation and offer cancel/refresh, rather than silently manufacturing an answer. `neq` and combinations are schema-declared, not separately runtime-tested here. |

400 `FormInvalidAnswerError` leaves the form pending: retain answers and display the server validation message safely. Successful detail state is `answered` with the typed answer; cancellation detail is `cancelled`. Replies/cancellation after settlement return 409 `FormAlreadySettledError`; missing valid-prefixed form IDs return 404. Refresh detail and disable stale actions. Malformed ID prefixes can yield 400 instead. No timed-expiry state exists in the retained `Form.State` union (`pending`, `answered`, `cancelled`); unavailable 404 is not proof of expiry. Unknown field types/unsafe external schemes must not crash or become silently omitted required fields; explain unsupported content and allow cancellation.

## Stop and recovery

`POST /api/session/{id}/interrupt?resume=false` returned `{ "interrupted":true }` for the held stream and false for the subsequent idle no-op. `resume=false` prevents resuming parked work; the queued next input remained in inbox. Do not discard that input or claim the queue was cleared. Partial text and step/terminal updates may arrive while Stop is pending. Keep rendering them, then re-query active state, metadata, history, inbox and attention. Confirm “Interrupted” from reconciliation, not button activation or HTTP success alone. False means this process interrupted no active execution; show the refreshed state. Ownership in another process and interrupting an OS tool subprocess were not exercised, so no immediate process-termination guarantee is made.

The global SSE contract is volatile: disconnected events are missed; slow-consumer overflow fails the stream. The verifier closes its consumer, submits work whose response is deliberately discarded by the simulated client, lets it finish, and reconnects. A stable prompt ID appears exactly once in recovered history without a second POST. A real agent question created during this outage is rediscovered through pending-form/detail reads, answered, and completes. Settled/cancelled forms are absent from pending listings. This is an actual subscriber disconnect and a **controlled client-discarded acknowledgement**, not a wire-level dropped HTTP response or full server restart.

Recovery algorithm for WI-009 (and the explicit refresh path in WI-008):

1. Retain backend identity, connection generation, server directory, session ID, stable input ID and unsent text. Mark live state unavailable; freeze mutation retries.
2. Authenticate the same supported backend; subscribe to global events **before** hydration and buffer/invalidate affected sessions while queries run.
3. Fetch owning session metadata, active map, newest history plus required older pages, inbox, and pending permissions/forms for opened directories. Fetch request detail when needed. Include inactive sessions with pending attention. Guard every result by backend/selection generation.
4. Reconcile stable input ID against both inbox and paged history. Found means admitted; completed work can already have left inbox. Not found means uncertain until sufficient authoritative evidence is obtained; preserve text and offer explicit refresh, never automatic resend or an invented failed state.
5. Replace fetched projections by identity, then process buffered changes. Re-fetch sessions touched during hydration or by ambiguous/unknown events; deduplicate IDs and avoid appending deltas onto already-refreshed complete text. There is no atomic multi-endpoint snapshot or replay cursor established here. Do not declare state current if one required query fails or intervening changes remain unreconciled.
6. Derive active/waiting/queued/last-terminal state together. A pending decision that disappeared becomes non-actionable; new attention remains discoverable across navigation. Restore live status only after successful hydration. Keep drafts unsent.

The experimental durable-log endpoint was also queried with `after=0&follow=false`; this fixture returned only a `log.synced` marker at sequence 60, **no usable event replay**. Its schema/description is not evidence of complete replay in this environment. Do not implement continuity by replaying the global event ID or relying on this endpoint. Text deltas lack durable sequences regardless. Recovery through REST does not need it.

Retry policy is a desktop decision: WI-009 retries recoverable read/stream failures after 1, 2, 4, 8 and 15 seconds, then offers explicit retry; no mutation retry. Treat 401 as requiring reconnect, unsupported version/protocol as requiring user action, and cancellation/backend replacement as ending that recovery generation. App restart restores server-side work only after explicit authentication/project/session selection; profiles/passwords/drafts are not persisted by this increment.

## Remaining limits

No timed expiry, subscriber overflow load test, commercial-model behavior, provider authentication, multi-process execution ownership, server restart, wire-level lost POST response, cross-project saved-permission boundary, or native WinUI interaction is claimed as runtime-verified. These do not prevent implementing the conservative state/reconciliation rules above. Controlled implementation tests must cover the failure/race cases and additional schema controls; native and real-model acceptance remain explicit prerequisites for implementation acceptance. WI-006's pending UI acceptance remains unchanged. WI-008/WI-009 are refined separately; this investigation does not start either implementation.
