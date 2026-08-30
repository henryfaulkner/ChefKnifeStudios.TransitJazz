[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateSet('dev', 'prod')][string]$Environment,
    [Parameter(Mandatory)][ValidateSet('ContainerAppConsoleLogs', 'ContainerAppSystemLogs')][string]$Table,
    [Parameter(Mandatory)][datetime]$StartUtc,
    [Parameter(Mandatory)][datetime]$EndUtc,
    [Parameter(Mandatory)][string]$Kql,
    [ValidateRange(1, 100)][int]$Limit = 50
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'QueryGuard.ps1')

$guarded = Assert-BoundedLogQuery -Table $Table -StartUtc $StartUtc -EndUtc $EndUtc -Kql $Kql -Limit $Limit
$workspaceId = Get-ApprovedWorkspaceId -Environment $Environment
$timespan = "$($guarded.StartUtc.ToString('o'))/$($guarded.EndUtc.ToString('o'))"
$escapedWorkspace = [Uri]::EscapeDataString($workspaceId)
$escapedTimespan = [Uri]::EscapeDataString($timespan)
$endpoint = "https://api.loganalytics.io/v1/workspaces/$escapedWorkspace/search?timespan=$escapedTimespan"
$body = @{ query = $guarded.Kql } | ConvertTo-Json -Compress

# PowerShell invokes az.cmd on Windows. Passing JSON inline can strip the JSON
# quotes before Azure CLI parses it, resulting in a "query cannot be empty"
# response. The @file convention preserves the body exactly.
$bodyFilePath = [IO.Path]::GetTempFileName()
try {
    [IO.File]::WriteAllText($bodyFilePath, $body, [Text.UTF8Encoding]::new($false))
    $bodyFileArgument = "@$bodyFilePath"

    # Fixed read-only operation. No caller-controlled URL, method, header, token, or command is passed.
    $raw = & az rest --method post --url $endpoint --resource 'https://api.loganalytics.io/' --headers 'Content-Type=application/json' --body $bodyFileArgument --output json
    if ($LASTEXITCODE -ne 0) { throw 'Azure Logs query failed; run doctor and inspect the first failing layer.' }
    $safe = ConvertTo-SafeOutput (($raw -join [Environment]::NewLine) | ConvertFrom-Json)
}
finally {
    if (Test-Path -LiteralPath $bodyFilePath) {
        Remove-Item -LiteralPath $bodyFilePath -Force -ErrorAction SilentlyContinue
    }
}

[pscustomobject]@{
    Workspace = $workspaceId
    Table = $guarded.Table
    StartUtc = $guarded.StartUtc.ToString('o')
    EndUtc = $guarded.EndUtc.ToString('o')
    Kql = $guarded.Kql
    Limit = $guarded.Limit
    Output = 'json'
    Rows = $safe
} | ConvertTo-Json -Depth 20
