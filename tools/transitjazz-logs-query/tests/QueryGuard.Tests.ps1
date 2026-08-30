$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '..\QueryGuard.ps1')

function Assert-Rejected([scriptblock]$Action) {
    try { & $Action; throw 'Expected guard rejection.' } catch { if ($_.Exception.Message -eq 'Expected guard rejection.') { throw } }
}

$start = [datetime]::Parse('2026-08-30T12:00:00Z').ToUniversalTime()
$end = [datetime]::Parse('2026-08-30T12:15:00Z').ToUniversalTime()
$good = "ContainerAppConsoleLogs | where TimeGenerated between (datetime(2026-08-30T12:00:00Z) .. datetime(2026-08-30T12:15:00Z)) | project TimeGenerated, Log | take 10"

Assert-BoundedLogQuery -Table ContainerAppConsoleLogs -StartUtc $start -EndUtc $end -Kql $good -Limit 10 | Out-Null
Assert-Rejected { Assert-BoundedLogQuery -Table ContainerAppConsoleLogs -StartUtc $start -EndUtc $end -Kql ($good -replace 'take 10$', 'take 101') -Limit 10 }
Assert-Rejected { Assert-BoundedLogQuery -Table ContainerAppConsoleLogs -StartUtc $start -EndUtc $end -Kql ($good -replace 'project TimeGenerated, Log', 'join Other on X') -Limit 10 }
Assert-Rejected { Assert-BoundedLogQuery -Table ContainerAppConsoleLogs -StartUtc $start -EndUtc $end -Kql 'ContainerAppConsoleLogs | project Log | take 10' -Limit 10 }
Assert-Rejected { Assert-BoundedLogQuery -Table ContainerAppConsoleLogs -StartUtc $start -EndUtc $end -Kql $good -Limit 101 }
Assert-Rejected { Assert-BoundedLogQuery -Table ContainerAppConsoleLogs -StartUtc $start -EndUtc $end.AddDays(32) -Kql $good -Limit 10 }
Assert-Rejected { Assert-BoundedLogQuery -Table ContainerAppConsoleLogs -StartUtc $start -EndUtc $end -Kql ($good -replace 'ContainerAppConsoleLogs', 'ContainerAppSystemLogs') -Limit 10 }
Assert-Rejected { Assert-BoundedLogQuery -Table ContainerAppConsoleLogs -StartUtc $start -EndUtc $end -Kql ($good -replace 'project TimeGenerated, Log', 'project TimeGenerated, Log | search secret') -Limit 10 }
Assert-Rejected { Get-ApprovedWorkspaceId -Environment staging }

$helperCommand = Get-Command (Join-Path $PSScriptRoot '..\transitjazz-logs-query.ps1')
foreach ($forbiddenParameter in @('Url', 'Method', 'Header')) {
    if ($helperCommand.Parameters.ContainsKey($forbiddenParameter)) {
        throw "Helper exposes forbidden parameter '$forbiddenParameter'."
    }
}

$safe = ConvertTo-SafeOutput ([pscustomobject]@{
    access_token = 'token-value'
    nested = [pscustomobject]@{ connectionString = 'DefaultEndpointsProtocol=https;AccountKey=secret' }
    message = 'bounded safe marker'
})
if ($safe.access_token -ne '[REDACTED]' -or $safe.nested.connectionString -ne '[REDACTED]' -or $safe.message -ne 'bounded safe marker') {
    throw 'Nested query output redaction failed.'
}

$safeRows = ConvertTo-SafeOutput ([pscustomobject]@{
    tables = @([pscustomobject]@{ rows = @(@('bounded row marker')) })
})
if (@($safeRows['tables']).Count -ne 1 -or @($safeRows['tables'][0]['rows']).Count -ne 1 -or $safeRows['tables'][0]['rows'][0] -ne 'bounded row marker') {
    throw 'Query rows were not preserved as arrays.'
}

$oldWorkspaceId = $env:TRANSITJAZZ_LOG_WORKSPACE_ID_PROD
$oldAzFunction = Get-Item Function:\az -ErrorAction SilentlyContinue
$oldAzDefinition = if ($null -ne $oldAzFunction) { $oldAzFunction.Definition } else { $null }
$global:capturedBodyFilePath = $null
try {
    $env:TRANSITJAZZ_LOG_WORKSPACE_ID_PROD = '00000000-0000-0000-0000-000000000001'
    $endUtc = [DateTime]::Parse('2026-08-30T12:15:00Z').ToUniversalTime()
    $startUtc = $endUtc.AddMinutes(-15)
    $helperKql = 'ContainerAppConsoleLogs | where TimeGenerated between (datetime(2026-08-30T12:00:00Z) .. datetime(2026-08-30T12:15:00Z)) | project TimeGenerated | take 1'

    function global:az {
        param([Parameter(ValueFromRemainingArguments = $true)][string[]]$Arguments)
        $bodyIndex = [Array]::IndexOf($Arguments, '--body')
        if ($bodyIndex -lt 0 -or $bodyIndex + 1 -ge $Arguments.Count) { throw 'Helper did not receive --body.' }
        $bodyArgument = $Arguments[$bodyIndex + 1]
        if (-not $bodyArgument.StartsWith('@')) { throw 'Helper did not use an @file body.' }
        $global:capturedBodyFilePath = $bodyArgument.Substring(1)
        if (-not (Test-Path -LiteralPath $global:capturedBodyFilePath)) { throw 'Helper body file was not created.' }
        $request = Get-Content -Raw -LiteralPath $global:capturedBodyFilePath | ConvertFrom-Json
        if ([string]::IsNullOrWhiteSpace($request.query)) { throw 'Helper sent an empty query.' }
        $global:capturedQuery = $request.query
        $global:LASTEXITCODE = 0
        Write-Output '{"tables":[]}'
    }

    & (Join-Path $PSScriptRoot '..\transitjazz-logs-query.ps1') -Environment prod -Table ContainerAppConsoleLogs -StartUtc $startUtc -EndUtc $endUtc -Kql $helperKql -Limit 1 | Out-Null
    if ([string]::IsNullOrWhiteSpace($global:capturedQuery)) { throw 'Helper regression did not capture a query.' }
    if ($null -eq $global:capturedBodyFilePath -or (Test-Path -LiteralPath $global:capturedBodyFilePath)) { throw 'Helper temporary body file was not cleaned up.' }
}
finally {
    if ($null -eq $oldWorkspaceId) { Remove-Item Env:TRANSITJAZZ_LOG_WORKSPACE_ID_PROD -ErrorAction SilentlyContinue } else { $env:TRANSITJAZZ_LOG_WORKSPACE_ID_PROD = $oldWorkspaceId }
    Remove-Variable capturedQuery -Scope Global -ErrorAction SilentlyContinue
    Remove-Variable capturedBodyFilePath -Scope Global -ErrorAction SilentlyContinue
    if ($null -eq $oldAzDefinition) { Remove-Item Function:\az -ErrorAction SilentlyContinue } else { Set-Item Function:\az -Value $oldAzDefinition }
}

Write-Output 'Query guard tests passed.'
