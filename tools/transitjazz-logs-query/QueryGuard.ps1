Set-StrictMode -Version Latest

$script:AllowedTables = @('ContainerAppConsoleLogs', 'ContainerAppSystemLogs')
$script:ForbiddenKql = '(?i)\b(join|union|find|externaldata|search|evaluate|invoke|workspace|app\(|adx\.)\b'
$script:SensitiveName = '(?i)(access[_-]?token|api[_-]?key|authorization|bearer|cookie|connection(string|[_-]?string)?|sharedaccesssignature|client[_-]?secret|accountkey|private key|password|sig)'

function Assert-BoundedLogQuery {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][ValidateSet('ContainerAppConsoleLogs', 'ContainerAppSystemLogs')][string]$Table,
        [Parameter(Mandatory)][datetime]$StartUtc,
        [Parameter(Mandatory)][datetime]$EndUtc,
        [Parameter(Mandatory)][string]$Kql,
        [Parameter(Mandatory)][ValidateRange(1, 100)][int]$Limit
    )

    if ($StartUtc.Kind -eq [DateTimeKind]::Unspecified -or $EndUtc.Kind -eq [DateTimeKind]::Unspecified) {
        throw 'StartUtc and EndUtc must include an explicit UTC offset.'
    }
    $start = $StartUtc.ToUniversalTime()
    $end = $EndUtc.ToUniversalTime()
    if ($end -le $start) { throw 'The UTC range must end after it starts.' }
    if (($end - $start).TotalDays -gt 31) { throw 'The UTC range may not exceed 31 days.' }
    if ([string]::IsNullOrWhiteSpace($Kql)) { throw 'KQL is required.' }
    if ($Kql -match $script:ForbiddenKql) { throw 'KQL contains an unsupported or cross-resource operator.' }
    if ($Kql -notmatch "(?i)(^|\|\s*)$([regex]::Escape($Table))(\s|\r|\n|$)") {
        throw 'KQL must use exactly the selected approved table.'
    }
    if ($Kql -notmatch '(?i)\bTimeGenerated\b\s+(between|>=|>)') {
        throw 'KQL must include a finite TimeGenerated UTC predicate.'
    }
    if ($Kql -notmatch '(?i)\bproject\b') { throw 'KQL must project useful columns.' }
    $take = [regex]::Match($Kql, '(?i)\|\s*take\s+(\d+)\s*;?\s*$')
    if (-not $take.Success -or [int]$take.Groups[1].Value -lt 1 -or [int]$take.Groups[1].Value -gt 100) {
        throw 'KQL must end with take 1..100.'
    }
    [pscustomobject]@{ Table = $Table; StartUtc = $start; EndUtc = $end; Kql = $Kql; Limit = $Limit }
}

function Get-ApprovedWorkspaceId {
    [CmdletBinding()]
    param([Parameter(Mandatory)][ValidateSet('dev', 'prod')][string]$Environment)
    $name = "TRANSITJAZZ_LOG_WORKSPACE_ID_$($Environment.ToUpperInvariant())"
    $id = [Environment]::GetEnvironmentVariable($name)
    if ([string]::IsNullOrWhiteSpace($id) -or $id -notmatch '^[0-9a-fA-F-]{36}$') {
        throw "Approved workspace alias '$Environment' is not configured."
    }
    return $id
}

function ConvertTo-SafeOutput {
    param($Value)
    if ($null -eq $Value) { return $null }
    if ($Value -is [string]) {
        if ($Value -match '(?i)(access[_-]?token|api[_-]?key|authorization|bearer|cookie|connection(string|[_-]?string)?|sharedaccesssignature|client[_-]?secret|accountkey|private key|password|sig=)') {
            return '[REDACTED]'
        }
        return $Value
    }
    if ($Value -is [System.Collections.IDictionary]) {
        $result = [ordered]@{}
        foreach ($key in $Value.Keys) {
            $keyText = [string]$key
            $result[$keyText] = if ($keyText -match $script:SensitiveName) { '[REDACTED]' } else { ConvertTo-SafeOutput $Value[$key] }
        }
        return $result
    }
    if ($Value -is [System.Collections.IEnumerable] -and $Value -isnot [string]) {
        return ,@($Value | ForEach-Object { ConvertTo-SafeOutput $_ })
    }
    if ($Value -is [System.Management.Automation.PSObject]) {
        $result = [ordered]@{}
        foreach ($property in $Value.PSObject.Properties) {
            $result[$property.Name] = if ($property.Name -match $script:SensitiveName) { '[REDACTED]' } else { ConvertTo-SafeOutput $property.Value }
        }
        return $result
    }
    return $Value
}
