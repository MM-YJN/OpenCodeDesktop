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
$scratch = Join-Path $tempParent ('ocd-wi015-' + [Guid]::NewGuid().ToString('N'))
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
    New-Item -ItemType Directory (Join-Path $projectA 'nested 空') -Force | Out-Null
    [IO.File]::WriteAllText((Join-Path $projectA 'nested 空/hello #%.txt'), "Hello 世界`nsecond line", [Text.UTF8Encoding]::new($false))
    [IO.File]::WriteAllText((Join-Path $projectA 'empty.txt'), '')
    [IO.File]::WriteAllBytes((Join-Path $projectA 'binary.bin'), [byte[]]@(0,1,2,255,0))
    [IO.File]::WriteAllText((Join-Path $projectA 'large.txt'), ('x' * 300000))
    1..550 | ForEach-Object { [IO.File]::WriteAllText((Join-Path $projectA "entry$_.txt"), '') }
    $query = 'location%5Bdirectory%5D=' + [Uri]::EscapeDataString($projectA)
    $null = Check 'resolve' "/api/location?$query"
    $null = Check 'listRoot' "/api/fs/list?$query"
    $null = Check 'listNested' ("/api/fs/list?$query&path=" + [Uri]::EscapeDataString('nested 空'))
    $null = Check 'findUnicode' ("/api/fs/find?$query&type=file&limit=100&query=" + [Uri]::EscapeDataString('hello'))
    $null = Check 'findBounded' "/api/fs/find?$query&type=file&limit=100&query=entry"
    $null = Check 'missingList' "/api/fs/list?$query&path=missing" 404
    $created = Check 'createReferenceFixture' '/api/session' 200 'POST' @{ location = @{ directory = $projectA }; title = 'WI015 reference fixture' }
    $sourceUri = ([Uri](Join-Path $projectA 'nested 空/hello #%.txt')).AbsoluteUri
    $referenceMessage = @{ id = 'msg_wi015_reference'; type = 'user'; time = @{ created = 1 }; text = 'Structured fixture file reference'; files = @(@{ data = 'SGVsbG8='; mime = 'text/plain'; source = @{ type = 'uri'; uri = $sourceUri }; name = 'hello #%.txt' }) }
    $fixtureInfo = $created.data.Clone()
    $fixtureInfo.id = 'ses_wi015_reference'
    $null = Check 'importReferenceFixture' '/api/experimental/session/import' 200 'POST' @{ info = $fixtureInfo; messages = @($referenceMessage); location = @{ directory = $projectA } }
    $referenceHistory = Check 'readReferenceHistory' ("/api/session/$($fixtureInfo.id)/message?limit=10&order=asc")
    if ($referenceHistory.data[0].files[0].source.uri -ne $sourceUri) { throw 'Structured file identity was not retained.' }
    function ReadCheck([string] $Name, [string] $Path, [string] $Range, [int] $Status) {
        $request = [Net.Http.HttpRequestMessage]::new([Net.Http.HttpMethod]::Get, "$baseUrl/api/fs/read/$([Uri]::EscapeDataString($Path))?$query")
        $request.Headers.Add('Authorization', $authorization)
        if ($Range) { $request.Headers.Add('Range', $Range) }
        $response = $client.SendAsync($request).GetAwaiter().GetResult()
        try {
            $bytes = $response.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult()
            if ([int]$response.StatusCode -ne $Status) { throw "$Name expected $Status got $([int]$response.StatusCode)" }
            $cases[$Name] = @{ status = [int]$response.StatusCode; contentType = [string]$response.Content.Headers.ContentType; contentRange = [string]$response.Content.Headers.ContentRange; length = $bytes.Length; utf8 = if ($bytes.Length -lt 100) { [Text.Encoding]::UTF8.GetString($bytes) } else { '<fixture bytes omitted>' } }
        } finally { $response.Dispose(); $request.Dispose() }
    }
    ReadCheck 'unicodeRead' 'nested 空\hello #%.txt' '' 200
    ReadCheck 'rangeRead' 'nested 空/hello #%.txt' 'bytes=0-7' 206
    ReadCheck 'emptyRead' 'empty.txt' 'bytes=0-262143' 416
    ReadCheck 'binaryRead' 'binary.bin' 'bytes=0-262143' 206
    ReadCheck 'largeRead' 'large.txt' 'bytes=0-262143' 206
    ReadCheck 'missingRead' 'missing.txt' '' 404
    ReadCheck 'directoryRead' 'nested 空' '' 404
    ReadCheck 'unsatisfiable' 'large.txt' 'bytes=400000-' 416
    $contract = Check 'runtimeContract' '/openapi.json'
    $paths = @('/api/location','/api/fs/list','/api/fs/find','/api/fs/read/*')
    $slice = [ordered]@{ openapi = $contract.openapi; info = $contract.info; paths = [ordered]@{}; components = @{ schemas = [ordered]@{} } }
    foreach ($path in $paths) { $slice.paths[$path] = $contract.paths[$path] }
    do {
        $added = $false
        foreach ($match in [regex]::Matches(($slice | ConvertTo-Json -Depth 100), '#/components/schemas/([^"\s]+)')) {
            $key = $match.Groups[1].Value
            if (-not $slice.components.schemas.Contains($key)) { $slice.components.schemas[$key] = $contract.components.schemas[$key]; $added = $true }
        }
    } while ($added)
    $snapshot = Join-Path $PSScriptRoot 'contracts/opencode-2.1.4-files.openapi.json'
    if (Test-Path $snapshot) {
        if (-not [Text.Json.Nodes.JsonNode]::DeepEquals([Text.Json.Nodes.JsonNode]::Parse([IO.File]::ReadAllText($snapshot)), [Text.Json.Nodes.JsonNode]::Parse(($slice | ConvertTo-Json -Depth 100)))) { throw 'Filesystem contract changed.' }
    }
    $slice | ConvertTo-Json -Depth 100 | Set-Content (Join-Path $output 'files.openapi.json') -Encoding utf8NoBOM
    $cases.Remove('runtimeContract')
    foreach ($name in @($cases.Keys)) {
        if ($cases[$name].body) { $cases[$name].body = $cases[$name].body | ConvertFrom-Json -AsHashtable }
    }
    $cases['listRoot'].body.data = @{ count = $cases['listRoot'].body.data.Count; sample = @($cases['listRoot'].body.data | Select-Object -First 4) }
    $cases['findBounded'].body.data = @{ count = $cases['findBounded'].body.data.Count; sample = @($cases['findBounded'].body.data | Select-Object -First 4) }
    $evidence = @{ version = $info.version; executableSha256 = $expectedHash; cases = $cases; unobserved = @('OS access denied: no ACL change made','Remote host and POSIX runtime','Symlink escape behavior') }
    $json = ($evidence | ConvertTo-Json -Depth 100).Replace(($scratch | ConvertTo-Json -Compress).Trim('"'), '<isolated-root>').Replace($scratch, '<isolated-root>')
    $json = $json.Replace(([Uri]$scratch).AbsoluteUri, 'file:///<isolated-root>')
    if ($json.Contains($password) -or $json.Contains($authorization)) { throw 'Credential in evidence.' }
    [IO.File]::WriteAllText((Join-Path $output 'files-evidence.json'),$json+"`n")
    Write-Output "Filesystem probes passed; evidence saved to $output"
}
finally {
    if ($server) {
        if (-not $server.HasExited) { $server.Kill($true); $server.WaitForExit() }
        $server.Dispose()
    }
    $client.Dispose(); $handler.Dispose()
    $resolvedScratch = [IO.Path]::GetFullPath($scratch)
    if (-not $resolvedScratch.StartsWith($tempParent.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase) -or
        [IO.Path]::GetFileName($resolvedScratch) -notmatch '^ocd-wi015-[0-9a-f]{32}$') { throw 'Unexpected cleanup path.' }
    for ($retry = 0; ; $retry++) {
        try { Remove-Item -LiteralPath $resolvedScratch -Recurse -Force; break }
        catch { if ($retry -ge 20) { throw }; Start-Sleep -Milliseconds 100 }
    }
}
