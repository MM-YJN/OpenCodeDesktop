# Requires PowerShell 7 and Node.js. Uses only the pinned CLI and a loopback provider fixture.
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $ExecutablePath,
    [Parameter(Mandatory)][string] $OutputDirectory
)

$ErrorActionPreference = 'Stop'
$executable = (Resolve-Path -LiteralPath $ExecutablePath).Path
$expectedHash = 'B12B0613E9B4A491E5B12D90A5E9BBAE6D261FE9688C82BF73125A036963D399'
if ((Get-FileHash -LiteralPath $executable -Algorithm SHA256).Hash -ne $expectedHash) {
    throw 'Executable does not match the verified @opencode/cli-windows-x64 2.1.4 artifact.'
}
$output = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force $output | Out-Null
$tempParent = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$scratch = Join-Path $tempParent ('ocd-wi007-' + [Guid]::NewGuid().ToString('N'))
if ($output.StartsWith($scratch, [StringComparison]::OrdinalIgnoreCase)) { throw 'Output must be outside scratch storage.' }
New-Item -ItemType Directory $scratch | Out-Null
$server = $null
$provider = $null
$cases = [ordered]@{}
$handler = [Net.Http.HttpClientHandler]::new()
$handler.UseProxy = $false
$handler.AllowAutoRedirect = $false
$client = [Net.Http.HttpClient]::new($handler)
$client.Timeout = [TimeSpan]::FromSeconds(15)
$password = [Guid]::NewGuid().ToString('N')
$authorization = 'Basic ' + [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes("opencode:$password"))

function Invoke-Probe([string] $Url, [string] $Authorization, [int] $TimeoutMs = 15000, [string] $Method = 'GET', [object] $Payload = $null) {
    $request = [Net.Http.HttpRequestMessage]::new([Net.Http.HttpMethod]::new($Method), $Url)
    if ($null -ne $Payload) { $request.Content = [Net.Http.StringContent]::new(($Payload | ConvertTo-Json -Depth 50 -Compress), [Text.Encoding]::UTF8, 'application/json') }
    $request.Headers.Accept.ParseAdd('application/json')
    $request.Headers.ConnectionClose = $true
    if ($Authorization) { $request.Headers.Add('Authorization', $Authorization) }
    $cancel = [Threading.CancellationTokenSource]::new($TimeoutMs)
    $response = $null
    try {
        $response = $client.SendAsync($request, $cancel.Token).GetAwaiter().GetResult()
        $body = $response.Content.ReadAsStringAsync($cancel.Token).GetAwaiter().GetResult()
        return [ordered]@{
            status = [int]$response.StatusCode
            contentType = [string]$response.Content.Headers.ContentType
            challenge = [string]$response.Headers.WwwAuthenticate
            retryAfter = [string]$response.Headers.RetryAfter
            body = $body
        }
    }
    catch {
        if ($cancel.IsCancellationRequested) { return [ordered]@{ failure = 'timeout' } }
        throw
    }
    finally {
        if ($response) { $response.Dispose() }
        $request.Dispose()
        $cancel.Dispose()
    }
}

