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
$scratch = Join-Path $tempParent ('ocd-wi001-' + [Guid]::NewGuid().ToString('N'))
if ($output.StartsWith($scratch, [StringComparison]::OrdinalIgnoreCase)) { throw 'Output must be outside scratch storage.' }
New-Item -ItemType Directory $scratch | Out-Null
$server = $null
$listener = $null
$heldSocket = $null
$handler = [Net.Http.HttpClientHandler]::new()
$handler.UseProxy = $false
$handler.AllowAutoRedirect = $false
$client = [Net.Http.HttpClient]::new($handler)
$client.Timeout = [Threading.Timeout]::InfiniteTimeSpan
$password = [Guid]::NewGuid().ToString('N')
$authorization = 'Basic ' + [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes("opencode:$password"))

function Invoke-Probe([string] $Url, [string] $Authorization, [int] $TimeoutMs = 2000) {
    $request = [Net.Http.HttpRequestMessage]::new([Net.Http.HttpMethod]::Get, $Url)
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
        if ($key.StartsWith('OPENCODE_')) { $start.Environment.Remove($key) | Out-Null }
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
    $cases = [ordered]@{}
    foreach ($case in @(
        @{ name = 'missingCredentials'; auth = '' },
        @{ name = 'wrongPassword'; auth = 'Basic b3BlbmNvZGU6d3Jvbmc=' },
        @{ name = 'wrongUsername'; auth = 'Basic ' + [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes("wrong:$password")) },
        @{ name = 'bearer'; auth = "Bearer $password" }
    )) {
        $result = Invoke-Probe "$baseUrl/api/info" $case.auth
        if ($result.status -ne 401 -or ($result.body | ConvertFrom-Json)._tag -ne 'UnauthorizedError') {
            throw "Unexpected authentication result: $($case.name)"
        }
        $cases[$case.name] = $result
    }
    $info.paths.tmp = '<isolated-root>\tmp\opencode'
    $info.pid = 0 # Sanitized process identity; equality was checked above.
    $info.urls = @('http://127.0.0.1:<port>')
    $ready.body = $info | ConvertTo-Json -Compress -Depth 10
    $cases['validCredentials'] = $ready
    $localhost = Invoke-Probe ($baseUrl.Replace('127.0.0.1', 'localhost') + '/api/info') $authorization
    $cases['localhost'] = @{ status = $localhost.status; failure = $localhost.failure }
    $contract = Invoke-Probe "$baseUrl/openapi.json" $authorization
    if ($contract.status -ne 200) { throw 'Runtime OpenAPI retrieval failed.' }
    [IO.File]::WriteAllText((Join-Path $output 'runtime-openapi.json'), $contract.body)
    $api = $contract.body | ConvertFrom-Json -AsHashtable
    $slice = [ordered]@{
        openapi = $api.openapi; info = $api.info
        paths = @{ '/api/info' = $api.paths['/api/info'] }
        components = @{ schemas = @{
            ServerInfo = $api.components.schemas.ServerInfo
            InvalidRequestErrorEncoded = $api.components.schemas.InvalidRequestErrorEncoded
            UnauthorizedErrorEncoded = $api.components.schemas.UnauthorizedErrorEncoded
        } }
    }
    $expected = [Text.Json.Nodes.JsonNode]::Parse([IO.File]::ReadAllText((Join-Path $PSScriptRoot 'contracts/opencode-2.1.4-connection.openapi.json')))
    $actual = [Text.Json.Nodes.JsonNode]::Parse(($slice | ConvertTo-Json -Depth 40))
    if (-not [Text.Json.Nodes.JsonNode]::DeepEquals($expected, $actual)) { throw 'Runtime connection contract differs from snapshot.' }

    # Hold a loopback TCP connection without replying: deterministic timeout fixture, not an OpenCode response.
    $listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
    $listener.Start()
    $fixturePort = $listener.LocalEndpoint.Port
    $accept = $listener.AcceptTcpClientAsync()
    $timeout = Invoke-Probe "http://127.0.0.1:$fixturePort/api/info" '' 300
    if (-not $accept.Wait(2000)) { throw 'Timeout fixture did not accept the connection.' }
    $heldSocket = $accept.GetAwaiter().GetResult()
    if ($timeout.failure -ne 'timeout') { throw 'Timeout fixture did not time out.' }
    $cases['controlledTimeout'] = $timeout
    $heldSocket.Dispose(); $heldSocket = $null
    $listener.Stop(); $listener = $null

    $server.Kill($true)
    $server.WaitForExit()
    try {
        $stopped = Invoke-Probe "$baseUrl/api/info" $authorization 5000
        if ($stopped.failure -ne 'timeout') { throw 'Stopped server unexpectedly returned an HTTP response.' }
        $cases['stoppedServer'] = $stopped
    }
    catch {
        $exception = $_.Exception
        while ($exception.InnerException) { $exception = $exception.InnerException }
        if ($exception -isnot [Net.Sockets.SocketException] -or $exception.SocketErrorCode -ne 'ConnectionRefused') { throw }
        $cases['stoppedServer'] = @{ failure = 'connectionRefused'; socketError = [string]$exception.SocketErrorCode }
    }
    $evidence = [ordered]@{
        verifiedAtUtc = [DateTime]::UtcNow.ToString('o'); serverVersion = '2.1.4'; executableSha256 = $expectedHash
        environment = @{ os = [Runtime.InteropServices.RuntimeInformation]::OSDescription; architecture = [string][Runtime.InteropServices.RuntimeInformation]::OSArchitecture; powershell = [string]$PSVersionTable.PSVersion }
        request = @{ method = 'GET'; path = '/api/info'; accept = 'application/json'; authorization = 'Basic <redacted>' }
        startupStatuses = $startupStatuses.ToArray(); cases = $cases
        runtimeConnectionContractMatchesSnapshot = $true
        sanitization = 'Credentials omitted; port, PID, and temporary path replaced. Runtime PID was checked against the launched process.'
    }
    $evidence | ConvertTo-Json -Depth 20 | Set-Content (Join-Path $output 'connection-evidence.json') -Encoding utf8NoBOM
    Write-Output "Verified OpenCode 2.1.4; evidence saved to $output"
}
finally {
    if ($heldSocket) { $heldSocket.Dispose() }
    if ($listener) { $listener.Stop() }
    if ($server) {
        if (-not $server.HasExited) { $server.Kill($true); $server.WaitForExit() }
        $server.Dispose()
    }
    $client.Dispose(); $handler.Dispose()
    $resolvedScratch = [IO.Path]::GetFullPath($scratch)
    if (-not $resolvedScratch.StartsWith($tempParent, [StringComparison]::OrdinalIgnoreCase) -or
        [IO.Path]::GetFileName($resolvedScratch) -notmatch '^ocd-wi001-[0-9a-f]{32}$') {
        throw 'Refusing cleanup outside the owned temporary directory.'
    }
    Remove-Item -LiteralPath $resolvedScratch -Recurse -Force
}
