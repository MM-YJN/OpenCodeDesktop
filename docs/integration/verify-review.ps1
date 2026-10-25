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
$scratch = Join-Path $tempParent ('ocd-wi016-' + [Guid]::NewGuid().ToString('N'))
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
    $fixtureStart = [Diagnostics.ProcessStartInfo]::new('node')
    $fixtureStart.UseShellExecute = $false
    $fixtureStart.CreateNoWindow = $true
    $fixtureStart.RedirectStandardOutput = $true
    $fixtureStart.ArgumentList.Add((Join-Path $PSScriptRoot 'review-provider-fixture.mjs'))
    $fixtureStart.Environment['WI016_FIXTURE_ROOT'] = Join-Path $scratch 'project 测试 #pct%'
    $provider = [Diagnostics.Process]::Start($fixtureStart)
    $providerLine = $provider.StandardOutput.ReadLineAsync()
    if(-not $providerLine.Wait(10000) -or $providerLine.Result -notmatch '^http://127\.0\.0\.1:\d+$') { throw 'Review provider failed to start.' }
    $providerUrl = $providerLine.Result
    @{ model='wi016/fixture'; providers=@{wi016=@{name='WI016 fixture';package='@opencode/ai/providers/openai-compatible';settings=@{baseURL="$providerUrl/v1";apiKey='fixture-only';encoding='none'};models=@{fixture=@{capabilities=@{tools=$true};limit=@{context=32000;output=1000}}}}}} | ConvertTo-Json -Depth 20 | Set-Content (Join-Path $scratch 'config/opencode/opencode.json')
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
    $projectA = Join-Path $scratch 'project 测试 #pct%'
    New-Item -ItemType Directory $projectA | Out-Null
    function FixtureGit([string[]] $Arguments) {
        & git -C $projectA @Arguments | Out-Null
        if ($LASTEXITCODE -ne 0) { throw 'Fixture git command failed.' }
    }
    FixtureGit @('init','-b','main'); FixtureGit @('config','user.email','fixture@example.invalid'); FixtureGit @('config','user.name','Fixture')
    foreach ($name in @('modified.txt','deleted.txt','renamed.txt')) { [IO.File]::WriteAllText((Join-Path $projectA $name), "before`n") }
    [IO.File]::WriteAllBytes((Join-Path $projectA 'binary.bin'),[byte[]]@(0,1,2,3))
    FixtureGit @('add','.'); FixtureGit @('commit','-m','baseline'); FixtureGit @('checkout','-b','feature','main')
    [IO.File]::WriteAllText((Join-Path $projectA 'committed.txt'), "commit`n")
    FixtureGit @('add','.'); FixtureGit @('commit','-m','feature')
    [IO.File]::WriteAllText((Join-Path $projectA 'modified.txt'), "after`n")
    Remove-Item -LiteralPath (Join-Path $projectA 'deleted.txt')
    FixtureGit @('mv','renamed.txt','new-name.txt')
    [IO.File]::WriteAllText((Join-Path $projectA 'untracked 空 #%.txt'), "untracked`n")
    [IO.File]::WriteAllBytes((Join-Path $projectA 'binary.bin'),[byte[]]@(0,5,6,7))
    $query = 'location%5Bdirectory%5D=' + [Uri]::EscapeDataString($projectA)
    $beforeReview = (& git -C $projectA status --porcelain=v1) -join "`n"
    $null = Check 'resolve' "/api/location?$query"
    $null = Check 'info' "/api/vcs?$query"
    $null = Check 'base' "/api/vcs/base?$query"
    $null = Check 'status' "/api/vcs/status?$query"
    foreach ($mode in @('working','branch','committed')) {
        $diff = Check $mode "/api/vcs/diff?$query&mode=$mode&context=3"
        if ($mode -eq 'committed' -and $diff.data.file -notcontains 'committed.txt') { throw 'Committed comparison missing fixture file.' }
        if ($mode -eq 'working') {
            foreach ($expected in @(@('deleted.txt','deleted'),@('modified.txt','modified'),@('new-name.txt','added'),@('renamed.txt','deleted'),@('untracked 空 #%.txt','added'))) {
                if (@($diff.data | Where-Object { $_.file -eq $expected[0] -and $_.status -eq $expected[1] }).Count -ne 1) { throw 'Working comparison fixture mismatch.' }
            }
            if (($diff.data | Where-Object file -eq 'binary.bin').patch -notmatch 'Binary files') { throw 'Missing binary notice.' }
        }
    }
    $null = Check 'explicitBase' "/api/vcs/diff?$query&mode=branch&base=main&context=3"
    $null = Check 'missingBase' "/api/vcs/diff?$query&mode=branch&base=missing-fixture&context=3" 503
    if (((& git -C $projectA status --porcelain=v1) -join "`n") -ne $beforeReview) { throw 'Review operations changed the fixture repository.' }
    $plain = Join-Path $scratch 'plain'; New-Item -ItemType Directory $plain | Out-Null
    $plainQuery = 'location%5Bdirectory%5D=' + [Uri]::EscapeDataString($plain)
    $null = Check 'nonRepositoryInfo' "/api/vcs?$plainQuery"
    $null = Check 'nonRepositoryDiff' "/api/vcs/diff?$plainQuery&mode=working&context=3"
    $created = Check 'createSession' '/api/session' 200 'POST' @{ location = @{ directory = $projectA }; title = 'WI016 fixture' }
    $live = $created.data.id
    $null = Check 'allowFixtureWrite' "/api/session/$live" 204 'PATCH' @{permissions=@(@{action='write';resource='*';effect='allow'},@{action='edit';resource='*';effect='allow'})}
    $null = Check 'selectModel' "/api/session/$live/model" 204 'POST' @{model=@{id='fixture';providerID='wi016'}}
    $null = Check 'livePromptOne' "/api/session/$live/prompt" 200 'POST' @{id='msg_wi016_liveone';text='Controlled first turn'}
    $null = Check 'waitOne' "/api/experimental/session/$live/wait" 204 'POST'
    $liveOne = Check 'liveTurnOne' "/api/session/$live/diff?from=msg_wi016_liveone&context=3"
    if($liveOne.data.file -notcontains 'modified.txt') {throw 'Live snapshot turn did not return modified.txt.'}
    $null = Check 'livePromptTwo' "/api/session/$live/prompt" 200 'POST' @{id='msg_wi016_livetwo';text='Controlled second turn'}
    $null = Check 'waitTwo' "/api/experimental/session/$live/wait" 204 'POST'
    $liveTwo = Check 'liveTurnTwo' "/api/session/$live/diff?from=msg_wi016_livetwo&context=3"
    $liveRange = Check 'liveTurnRange' "/api/session/$live/diff?from=msg_wi016_liveone&to=msg_wi016_livetwo&context=3"
    $null = Check 'liveHistory' "/api/session/$live/message?limit=50&order=asc"
    if(($liveTwo.data | Where-Object file -eq 'modified.txt').patch -notmatch 'controlled turn 1' -or ($liveRange.data | Where-Object file -eq 'modified.txt').patch -notmatch 'controlled turn 2') { throw 'Unexpected snapshot range contents.' }
    $infoFixture = $created.data.Clone(); $infoFixture.id = 'ses_wi016_fixture'
    $messages = @(
        @{ id='msg_wi016_one'; type='user'; time=@{created=1}; text='First fixture turn' },
        @{ id='msg_wi016_idle'; type='idle'; time=@{created=2}; outcome='succeeded' },
        @{ id='msg_wi016_two'; type='user'; time=@{created=3}; text='Second fixture turn' },
        @{ id='msg_wi016_idle2'; type='idle'; time=@{created=4}; outcome='succeeded' }
    )
    $null = Check 'importSession' '/api/experimental/session/import' 200 'POST' @{info=$infoFixture; messages=$messages; location=@{directory=$projectA}}
    $session = $infoFixture.id
    $null = Check 'singleTurn' "/api/session/$session/diff?from=msg_wi016_one&context=3"
    $null = Check 'turnRange' "/api/session/$session/diff?from=msg_wi016_one&to=msg_wi016_two&context=3"
    $null = Check 'missingTurn' "/api/session/$session/diff?from=msg_wi016_missing&context=3" 404
    $null = Check 'missingSession' '/api/session/ses_wi016_missing/diff?from=msg_wi016_one&context=3' 404
    $contract = Check 'runtimeContract' '/openapi.json'
    $paths = @('/api/vcs','/api/vcs/base','/api/vcs/status','/api/vcs/diff','/api/session/{sessionID}/diff')
    $slice = [ordered]@{openapi=$contract.openapi;info=$contract.info;paths=[ordered]@{};components=@{schemas=[ordered]@{}}}
    foreach($path in $paths) {$slice.paths[$path]=$contract.paths[$path]}
    do {
        $added=$false
        foreach($match in [regex]::Matches(($slice | ConvertTo-Json -Depth 100),'#/components/schemas/([^"\s]+)')) {
            $key=$match.Groups[1].Value
            if(-not $slice.components.schemas.Contains($key)) {$slice.components.schemas[$key]=$contract.components.schemas[$key];$added=$true}
        }
    } while($added)
    $snapshot = Join-Path $PSScriptRoot 'contracts/opencode-2.1.4-review.openapi.json'
    if(Test-Path $snapshot) {
        if(-not [Text.Json.Nodes.JsonNode]::DeepEquals([Text.Json.Nodes.JsonNode]::Parse([IO.File]::ReadAllText($snapshot)),[Text.Json.Nodes.JsonNode]::Parse(($slice|ConvertTo-Json -Depth 100)))) {throw 'Review contract changed.'}
    }
    $slice | ConvertTo-Json -Depth 100 | Set-Content (Join-Path $output 'review.openapi.json') -Encoding utf8NoBOM
    $cases.Remove('runtimeContract')
    foreach($name in @($cases.Keys)) {if($cases[$name].body) {$cases[$name].body=$cases[$name].body|ConvertFrom-Json -AsHashtable}}
    $evidence=@{version=$info.version;executableSha256=$expectedHash;cases=$cases;unobserved=@('Steered prompts while busy and active working-copy snapshots','Remote/POSIX providers','Concurrent external repository edits')}
    $json=($evidence|ConvertTo-Json -Depth 100).Replace(($scratch|ConvertTo-Json -Compress).Trim('"'),'<isolated-root>').Replace($scratch,'<isolated-root>')
    if($json.Contains($password) -or $json.Contains($authorization)) {throw 'Credential in evidence.'}
    [IO.File]::WriteAllText((Join-Path $output 'review-evidence.json'),$json+"`n")
    Write-Output "Review probes passed; evidence saved to $output"
}
finally {
    if ($server) {
        if (-not $server.HasExited) { $server.Kill($true); $server.WaitForExit() }
        $server.Dispose()
    }
    if ($provider) { if (-not $provider.HasExited) { $provider.Kill($true); $provider.WaitForExit() }; $provider.Dispose() }
    $client.Dispose(); $handler.Dispose()
    $resolvedScratch = [IO.Path]::GetFullPath($scratch)
    if (-not $resolvedScratch.StartsWith($tempParent.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase) -or
        [IO.Path]::GetFileName($resolvedScratch) -notmatch '^ocd-wi016-[0-9a-f]{32}$') { throw 'Unexpected cleanup path.' }
    for ($retry = 0; ; $retry++) {
        try { Remove-Item -LiteralPath $resolvedScratch -Recurse -Force; break }
        catch { if ($retry -ge 20) { throw }; Start-Sleep -Milliseconds 100 }
    }
}
