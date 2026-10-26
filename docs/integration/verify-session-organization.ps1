# Requires PowerShell 7. Run only against the pinned, separately downloaded CLI.
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
$scratch = Join-Path $tempParent ('ocd-wi018-' + [Guid]::NewGuid().ToString('N'))
if ($output.StartsWith($scratch, [StringComparison]::OrdinalIgnoreCase)) { throw 'Output must be outside scratch storage.' }
New-Item -ItemType Directory $scratch | Out-Null
$events = $null
$server = $null
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

try {
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
        if ($result.status -ne $Status) { throw "$Name expected HTTP $Status, got HTTP $($result.status). Response body omitted." }
        $cases[$Name] = $result
        if ($result.body) { return $result.body | ConvertFrom-Json -AsHashtable }
    }
    if (-not ('Wi007Events' -as [type])) { Add-Type -Path (Join-Path $PSScriptRoot 'task-events-fixture.cs') }
    $events = [Wi007Events]::new("$baseUrl/api/event", $authorization)
    for ($attempt=0; $attempt -lt 100 -and -not $events.Connected; $attempt++) { Start-Sleep -Milliseconds 20 }
    if (-not $events.Connected) { throw 'Event stream unavailable.' }
    $projectA = Join-Path $scratch 'project A 测试'
    New-Item -ItemType Directory $projectA | Out-Null
    $q = 'directory=' + [Uri]::EscapeDataString($projectA)
    $created = Check 'create' '/api/session' 200 'POST' @{location=@{directory=$projectA};title='Older # % 测试'}
    $id = $created.data.id
    $null = Check 'createSecond' '/api/session' 200 'POST' @{location=@{directory=$projectA};title='Newer # % 测试'}
    $first = Check 'firstFilteredPage' "/api/session?${q}&search=%23%20%25&archived=false&limit=1&order=desc"
    if ($first.data.Count -ne 1 -or -not $first.cursor.next) { throw 'Expected filtered pagination.' }
    $next = Check 'nextFilteredPage' ("/api/session?${q}&search=%23%20%25&archived=false&limit=1&cursor=" + [Uri]::EscapeDataString($first.cursor.next))
    if ($next.data[0].id -ne $id) { throw 'Older filtered session missing.' }
    $null = Check 'rename' "/api/session/$id" 204 'PATCH' @{title='Renamed 世界 # %'}
    $renamed = Check 'renamedMetadata' "/api/session/$id"
    if ($renamed.data.title -ne 'Renamed 世界 # %') { throw 'Rename did not persist.' }
    $null = Check 'emptyTitle' "/api/session/$id" 204 'PATCH' @{title=''}
    $null = Check 'emptyTitleMetadata' "/api/session/$id"
    $null = Check 'restoreTitle' "/api/session/$id" 204 'PATCH' @{title='Renamed 世界 # %'}
    $fixture = $renamed.data.Clone()
    $fixture.id = 'ses_wi018_content'
    $null = Check 'importHistory' '/api/experimental/session/import' 200 'POST' @{info=$fixture;location=@{directory=$projectA};messages=@(@{id='msg_wi018_content';type='user';time=@{created=1};text='Archive preserves this history'})}
    $id = $fixture.id
    $fixtureFile = Join-Path $projectA 'preserved.txt'
    [IO.File]::WriteAllText($fixtureFile, 'preserved fixture content')
    $before = Check 'historyBeforeArchive' "/api/session/$id/message?limit=10"
    $null = Check 'archive' "/api/session/$id" 204 'PATCH' @{archived=$true}
    $archived = Check 'archivedMetadata' "/api/session/$id"
    if (-not $archived.data.time.archived) { throw 'Missing archive timestamp.' }
    $active = Check 'activeSearch' "/api/session?${q}&search=Renamed&archived=false&limit=1"
    if (@($active.data | Where-Object id -eq $id).Count) { throw 'Archive still visible in active history.' }
    $list = Check 'archivedSearch' "/api/session?${q}&search=Renamed&archived=true&limit=1"
    if ($list.data[0].id -ne $id) { throw 'Archived session missing.' }
    $after = Check 'historyAfterArchive' "/api/session/$id/message?limit=10"
    if (-not [Text.Json.Nodes.JsonNode]::DeepEquals([Text.Json.Nodes.JsonNode]::Parse(($before | ConvertTo-Json -Depth 100)), [Text.Json.Nodes.JsonNode]::Parse(($after | ConvertTo-Json -Depth 100)))) { throw 'History changed.' }
    $null = Check 'archiveAgain' "/api/session/$id" 204 'PATCH' @{archived=$true}
    $null = Check 'restore' "/api/session/$id" 204 'PATCH' @{archived=$false}
    $restored = Check 'restoredMetadata' "/api/session/$id"
    if ($restored.data.time.archived) { throw 'Archive timestamp remained.' }
    $empty = Check 'noMatches' "/api/session?${q}&search=doesnotexist&archived=false"
    if ($empty.data.Count) { throw 'Unexpected search result.' }
    $null = Check 'invalidTitleType' "/api/session/$id" 400 'PATCH' @{title=123}
    $null = Check 'missingMutation' '/api/session/ses_missing' 404 'PATCH' @{title='missing'}
    $runningFixture = Check 'createRunning' '/api/session' 200 'POST' @{location=@{directory=$projectA};title='Running archive fixture'}
    $runningId = $runningFixture.data.id
    $null = Check 'runningModel' "/api/session/$runningId/model" 204 'POST' @{model=@{id='fixture';providerID='wi007'}}
    $null = Check 'runningAgent' "/api/session/$runningId/agent" 204 'POST' @{agent='build'}
    $null = Check 'startRunning' "/api/session/$runningId/prompt" 200 'POST' @{text='WI007:stall'}
    for ($attempt=0; $attempt -lt 50; $attempt++) {
        $active = Check 'activeBeforeArchive' '/api/session/active'
        if ($active.data.Contains($runningId)) { break }
        Start-Sleep -Milliseconds 100
    }
    if (-not $active.data.Contains($runningId)) { throw 'Fixture never became active.' }
    $null = Check 'archiveRunning' "/api/session/$runningId" 204 'PATCH' @{archived=$true}
    $active = Check 'activeAfterArchive' '/api/session/active'
    $null = Check 'archivedRunningMetadata' "/api/session/$runningId"
    $null = Check 'stopRunning' "/api/session/$runningId/interrupt?resume=false" 200 'POST'
    $null = Check 'archivedAskRule' "/api/session/$runningId" 204 'PATCH' @{permissions=@(@{action='wi018';resource='*';effect='ask'})}
    $null = Check 'archivedPermission' "/api/session/$runningId/permission" 200 'POST' @{action='wi018';resources=@('preserved.txt');save=@('*')}
    $attention = Check 'archivedGlobalAttention' ('/api/permission/request?location%5Bdirectory%5D='+[Uri]::EscapeDataString($projectA))
    if (-not @($attention.data | Where-Object sessionID -eq $runningId).Count) { throw 'Archived attention was hidden.' }
    $requestId = @($attention.data | Where-Object sessionID -eq $runningId)[0].id
    $null = Check 'replyArchivedPermission' "/api/session/$runningId/permission/$requestId/reply" 204 'POST' @{decision='reject'}
    if ([IO.File]::ReadAllText($fixtureFile) -ne 'preserved fixture content') { throw 'File was changed.' }
    $cases['filePreserved'] = @{ unchanged=$true }
    Start-Sleep -Milliseconds 200
    $events.Dispose()
    $eventData = @($events.Lines.ToArray() | Where-Object { $_.StartsWith('data: ') } | ForEach-Object { $_.Substring(6) | ConvertFrom-Json -AsHashtable })
    $events = $null
    foreach ($type in @('session.renamed','session.archived','session.unarchived')) {
        if (-not @($eventData | Where-Object type -eq $type).Count) { throw "Missing organization event: $type" }
    }
    $contract = Check 'runtimeContract' '/openapi.json'
    $paths = @('/api/session','/api/session/{sessionID}','/api/session/{sessionID}/message','/api/event')
    $slice = [ordered]@{ openapi = $contract.openapi; info = $contract.info; paths = [ordered]@{}; components = @{ schemas = [ordered]@{} } }
    foreach ($path in $paths) { $slice.paths[$path] = $contract.paths[$path] }
    do {
        $added = $false
        foreach ($match in [regex]::Matches(($slice | ConvertTo-Json -Depth 100), '#/components/schemas/([^"\s]+)')) {
            $key = $match.Groups[1].Value
            if (-not $slice.components.schemas.Contains($key)) { $slice.components.schemas[$key] = $contract.components.schemas[$key]; $added = $true }
        }
    } while ($added)
    $snapshot = Join-Path $PSScriptRoot 'contracts/opencode-2.1.4-organization.openapi.json'
    if (Test-Path $snapshot) {
        if (-not [Text.Json.Nodes.JsonNode]::DeepEquals([Text.Json.Nodes.JsonNode]::Parse([IO.File]::ReadAllText($snapshot)), [Text.Json.Nodes.JsonNode]::Parse(($slice | ConvertTo-Json -Depth 100)))) { throw 'Session organization contract changed.' }
    }
    $slice | ConvertTo-Json -Depth 100 | Set-Content (Join-Path $output 'organization.openapi.json') -Encoding utf8NoBOM
    $cases.Remove('runtimeContract')
    foreach ($name in @($cases.Keys)) {
        if ($cases[$name].body) {
            $cases[$name].body = $cases[$name].body | ConvertFrom-Json -AsHashtable
            if ($cases[$name].body.cursor) {
                foreach ($key in @('next','previous')) {
                    if ($cases[$name].body.cursor[$key]) { $cases[$name].body.cursor[$key] = '<opaque-cursor>' }
                }
            }
        }
    }
    $evidence = @{ version = $info.version; executableSha256 = $expectedHash; cases = $cases; events = $eventData; unobserved = @('Atomic archive eligibility is not provided by the contract','Remote host and POSIX runtime') }
    $json = ($evidence | ConvertTo-Json -Depth 100).Replace(($scratch | ConvertTo-Json -Compress).Trim('"'), '<isolated-root>').Replace($scratch, '<isolated-root>')
    $json = $json.Replace(([Uri]$scratch).AbsoluteUri, 'file:///<isolated-root>')
    if ($json.Contains($password) -or $json.Contains($authorization)) { throw 'Credential in evidence.' }
    [IO.File]::WriteAllText((Join-Path $output 'organization-evidence.json'),$json+"`n")
    Write-Output "Session organization probes passed; evidence saved to $output"
}
finally {
    if ($events) { $events.Dispose() }
    if ($provider) { if (-not $provider.HasExited) { $provider.Kill($true); $provider.WaitForExit() }; $provider.Dispose() }
    if ($server) {
        if (-not $server.HasExited) { $server.Kill($true); $server.WaitForExit() }
        $server.Dispose()
    }
    $client.Dispose(); $handler.Dispose()
    $resolvedScratch = [IO.Path]::GetFullPath($scratch)
    if (-not $resolvedScratch.StartsWith($tempParent.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase) -or
        [IO.Path]::GetFileName($resolvedScratch) -notmatch '^ocd-wi018-[0-9a-f]{32}$') { throw 'Unexpected cleanup path.' }
    for ($retry = 0; ; $retry++) {
        try { Remove-Item -LiteralPath $resolvedScratch -Recurse -Force; break }
        catch { if ($retry -ge 20) { throw }; Start-Sleep -Milliseconds 100 }
    }
}
