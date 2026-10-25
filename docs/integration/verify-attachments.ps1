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
$scratch = Join-Path $tempParent ('ocd-wi017-' + [Guid]::NewGuid().ToString('N'))
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
    $fixtureStart.ArgumentList.Add((Join-Path $PSScriptRoot 'attachment-provider-fixture.mjs'))
    $fixtureStart.Environment['WI017_CAPTURE'] = Join-Path $output 'provider-input.json'
    $provider = [Diagnostics.Process]::Start($fixtureStart)
    $providerLine = $provider.StandardOutput.ReadLineAsync()
    if(-not $providerLine.Wait(10000) -or $providerLine.Result -notmatch '^http://127\.0\.0\.1:\d+$') { throw 'Review provider failed to start.' }
    $providerUrl = $providerLine.Result
    @{ model='wi017/fixture'; providers=@{wi017=@{name='WI017 fixture';package='@opencode/ai/providers/openai-compatible';settings=@{baseURL="$providerUrl/v1";apiKey='fixture-only';encoding='none'};models=@{fixture=@{capabilities=@{tools=$true};limit=@{context=32000;output=1000}}}}}} | ConvertTo-Json -Depth 20 | Set-Content (Join-Path $scratch 'config/opencode/opencode.json')
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
    $projectA = $start.WorkingDirectory
    $name = 'note 空 #%.txt'
    $file = Join-Path $projectA $name
    [IO.File]::WriteAllText($file, 'SERVER_FILE_MARKER_017')
    $query = 'location%5Bdirectory%5D=' + [Uri]::EscapeDataString($projectA)
    $created = Check 'create' "/api/session?$query" 200 'POST' @{}
    $session = $created.data.id
    $null = Check 'selectModel' "/api/session/$session/model" 204 'POST' @{model=@{id='fixture';providerID='wi017'}}
    $files = @(@{uri=([uri]$file).AbsoluteUri;name=$name},@{uri='data:text/plain;base64,TE9DQUxfQ09OVEVOVF9NQVJLRVJfMDE3';name='local.txt'})
    foreach ($extension in @('md','cs','json','xml','yaml','yml','js','ts','css','html','py','csv','log')) {
        $source=Join-Path $projectA "source.$extension"
        [IO.File]::WriteAllText($source,"SERVER_$($extension)_MARKER_017")
        $files+=@{uri=([uri]$source).AbsoluteUri;name="source.$extension"}
    }
    $null = Check 'admit' "/api/session/$session/prompt" 200 'POST' @{id='msg_wi017_files';text='Inspect attachment fixture';files=$files;resume=$false}
    $null = Check 'inbox' "/api/session/$session/inbox"
    $null = Check 'resume' "/api/session/$session/prompt" 200 'POST' @{id='msg_wi017_resume';text='Continue fixture'}
    $deadline=[DateTime]::UtcNow.AddSeconds(40)
    do {
        Start-Sleep -Milliseconds 200
        $history=Check 'history' "/api/session/$session/message?limit=50&order=asc"
        if(@($history.data | Where-Object type -eq 'idle').Count) {break}
        if([DateTime]::UtcNow -gt $deadline) {throw 'Fixture completion timed out.'}
    } while($true)
    $capture=[IO.File]::ReadAllText((Join-Path $output 'provider-input.json'))
    if(-not $capture.Contains('SERVER_FILE_MARKER_017') -or -not $capture.Contains('LOCAL_CONTENT_MARKER_017')) {throw 'Provider did not receive both contents.'}
    foreach ($extension in @('md','cs','json','xml','yaml','yml','js','ts','css','html','py','csv','log')) {
        if(-not $capture.Contains("SERVER_$($extension)_MARKER_017")) {throw "Provider did not receive $extension server content."}
    }
    $null = Check 'missingFile' "/api/session/$session/prompt" 400 'POST' @{text='Missing';files=@(@{uri=([uri](Join-Path $projectA 'missing.txt')).AbsoluteUri});resume=$false}
    $null = Check 'emptyInline' "/api/session/$session/prompt" 200 'POST' @{id='msg_wi017_empty';text='Empty text attachment';files=@(@{uri='data:text/plain;base64,';name='empty.txt'});resume=$false}
    $large = 'data:text/plain;base64,' + [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes(('A' * 262144)))
    $null = Check 'fourBoundedInlineFiles' "/api/session/$session/prompt" 200 'POST' @{id='msg_wi017_bounds';text='Client bound';files=@(1..4 | ForEach-Object { @{uri=$large;name="bounded$_.txt"} });resume=$false}
    $cases['fourBoundedInlineFiles'].body = (@{ admitted=$true;files=4;bytesPerFile=262144 } | ConvertTo-Json -Compress)
    $contract = Check 'runtimeContract' '/openapi.json'
    $paths = @('/api/session/{sessionID}/prompt','/api/session/{sessionID}/message')
    $slice = [ordered]@{openapi=$contract.openapi;info=$contract.info;paths=[ordered]@{};components=@{schemas=[ordered]@{}}}
    foreach($path in $paths) {$slice.paths[$path]=$contract.paths[$path]}
    do {
        $added=$false
        foreach($match in [regex]::Matches(($slice | ConvertTo-Json -Depth 100),'#/components/schemas/([^"\s]+)')) {
            $key=$match.Groups[1].Value
            if(-not $slice.components.schemas.Contains($key)) {$slice.components.schemas[$key]=$contract.components.schemas[$key];$added=$true}
        }
    } while($added)
    $snapshot = Join-Path $PSScriptRoot 'contracts/opencode-2.1.4-attachments.openapi.json'
    if(Test-Path $snapshot) {
        if(-not [Text.Json.Nodes.JsonNode]::DeepEquals([Text.Json.Nodes.JsonNode]::Parse([IO.File]::ReadAllText($snapshot)),[Text.Json.Nodes.JsonNode]::Parse(($slice|ConvertTo-Json -Depth 100)))) {throw 'Attachment contract changed.'}
    }
    $slice | ConvertTo-Json -Depth 100 | Set-Content (Join-Path $output 'attachments.openapi.json') -Encoding utf8NoBOM
    $cases.Remove('runtimeContract')
    foreach($name in @($cases.Keys)) {if($cases[$name].body) {$cases[$name].body=$cases[$name].body|ConvertFrom-Json -AsHashtable}}
    $evidence=@{version=$info.version;executableSha256=$expectedHash;cases=$cases;supportedServerExtensions=@('txt','md','cs','json','xml','yaml','yml','js','ts','css','html','py','csv','log');providerInput=@{serverTextObserved=$capture.Contains('SERVER_FILE_MARKER_017');localTextObserved=$capture.Contains('LOCAL_CONTENT_MARKER_017')};unobserved=@('Remote/POSIX providers','Server maximum payload size','Non-text media','Concurrent changes between validation and server admission')}
    $json=($evidence|ConvertTo-Json -Depth 100).Replace(($scratch|ConvertTo-Json -Compress).Trim('"'),'<isolated-root>').Replace($scratch,'<isolated-root>').Replace(([uri]$scratch).AbsoluteUri,'file:///<isolated-root>')
    if($json.Contains($password) -or $json.Contains($authorization)) {throw 'Credential in evidence.'}
    [IO.File]::WriteAllText((Join-Path $output 'attachments-evidence.json'),$json+"`n")
    Write-Output "Attachment probes passed; evidence saved to $output"
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
        [IO.Path]::GetFileName($resolvedScratch) -notmatch '^ocd-wi017-[0-9a-f]{32}$') { throw 'Unexpected cleanup path.' }
    for ($retry = 0; ; $retry++) {
        try { Remove-Item -LiteralPath $resolvedScratch -Recurse -Force; break }
        catch { if ($retry -ge 20) { throw }; Start-Sleep -Milliseconds 100 }
    }
}
