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
$scratch = Join-Path $tempParent ('ocd-wi005-' + [Guid]::NewGuid().ToString('N'))
if ($output.StartsWith($scratch, [StringComparison]::OrdinalIgnoreCase)) { throw 'Output must be outside scratch storage.' }
New-Item -ItemType Directory $scratch | Out-Null
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
    $projectA = Join-Path $scratch 'project A 测试'
    $projectB = Join-Path $scratch 'project B'
    New-Item -ItemType Directory $projectA, $projectB | Out-Null
    # Empty git roots deliberately have no commits or user configuration.
    foreach ($directory in @($projectA, $projectB)) {
        & git -C $directory init --quiet
        if ($LASTEXITCODE -ne 0) { throw 'Disposable git initialization failed.' }
    }
    function LocationQuery([string] $Directory) { return 'location%5Bdirectory%5D=' + [Uri]::EscapeDataString($Directory) }
    $query = LocationQuery $projectA
    $location = Check 'openProjectA' "/api/location?$query"
    $null = Check 'validateDirectory' "/api/fs/list?$query"
    $otherLocation = Check 'openProjectB' ('/api/location?' + (LocationQuery $projectB))
    if ($location.directory -ne $projectA -or $otherLocation.directory -ne $projectB) { throw 'Project resolution/isolation failed.' }
    $filter = 'directory=' + [Uri]::EscapeDataString($location.directory)
    $empty = Check 'emptySessions' "/api/session?$filter"
    if ($empty.data.Count -ne 0) { throw 'Isolated project was not empty.' }
    $created = Check 'createSession' '/api/session' 200 'POST' @{ location = @{ directory = $location.directory }; title = 'WI-005 disposable history' }
    $session = $created.data.id
    $listed = Check 'listCreatedSession' "/api/session?$filter"
    if ($listed.data.Count -ne 1 -or $listed.data[0].id -ne $session) { throw 'Created session absent from directory listing.' }
    $reopened = Check 'reopenSession' "/api/session/$session"
    if ($reopened.data.location.directory -ne $location.directory) { throw 'Session ownership mismatch.' }
    $emptyHistory = Check 'emptyHistory' "/api/session/$session/message?limit=2&order=desc"
    if ($emptyHistory.data.Count -ne 0) { throw 'New session history was not empty.' }
    # Import a projected history fixture: synthetic submission only queues inbox items,
    # so it cannot exercise persisted history without execution. Import never invokes a provider.
    $fixtureInfo = $created.data.Clone()
    $fixtureInfo.id = 'ses_wi005history'
    $messages = @(foreach ($number in 1..5) {
        @{ id = "msg_wi005_$number"; type = 'synthetic'; text = "WI-005 fixture $number"; time = @{ created = 1700000000000 + $number } }
    })
    $null = Check 'importHistoryFixture' '/api/experimental/session/import' 200 'POST' @{ info = $fixtureInfo; messages = $messages; location = @{ directory = $location.directory } }
    $session = $fixtureInfo.id
    $null = Check 'reopenHistorySession' "/api/session/$session"
    $first = Check 'historyNewest' "/api/session/$session/message?limit=2&order=desc"
    if ($first.data.Count -ne 2 -or -not $first.cursor.next) { throw 'Expected a populated history cursor.' }
    $cursor = [Uri]::EscapeDataString($first.cursor.next)
    $second = Check 'historyOlder' "/api/session/$session/message?limit=2&cursor=$cursor"
    $third = Check 'historyOldest' ("/api/session/$session/message?limit=2&cursor=" + [Uri]::EscapeDataString($second.cursor.next))
    $ids = @($first.data.id) + @($second.data.id) + @($third.data.id)
    $end = Check 'historyEnd' ("/api/session/$session/message?limit=2&cursor=" + [Uri]::EscapeDataString($third.cursor.next))
    if ($ids.Count -ne 5 -or @($ids | Select-Object -Unique).Count -ne 5 -or $end.data.Count -ne 0) { throw 'History pagination lost/duplicated fixture messages.' }
    $null = Check 'listedSession' "/api/session?$filter&limit=1&order=desc"
    $null = Check 'createSecondSession' '/api/session' 200 'POST' @{ location = @{ directory = $location.directory }; title = 'WI-005 second session' }
    $sessions = Check 'sessionPageOne' "/api/session?$filter&limit=1&order=desc"
    $nextSessions = Check 'sessionPageTwo' ("/api/session?$filter&limit=1&cursor=" + [Uri]::EscapeDataString($sessions.cursor.next))
    if ($sessions.data.Count -ne 1 -or $nextSessions.data.Count -ne 1 -or $sessions.data[0].id -eq $nextSessions.data[0].id) { throw 'Session pagination failed.' }
    $other = Check 'otherProjectSessions' ('/api/session?directory=' + [Uri]::EscapeDataString($otherLocation.directory))
    if ($other.data.Count -ne 0) { throw 'Sessions leaked to the other project.' }
    $null = Check 'missingDirectory' ('/api/location?' + (LocationQuery (Join-Path $scratch 'missing'))) 404
    $file = Join-Path $scratch 'ordinary-file.txt'
    Set-Content $file 'fixture'
    $null = Check 'fileAsDirectory' ('/api/location?' + (LocationQuery $file)) 200
    $null = Check 'validateFileAsDirectory' ('/api/fs/list?' + (LocationQuery $file)) 404
    $null = Check 'missingSession' '/api/session/ses_missing' 404
    $null = Check 'missingSessionHistory' '/api/session/ses_missing/message' 404
    $null = Check 'invalidHistoryCursor' "/api/session/$session/message?cursor=invalid" 400
    $null = Check 'invalidSessionCursor' '/api/session?cursor=invalid' 400
    $null = Check 'missingAuthorization' "/api/session?$filter" 401 'GET' $null ''
    $null = Check 'wrongAuthorization' "/api/session/$session/message" 401 'GET' $null 'Basic b3BlbmNvZGU6d3Jvbmc='
    $null = Check 'models' "/api/model?$query"
    $null = Check 'agents' "/api/agent?$query"
    $null = Check 'defaultModel' "/api/model/default?$query"
    $contract = Check 'runtimeContract' '/openapi.json'
    $paths = @('/api/location', '/api/fs/list', '/api/project', '/api/session', '/api/session/{sessionID}', '/api/session/{sessionID}/message', '/api/experimental/session/import', '/api/model', '/api/model/default', '/api/agent')
    $slice = [ordered]@{ openapi = $contract.openapi; info = $contract.info; paths = [ordered]@{}; components = @{ schemas = [ordered]@{} } }
    foreach ($path in $paths) { $slice.paths[$path] = $contract.paths[$path] }
    # Retain the transitive schema closure so every local reference resolves.
    do {
        $added = $false
        $json = $slice | ConvertTo-Json -Depth 100
        foreach ($match in [regex]::Matches($json, '#/components/schemas/([^"\s]+)')) {
            $key = $match.Groups[1].Value
            if (-not $slice.components.schemas.Contains($key)) {
                $slice.components.schemas[$key] = $contract.components.schemas[$key]
                $added = $true
            }
        }
    } while ($added)
    $snapshotPath = Join-Path $PSScriptRoot 'contracts/opencode-2.1.4-project-session.openapi.json'
    if (Test-Path -LiteralPath $snapshotPath) {
        $expected = [Text.Json.Nodes.JsonNode]::Parse([IO.File]::ReadAllText($snapshotPath))
        $actual = [Text.Json.Nodes.JsonNode]::Parse(($slice | ConvertTo-Json -Depth 100))
        if (-not [Text.Json.Nodes.JsonNode]::DeepEquals($expected, $actual)) { throw 'Runtime project/session contract differs from snapshot.' }
    }
    $slice | ConvertTo-Json -Depth 100 | Set-Content (Join-Path $output 'project-session.openapi.json') -Encoding utf8NoBOM
    $cases.Remove('runtimeContract')
    foreach ($name in @($cases.Keys)) {
        $body = $cases[$name].body | ConvertFrom-Json -AsHashtable
        if ($body.cursor) {
            foreach ($key in @($body.cursor.Keys)) { if ($body.cursor[$key]) { $body.cursor[$key] = '<opaque-cursor>' } }
        }
        if ($name -in @('models', 'defaultModel')) {
            $body.data = @($body.data | ForEach-Object { @{ id = $_.id; providerID = $_.providerID; modelID = $_.modelID; enabled = $_.enabled; status = $_.status } })
        }
        if ($name -eq 'agents') {
            $body.data = @($body.data | ForEach-Object { @{ id = $_.id; mode = $_.mode; hidden = $_.hidden } })
        }
        $cases[$name].body = $body
    }
    $evidence = [ordered]@{ serverVersion = $info.version; executableSha256 = $expectedHash; verifiedAtUtc = [DateTime]::UtcNow.ToString('o'); sanitization = 'Owned root replaced; nonempty cursors replaced; model/agent bodies projected to availability fields. No authorization retained.'; cases = $cases }
    $json = $evidence | ConvertTo-Json -Depth 100
    # Sanitize both raw and JSON-escaped owned paths, never retaining authorization.
    $json = $json.Replace(($scratch | ConvertTo-Json -Compress).Trim('"'), '<isolated-root>').Replace($scratch, '<isolated-root>')
    if ($json.Contains($password) -or $json.Contains($authorization)) { throw 'Credential leaked into evidence.' }
    [IO.File]::WriteAllText((Join-Path $output 'project-session-evidence.json'), $json + "`n")
    Write-Output "Verified project/session workflow; evidence saved to $output"
}
finally {
    if ($server) {
        if (-not $server.HasExited) { $server.Kill($true); $server.WaitForExit() }
        $server.Dispose()
    }
    $client.Dispose(); $handler.Dispose()
    $resolvedScratch = [IO.Path]::GetFullPath($scratch)
    if (-not $resolvedScratch.StartsWith($tempParent.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase) -or
        [IO.Path]::GetFileName($resolvedScratch) -notmatch '^ocd-wi005-[0-9a-f]{32}$') { throw 'Unexpected cleanup path.' }
    for ($retry = 0; ; $retry++) {
        try { Remove-Item -LiteralPath $resolvedScratch -Recurse -Force; break }
        catch { if ($retry -ge 20) { throw }; Start-Sleep -Milliseconds 100 }
    }
}
