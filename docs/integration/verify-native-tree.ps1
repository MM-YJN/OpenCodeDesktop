# Requires WinApp CLI 0.7+ and a running packaged app with a populated project tree.
# Uses read-only UIA inspection. Evidence can contain workspace names; keep it outside Git.
[CmdletBinding()]
param(
    [Parameter(Mandatory)][int] $AppProcessId,
    [Parameter(Mandatory)][string] $OutputDirectory,
    [ValidateRange(1, 20)][int] $Iterations = 3
)

$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
for ($iteration = 1; $iteration -le $Iterations; $iteration++) {
    $snapshot = & winapp ui inspect -a $AppProcessId --interactive --json
    if ($LASTEXITCODE -ne 0) { throw "UIA inspection failed on iteration $iteration." }
    $json = $snapshot -join "`n"
    $json | Set-Content -LiteralPath (Join-Path $OutputDirectory "tree-$iteration.json") -Encoding utf8
    $state = $json | ConvertFrom-Json
    $rows = @($state.windows.elements | Where-Object type -eq 'TreeItem')
    if ($rows.Count -eq 0) { throw 'No realized tree rows: connect and expand a project before running this check.' }
    $unnamed = @($rows | Where-Object {
        $_.name -notmatch '^(Project |Session |New session draft in |Load more sessions for )'
    })
    if ($unnamed.Count -ne 0) { throw "$($unnamed.Count) tree rows lack meaningful accessible names on iteration $iteration." }
    Write-Output "Iteration ${iteration}: $($rows.Count) realized tree rows have meaningful accessible names."
}
