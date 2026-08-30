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

Write-Output 'Query guard tests passed.'