$events = $null
try {
    if (-not ('Wi007Events' -as [type])) { Add-Type -Path (Join-Path $PSScriptRoot 'task-events-fixture.cs') }
    $start = [Diagnostics.ProcessStartInfo]::new($executable)
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $start.WorkingDirectory = Join-Path $scratch 'workspace'
    New-Item -ItemType Directory $start.WorkingDirectory | Out-Null
    foreach ($key in @($start.Environment.Keys)) {
        if ($key.StartsWith('OPENCODE_') -or $key -match '(?i)(API_?KEY|TOKEN|SECRET|PASSWORD|CREDENTIAL)') { $start.Environment.Remove($key) | Out-Null }
    }
    $isolated = @{
        XDG_DATA_HOME = "$scratch/data"; XDG_CONFIG_HOME = "$scratch/config"
        XDG_CACHE_HOME = "$scratch/cache"; XDG_STATE_HOME = "$scratch/state"
        TEMP = "$scratch/tmp"; TMP = "$scratch/tmp"; TMPDIR = "$scratch/tmp"
        OPENCODE_TEST_HOME = "$scratch/home"; OPENCODE_CONFIG_DIR = "$scratch/config/opencode"
        OPENCODE_CONFIG_PROJECT_DISABLE = '1'; OPENCODE_DISABLE_MODELS_FETCH = '1'
        OPENCODE_PASSWORD = $password
    }
    foreach ($directory in @('tmp', 'home', 'config/opencode')) {
        New-Item -ItemType Directory -Force (Join-Path $scratch $directory) | Out-Null
    }
    $fixtureStart = [Diagnostics.ProcessStartInfo]::new('node')
    $fixtureStart.UseShellExecute = $false
    $fixtureStart.CreateNoWindow = $true
    $fixtureStart.RedirectStandardOutput = $true
    $fixtureStart.ArgumentList.Add((Join-Path $PSScriptRoot 'task-provider-fixture.mjs'))
    $provider = [Diagnostics.Process]::Start($fixtureStart)
    $providerLine = $provider.StandardOutput.ReadLineAsync()
    if (-not $providerLine.Wait(10000) -or $providerLine.Result -notmatch '^http://127\.0\.0\.1:\d+$') { throw 'Provider fixture did not start.' }
    $providerUrl = $providerLine.Result
    $configuration = @{ model = 'wi007/fixture'; providers = @{ wi007 = @{
        name = 'WI007 loopback fixture'; package = '@opencode/ai/providers/openai-compatible'
        settings = @{ baseURL = "$providerUrl/v1"; apiKey = 'fixture-only'; encoding = 'none' }
        models = @{ fixture = @{ capabilities = @{ tools = $true }; limit = @{ context = 32000; output = 1000 } } }
    } } }
    $configuration | ConvertTo-Json -Depth 20 | Set-Content (Join-Path $scratch 'config/opencode/opencode.json')
    foreach ($entry in $isolated.GetEnumerator()) { $start.Environment[$entry.Key] = $entry.Value }
    foreach ($argument in @('serve', '--hostname', '127.0.0.1', '--port', '0')) {
        $start.ArgumentList.Add($argument)
    }
    $server = [Diagnostics.Process]::Start($start)
    $stderr = $server.StandardError.ReadToEndAsync()
    $line = $server.StandardOutput.ReadLineAsync()
    if (-not $line.Wait(30000) -or $line.Result -notmatch '^server listening on (http://127\.0\.0\.1:\d+)$') {
        throw 'Server did not announce its loopback URL within 30 seconds.'
    }
    $baseUrl = $Matches[1]
    $startupStatuses = [Collections.Generic.List[int]]::new()
    $deadline = [DateTime]::UtcNow.AddSeconds(30)
    do {
        $ready = Invoke-Probe "$baseUrl/api/info" $authorization
        if ($ready.status) { $startupStatuses.Add($ready.status) }
        if ($ready.status -eq 200) { break }
        if ([DateTime]::UtcNow -ge $deadline) { throw 'Server did not become ready within 30 seconds.' }
        Start-Sleep -Milliseconds 100
    } while ($true)
    $info = $ready.body | ConvertFrom-Json
    if ($info.version -ne '2.1.4' -or $info.pid -ne $server.Id) { throw 'Runtime identity mismatch.' }
    if ([IO.Path]::GetFullPath($info.paths.tmp).TrimEnd('\') -ne (Join-Path $scratch 'tmp/opencode').Replace('/', '\')) {
        throw 'Server temporary path escaped the isolated root.'
    }
    function Check([string] $Name, [string] $Path, [int] $Status = 200, [string] $Method = 'GET', [object] $Payload = $null, [string] $Auth = $authorization) {
        $result = Invoke-Probe "$baseUrl$Path" $Auth 15000 $Method $Payload
        $cases[$Name] = $result
        if ($result.status -ne $Status) { throw "$Name expected HTTP $Status, got HTTP $($result.status). Response body omitted." }
        if ($result.body) { return $result.body | ConvertFrom-Json -AsHashtable }
    }
    Set-Content (Join-Path $start.WorkingDirectory 'fixture.txt') 'WI007 harmless tool fixture'
    $query = 'location%5Bdirectory%5D=' + [Uri]::EscapeDataString($start.WorkingDirectory)
    function Assert([bool] $Condition, [string] $Message) { if (-not $Condition) { throw $Message } }
    function New-Session([string] $Name) {
        $created = Check "create_$Name" '/api/session' 200 'POST' @{ location = @{ directory = $start.WorkingDirectory }; title = "WI007 $Name" }
        return $created.data.id
    }
    function Wait-Idle([string] $Id, [string] $Name, [string] $Outcome) {
        $null = Check "wait_$Name" "/api/experimental/session/$Id/wait" 204 'POST'
        $state = Check "state_$Name" "/api/session/$Id"
        Assert ($state.data.outcome -eq $Outcome -and $state.data.time.idle) "Unexpected outcome for $Name"
    }
    function Pending-Permission([string] $Id, [string] $Name, [string] $Decision, [string] $Resource = 'fixture.txt') {
        # Creation evaluates/queues the request; it does not wait for the user's reply.
        $body = Check "evaluated_$Name" "/api/session/$Id/permission" 200 'POST' @{
            action = 'wi007'; resources = @($Resource); save = @('*')
        }
        $pending = Check "pending_$Name" "/api/session/$Id/permission"
        Assert ($pending.data.Count -eq 1) "Expected one pending permission for $Name"
        $permission = $pending.data[0].id
        $null = Check "reply_$Name" "/api/session/$Id/permission/$permission/reply" 204 'POST' @{ decision = $Decision }
        $null = Check "stale_$Name" "/api/session/$Id/permission/$permission/reply" 404 'POST' @{ decision = $Decision }
        return $body
    }
    $models = Check 'models' "/api/model?$query"
    Assert (@($models.data | Where-Object { $_.providerID -eq 'wi007' -and $_.enabled }).Count -eq 1) 'Fixture model unavailable'
    $null = Check 'agents' "/api/agent?$query"
    $session = New-Session 'execution'
    $null = Check 'selectModel' "/api/session/$session/model" 204 'POST' @{ model = @{ id = 'fixture'; providerID = 'wi007' } }
    $null = Check 'selectAgent' "/api/session/$session/agent" 204 'POST' @{ agent = 'build' }
    $null = Check 'invalidModel' "/api/session/$session/model" 204 'POST' @{ model = @{ id = 'missing'; providerID = 'wi007' } }
    $null = Check 'invalidAgent' "/api/session/$session/agent" 204 'POST' @{ agent = 'missing' }
    $null = Check 'restoreModel' "/api/session/$session/model" 204 'POST' @{ model = @{ id = 'fixture'; providerID = 'wi007' } }
    $null = Check 'restoreAgent' "/api/session/$session/agent" 204 'POST' @{ agent = 'build' }
    $events = [Wi007Events]::new("$baseUrl/api/event", $authorization)
    for ($attempt = 0; $attempt -lt 100 -and -not $events.Connected; $attempt++) { Start-Sleep -Milliseconds 20 }
    Assert $events.Connected 'SSE consumer did not connect'
    $queued = Check 'admitParked' "/api/session/$session/prompt" 200 'POST' @{ id = 'msg_wi007parked'; text = 'WI007:complete'; resume = $false; delivery = 'queue' }
    Assert ($queued.data.id -eq 'msg_wi007parked' -and $queued.data.delivery -eq 'queue') 'Prompt admission identity mismatch'
    $inbox = Check 'parkedInbox' "/api/session/$session/inbox"
    Assert (@($inbox.data | Where-Object id -eq 'msg_wi007parked').Count -eq 1) 'Admitted input absent from inbox'
    $null = Check 'parkedHistory' "/api/session/$session/message"
    $null = Check 'duplicatePrompt' "/api/session/$session/prompt" 200 'POST' @{ id = 'msg_wi007parked'; text = 'WI007:complete'; resume = $false }
    $null = Check 'duplicateChangedPrompt' "/api/session/$session/prompt" 200 'POST' @{ id = 'msg_wi007parked'; text = 'changed'; resume = $false }
    $deduplicated = Check 'deduplicatedInbox' "/api/session/$session/inbox"
    Assert ($deduplicated.data.Count -eq 1 -and $deduplicated.data[0].payload.text -eq 'WI007:complete') 'Stable input ID did not retain original payload'
    $null = Check 'removeParked' "/api/session/$session/inbox/msg_wi007parked" 204 'DELETE'
    $null = Check 'promptComplete' "/api/session/$session/prompt" 200 'POST' @{ text = 'WI007:complete' }
    Wait-Idle $session 'complete' 'succeeded'
    $history = Check 'completedHistory' "/api/session/$session/message?limit=50"
    Assert (@($history.data | Where-Object type -eq 'assistant').Count -gt 0) 'No assistant history'
    $null = Check 'promptTool' "/api/session/$session/prompt" 200 'POST' @{ text = 'WI007:tool' }
    Wait-Idle $session 'tool' 'succeeded'
    $null = Check 'toolHistory' "/api/session/$session/message?limit=50"
    $null = Check 'promptFailure' "/api/session/$session/prompt" 200 'POST' @{ text = 'WI007:fail' }
    Wait-Idle $session 'failure' 'failed'
    $null = Check 'failedHistory' "/api/session/$session/message?limit=50"
    $null = Check 'promptStall' "/api/session/$session/prompt" 200 'POST' @{ text = 'WI007:stall' }
    Start-Sleep -Milliseconds 300
    $running = Check 'runningState' "/api/session/$session"
    $active = Check 'activeWhileRunning' '/api/session/active'
    Assert ($active.data[$session].type -eq 'running') 'Held provider did not leave session active'
    $null = Check 'queueWhileRunning' "/api/session/$session/prompt" 200 'POST' @{ id = 'msg_wi007next'; text = 'WI007:complete'; delivery = 'queue' }
    $interrupted = Check 'interruptActive' "/api/session/$session/interrupt?resume=false" 200 'POST'
    Assert $interrupted.interrupted 'Expected active interruption'
    Wait-Idle $session 'interrupted' 'interrupted'
    $null = Check 'interruptedHistory' "/api/session/$session/message?limit=50"
    $null = Check 'inboxAfterInterrupt' "/api/session/$session/inbox"
    $idleInterrupt = Check 'interruptIdle' "/api/session/$session/interrupt?resume=false" 200 'POST'
    Assert (-not $idleInterrupt.interrupted) 'Idle interruption should be a no-op'
    $null = Check 'removeNext' "/api/session/$session/inbox/msg_wi007next" 204 'DELETE'
    # A real read tool waits for a session permission, rather than a standalone fixture request.
    $permissionSession = New-Session 'agentPermission'
    $null = Check 'agentAskRules' "/api/session/$permissionSession" 204 'PATCH' @{ permissions = @(@{ action = 'read'; resource = '*'; effect = 'ask' }) }
    $null = Check 'promptAgentPermission' "/api/session/$permissionSession/prompt" 200 'POST' @{ text = 'WI007:tool' }
    for ($attempt = 0; $attempt -lt 100; $attempt++) {
        $pendingTool = Check 'agentPermissionPending' "/api/session/$permissionSession/permission"
        if ($pendingTool.data.Count) { break }
        Start-Sleep -Milliseconds 50
    }
    Assert ($pendingTool.data.Count -eq 1) 'Read tool did not ask for permission'
    $null = Check 'activeAwaitingPermission' '/api/session/active'
    $requestId = $pendingTool.data[0].id
    $null = Check 'answerAgentPermission' "/api/session/$permissionSession/permission/$requestId/reply" 204 'POST' @{ decision = 'once' }
    Wait-Idle $permissionSession 'agentPermission' 'succeeded'
    $toolHistory = Check 'agentPermissionHistory' "/api/session/$permissionSession/message?limit=50"
    Assert (@($toolHistory.data | Where-Object type -eq 'assistant' | ForEach-Object content | Where-Object { $_.type -eq 'tool' -and $_.state.status -eq 'completed' }).Count -ge 1) 'Read tool did not complete'
    # Standalone service requests exercise real permission storage/replies, without an agent deciding to ask.
    $attention = New-Session 'attention'
    $null = Check 'askRules' "/api/session/$attention" 204 'PATCH' @{ permissions = @(@{ action = 'wi007'; resource = '*'; effect = 'ask' }) }
    $once = Pending-Permission $attention 'once' 'once'
    Assert ($once.data.effect -eq 'ask') 'Once fixture did not ask'
    $reject = Pending-Permission $attention 'rejectAfterOnce' 'reject'
    Assert ($reject.data.effect -eq 'ask') 'Reject fixture did not ask'
    $always = Pending-Permission $attention 'always' 'always'
    Assert ($always.data.effect -eq 'ask') 'Always fixture did not ask'
    $null = Check 'savedPermissions' "/api/permission/saved?$query"
    $auto = Check 'savedAutoAllow' "/api/session/$attention/permission" 200 'POST' @{ action = 'wi007'; resources = @('other.txt') }
    Assert ($auto.data.effect -eq 'allow') 'Saved rule did not allow matching resource'
    $another = New-Session 'savedScope'
    $null = Check 'crossSessionSaved' "/api/session/$another/permission" 200 'POST' @{ action = 'wi007'; resources = @('other.txt') }
    $fields = @(
        @{ key = 'name'; type = 'string'; required = $true; minLength = 2; maxLength = 8; pattern = '^[a-z]+$' },
        @{ key = 'number'; type = 'number'; required = $true; minimum = 1; maximum = 3 },
        @{ key = 'integer'; type = 'integer'; required = $true; minimum = 1; maximum = 3 },
        @{ key = 'confirm'; type = 'boolean'; required = $true },
        @{ key = 'choices'; type = 'multiselect'; required = $true; minItems = 1; maxItems = 1; options = @(@{ value = 'a'; label = 'A' }, @{ value = 'b'; label = 'B' }) },
        @{ key = 'select'; type = 'string'; required = $true; options = @(@{ value = 'a'; label = 'A' }) },
        @{ key = 'external'; type = 'external'; url = 'https://example.invalid/wi007' }
    )
    $form = Check 'createForm' "/api/session/$attention/form" 200 'POST' @{ title = 'WI007 validation fixture'; fields = $fields }
    $formId = $form.data.id
    $null = Check 'locationForms' "/api/form?$query"
    $answer = @{ name = 'valid'; number = 2.5; integer = 2; confirm = $true; choices = @('a'); select = 'a'; external = $true }
    $null = Check 'invalidMissingAnswer' "/api/session/$attention/form/$formId/reply" 400 'POST' @{ answer = @{} }
    foreach ($invalid in @(
        @{ key = 'name'; value = 'A' }, @{ key = 'number'; value = 9 }, @{ key = 'integer'; value = 1.5 },
        @{ key = 'confirm'; value = 'yes' }, @{ key = 'choices'; value = @('a','b') }, @{ key = 'select'; value = 'unknown' }
    )) {
        $bad = $answer.Clone(); $bad[$invalid.key] = $invalid.value
        $null = Check ("invalid_" + $invalid.key) "/api/session/$attention/form/$formId/reply" 400 'POST' @{ answer = $bad }
    }
    $null = Check 'formStillPending' "/api/session/$attention/form/$formId"
    $noAcknowledgement = $answer.Clone(); $noAcknowledgement.Remove('external')
    $null = Check 'invalidExternalAcknowledgement' "/api/session/$attention/form/$formId/reply" 400 'POST' @{ answer = $noAcknowledgement }
    $null = Check 'validFormAnswer' "/api/session/$attention/form/$formId/reply" 204 'POST' @{ answer = $answer }
    $null = Check 'answeredForm' "/api/session/$attention/form/$formId"
    $null = Check 'alreadyAnswered' "/api/session/$attention/form/$formId/reply" 409 'POST' @{ answer = $answer }
    $null = Check 'cancelAnswered' "/api/session/$attention/form/$formId" 409 'DELETE'
    $cancelForm = Check 'createCancelForm' "/api/session/$attention/form" 200 'POST' @{ title = 'Cancel fixture'; fields = @(@{ key = 'text'; type = 'string' }) }
    $cancelId = $cancelForm.data.id
    $null = Check 'cancelForm' "/api/session/$attention/form/$cancelId" 204 'DELETE'
    $null = Check 'cancelledForm' "/api/session/$attention/form/$cancelId"
    $null = Check 'replyCancelled' "/api/session/$attention/form/$cancelId/reply" 409 'POST' @{ answer = @{ text = 'late' } }
    $null = Check 'missingForm' "/api/session/$attention/form/frm_missing" 404
    $null = Check 'missingPermission' "/api/session/$attention/permission/per_missing/reply" 404 'POST' @{ decision = 'once' }
    $conditional = Check 'createConditionalForm' "/api/session/$attention/form" 200 'POST' @{
        title = 'Conditional fields'
        fields = @(
            @{ key = 'show'; type = 'boolean'; required = $true },
            @{ key = 'dependent'; type = 'string'; required = $true; when = @(@{ key = 'show'; op = 'eq'; value = $true }) },
            @{ key = 'hidden'; type = 'string'; required = $true; hidden = $true }
        )
    }
    $conditionalId = $conditional.data.id
    $null = Check 'invalidConditional' "/api/session/$attention/form/$conditionalId/reply" 400 'POST' @{ answer = @{ show = $true } }
    $null = Check 'invalidHiddenRequired' "/api/session/$attention/form/$conditionalId/reply" 400 'POST' @{ answer = @{ show = $false } }
    $null = Check 'validConditional' "/api/session/$attention/form/$conditionalId/reply" 204 'POST' @{ answer = @{ show = $false; hidden = 'preset' } }
    # Disconnect this SSE consumer; the server continues, and the response is deliberately ignored.
    $events.Dispose()
    $eventLines = @($events.Lines.ToArray())
    $events = $null
    $questionSession = New-Session 'agentQuestionDuringOutage'
    $null = Check 'promptQuestion' "/api/session/$questionSession/prompt" 200 'POST' @{ text = 'WI007:question' }
    for ($attempt = 0; $attempt -lt 100; $attempt++) {
        $pendingQuestion = Check 'agentQuestionPending' "/api/session/$questionSession/form"
        if ($pendingQuestion.data.Count) { break }
        Start-Sleep -Milliseconds 50
    }
    Assert ($pendingQuestion.data.Count -eq 1) 'Question tool did not create a form'
    $null = Check 'activeAwaitingQuestion' '/api/session/active'
    $unknownRequest = Invoke-Probe "$baseUrl/api/session/$session/prompt" $authorization 15000 'POST' @{ id = 'msg_wi007lostresponse'; text = 'WI007:complete' }
    Assert ($unknownRequest.status -eq 200) 'Disconnected scenario prompt not admitted'
    $null = Check 'missedWait' "/api/experimental/session/$session/wait" 204 'POST'
    $recoveryEvents = [Wi007Events]::new("$baseUrl/api/event", $authorization)
    try {
        $null = Check 'recoveredState' "/api/session/$session"
        $recovered = Check 'recoveredHistory' "/api/session/$session/message?limit=50"
        Assert (@($recovered.data | Where-Object id -eq 'msg_wi007lostresponse').Count -eq 1) 'Lost-response input not found exactly once'
        $null = Check 'recoveredInbox' "/api/session/$session/inbox"
        $null = Check 'recoveredPermissions' "/api/permission/request?$query"
        $null = Check 'recoveredForms' "/api/form?$query"
        $questionId = $pendingQuestion.data[0].id
        $question = Check 'recoveredQuestion' "/api/session/$questionSession/form/$questionId"
        $key = $question.data.fields[0].key
        $value = $question.data.fields[0].options[0].value
        $null = Check 'replyRecoveredQuestion' "/api/session/$questionSession/form/$questionId/reply" 204 'POST' @{ answer = @{ $key = $value } }
        Wait-Idle $questionSession 'question' 'succeeded'
        $null = Check 'questionHistory' "/api/session/$questionSession/message?limit=50"
        $null = Check 'formsAfterRecovery' "/api/form?$query"
        $null = Check 'activeAfterRecovery' '/api/session/active'
        $log = Invoke-Probe "$baseUrl/api/experimental/session/$session/log?after=0&follow=false" $authorization
        Assert ($log.status -eq 200 -and $log.contentType.StartsWith('text/event-stream')) 'Durable log did not return SSE'
        $cases['durableLog'] = $log
    } finally { $recoveryEvents.Dispose() }
    $stats = Invoke-Probe "$providerUrl/stats" ''
    $cases['providerFixtureCalls'] = @{ status = $stats.status; body = ($stats.body | ConvertFrom-Json -AsHashtable) }
    $contract = Check 'runtimeContract' '/openapi.json'
    $paths = @(
        '/api/model', '/api/model/default', '/api/agent', '/api/session', '/api/session/active',
        '/api/session/{sessionID}', '/api/session/{sessionID}/model', '/api/session/{sessionID}/agent',
        '/api/session/{sessionID}/prompt', '/api/session/{sessionID}/interrupt', '/api/session/{sessionID}/inbox',
        '/api/session/{sessionID}/inbox/{inboxID}', '/api/session/{sessionID}/message',
        '/api/session/{sessionID}/permission', '/api/session/{sessionID}/permission/{requestID}',
        '/api/session/{sessionID}/permission/{requestID}/reply', '/api/permission/request', '/api/permission/saved',
        '/api/session/{sessionID}/form', '/api/session/{sessionID}/form/{formID}',
        '/api/session/{sessionID}/form/{formID}/reply', '/api/form', '/api/event',
        '/api/experimental/session/{sessionID}/wait', '/api/experimental/session/{sessionID}/log'
    )
    $slice = [ordered]@{ openapi = $contract.openapi; info = $contract.info; paths = [ordered]@{}; components = @{ schemas = [ordered]@{} } }
    foreach ($path in $paths) {
        Assert ($null -ne $contract.paths[$path]) "Missing contract path: $path"
        $slice.paths[$path] = $contract.paths[$path]
    }
    do {
        $added = $false
        foreach ($match in [regex]::Matches(($slice | ConvertTo-Json -Depth 100), '#/components/schemas/([^"\s]+)')) {
            $key = $match.Groups[1].Value
            if (-not $slice.components.schemas.Contains($key)) {
                Assert ($null -ne $contract.components.schemas[$key]) "Missing schema: $key"
                $slice.components.schemas[$key] = $contract.components.schemas[$key]
                $added = $true
            }
        }
    } while ($added)
    $snapshotPath = Join-Path $PSScriptRoot 'contracts/opencode-2.1.4-task-workflow.openapi.json'
    if (Test-Path -LiteralPath $snapshotPath) {
        $expected = [Text.Json.Nodes.JsonNode]::Parse([IO.File]::ReadAllText($snapshotPath))
        $actual = [Text.Json.Nodes.JsonNode]::Parse(($slice | ConvertTo-Json -Depth 100))
        Assert ([Text.Json.Nodes.JsonNode]::DeepEquals($expected, $actual)) 'Runtime task contract differs from snapshot'
    }
    $slice | ConvertTo-Json -Depth 100 | Set-Content (Join-Path $output 'task-workflow.openapi.json') -Encoding utf8NoBOM
    $cases.Remove('runtimeContract')
    foreach ($name in @($cases.Keys)) {
        if ($cases[$name].body -is [string] -and $cases[$name].body -and $name -ne 'durableLog') {
            $cases[$name].body = $cases[$name].body | ConvertFrom-Json -AsHashtable
        }
        $body = $cases[$name].body
        if ($body -is [System.Collections.IDictionary] -and $body.cursor) {
            foreach ($key in @($body.cursor.Keys)) { if ($body.cursor[$key]) { $body.cursor[$key] = '<opaque-cursor>' } }
        }
        if ($name -eq 'models') {
            $body.data = @($body.data | ForEach-Object { @{ id = $_.id; providerID = $_.providerID; enabled = $_.enabled; status = $_.status } })
        }
        if ($name -eq 'agents') {
            $body.data = @($body.data | ForEach-Object { @{ id = $_.id; mode = $_.mode; hidden = $_.hidden } })
        }
    }
    $observedEvents = @($eventLines | Where-Object { $_.StartsWith('data: ') } | ForEach-Object { $_.Substring(6) | ConvertFrom-Json -AsHashtable })
    foreach ($type in @('session.execution.started', 'session.execution.succeeded', 'session.execution.failed',
        'session.execution.interrupted', 'session.text.delta', 'session.tool.called', 'session.tool.success', 'permission.asked', 'permission.replied', 'form.created', 'form.replied', 'form.cancelled')) {
        Assert (@($observedEvents | Where-Object type -eq $type).Count -gt 0) "Expected event not observed: $type"
    }
    $evidence = [ordered]@{
        serverVersion = $info.version; executableSha256 = $expectedHash; verifiedAtUtc = [DateTime]::UtcNow.ToString('o')
        basis = 'Real pinned service; deterministic loopback provider, not a live model. SSE explicitly disconnected; one prompt response deliberately discarded, not a wire-level lost-response test.'
        sanitization = 'Owned paths/ports and cursors replaced; model/agent settings omitted; no authorization or real provider credentials.'
        cases = $cases; events = $observedEvents
    }
    $json = $evidence | ConvertTo-Json -Depth 100
    $json = $json.Replace(($scratch | ConvertTo-Json -Compress).Trim('"'), '<isolated-root>').Replace($scratch, '<isolated-root>')
    $json = $json.Replace($baseUrl, '<isolated-server>').Replace($providerUrl, '<fixture-provider>')
    Assert (-not $json.Contains($password) -and -not $json.Contains($authorization)) 'Credential leaked into evidence'
    [IO.File]::WriteAllText((Join-Path $output 'task-workflow-evidence.json'), $json + "`n")
    Write-Output "Verified $($cases.Count) task-workflow cases, $($observedEvents.Count) events, $($slice.components.schemas.Count) schemas."
}

finally {
    if ($events) {
        try { $events.Dispose() }
        catch { Write-Warning 'Event-consumer cleanup failed; continuing owned process cleanup.' }
    }
    if ($server) {
        if (-not $server.HasExited) { $server.Kill($true); $server.WaitForExit() }
        $server.Dispose()
    }
    if ($provider) { if (-not $provider.HasExited) { $provider.Kill($true); $provider.WaitForExit() }; $provider.Dispose() }
    $client.Dispose(); $handler.Dispose()
    $resolvedScratch = [IO.Path]::GetFullPath($scratch)
    if (-not $resolvedScratch.StartsWith($tempParent.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase) -or
        [IO.Path]::GetFileName($resolvedScratch) -notmatch '^ocd-wi007-[0-9a-f]{32}$') { throw 'Unexpected cleanup path.' }
    for ($retry = 0; ; $retry++) {
        try { Remove-Item -LiteralPath $resolvedScratch -Recurse -Force; break }
        catch { if ($retry -ge 20) { throw }; Start-Sleep -Milliseconds 100 }
    }
}
