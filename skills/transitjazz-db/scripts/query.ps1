<#
.SYNOPSIS
Run reviewed TransitJazz SELECT queries in one read-only database snapshot.
.DESCRIPTION
Uses psql on PATH. Connection secrets come from an existing environment variable,
an explicitly selected appsettings connection, or existing libpq settings.
Named :parameters are quoted as psql literals; SQL null is supported.
#>
[CmdletBinding(DefaultParameterSetName = 'Inline')]
param(
    [Parameter(Mandatory, ParameterSetName = 'Inline')]
    [string]$Sql,

    [Parameter(Mandatory, ParameterSetName = 'Files')]
    [string[]]$SqlFile,

    [hashtable]$Parameters = @{},
    [string]$ConnectionEnvironmentVariable = 'ConnectionStrings__TransitJazzDB',
    [string]$AppSettingsPath,
    [string]$ConnectionName = 'TransitJazzDB',
    [ValidateSet('table', 'csv')]
    [string]$Format = 'table',
    [ValidateRange(1, 300)]
    [int]$StatementTimeoutSeconds = 30
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$psql = Get-Command psql -CommandType Application -ErrorAction Stop
$queries = if ($PSCmdlet.ParameterSetName -eq 'Files') {
    @($SqlFile | ForEach-Object { Get-Content -LiteralPath $_ -Raw -Encoding UTF8 })
} else { @($Sql) }

$arguments = @('-X', '-w', '-q', '-v', 'ON_ERROR_STOP=1', '-P', 'pager=off', '-P', 'null=NULL')
if ($Format -eq 'csv') { $arguments += '--csv' }
$parameterSettings = @{}
$parameterSetup = @()
foreach ($name in $Parameters.Keys) {
    if ($name -notmatch '^[a-z][a-z0-9_]*$') { throw 'Parameter names must be lowercase SQL identifiers.' }
    if ($null -ne $Parameters[$name] -and [string]$Parameters[$name] -ne '') {
        # Avoid Windows PowerShell's native argument conversion of embedded quotes.
        $variable = "TRANSITJAZZ_DB_PARAMETER_$name"
        $parameterSettings[$variable] = [string]$Parameters[$name]
        $parameterSetup += "\getenv $name $variable"
    }
}

# Skip strings, quoted identifiers, dollar-quoted strings, and ordinary comments.
# This adapts the repository's named query contracts; it is not an SQL authorization parser.
$tokenPattern = @'
--[^\r\n]*|/\*[\s\S]*?\*/|'(?:''|[^'])*'|"(?:""|[^"])*"|\$(?<tag>[A-Za-z_][A-Za-z_0-9]*|)\$[\s\S]*?\$\k<tag>\$|(?<!:):(?<parameter>[a-z][a-z0-9_]*)
'@
$prepared = @($queries | ForEach-Object {
    if ([string]::IsNullOrWhiteSpace($_)) { throw 'SQL must not be empty.' }
    if ($_ -match '(?m)^\s*\\') { throw 'Supply SQL rather than psql backslash commands.' }
    [regex]::Replace($_, $tokenPattern, [System.Text.RegularExpressions.MatchEvaluator]{
        param($match)
        $name = $match.Groups['parameter'].Value
        if (-not $name) { return $match.Value }
        if (-not $Parameters.ContainsKey($name)) { throw "Missing SQL parameter: $name." }
        if ($null -eq $Parameters[$name]) { return 'NULL' }
        if ([string]$Parameters[$name] -eq '') { return "''" }
        return ":'$name'"
    })
})

$connection = $null
if ($AppSettingsPath) {
    try {
        $settings = Get-Content -LiteralPath $AppSettingsPath -Raw -Encoding UTF8 | ConvertFrom-Json
        $connection = $settings.ConnectionStrings.$ConnectionName
    } catch { throw 'Could not read the selected appsettings connection; inspect its path and key without displaying credentials.' }
    if ([string]::IsNullOrWhiteSpace($connection)) { throw 'The selected appsettings connection is empty or missing.' }
} else {
    $connection = [Environment]::GetEnvironmentVariable($ConnectionEnvironmentVariable, 'Process')
}

$overrides = @{
    PGOPTIONS = "-c default_transaction_read_only=on -c timezone=UTC -c statement_timeout=$($StatementTimeoutSeconds * 1000)"
    PGCONNECT_TIMEOUT = '10'
    PGCLIENTENCODING = 'UTF8'
}
foreach ($variable in $parameterSettings.Keys) { $overrides[$variable] = $parameterSettings[$variable] }
if (-not [string]::IsNullOrWhiteSpace($connection)) {
    try {
        $builder = [System.Data.Common.DbConnectionStringBuilder]::new()
        $builder.set_ConnectionString($connection)
        $fields = @{}
        foreach ($key in $builder.get_Keys()) { $fields[($key -replace '\s', '').ToLowerInvariant()] = [string]$builder.get_Item($key) }
    } catch { throw 'Invalid Npgsql connection string; its value has been suppressed.' }

    $mapping = @{
        PGHOST = @('host', 'server')
        PGPORT = @('port')
        PGDATABASE = @('database', 'initialcatalog')
        PGUSER = @('username', 'userid', 'user', 'uid')
        PGPASSWORD = @('password', 'pwd')
        PGSSLMODE = @('sslmode')
        PGSSLROOTCERT = @('rootcertificate')
        PGSSLCERT = @('sslcertificate')
        PGSSLKEY = @('sslkey')
    }
    foreach ($variable in $mapping.Keys) {
        $overrides[$variable] = $null
        foreach ($alias in $mapping[$variable]) {
            if ($fields.ContainsKey($alias)) { $overrides[$variable] = $fields[$alias]; break }
        }
    }
    foreach ($variable in @('PGHOST', 'PGDATABASE', 'PGUSER')) {
        if ([string]::IsNullOrWhiteSpace($overrides[$variable])) { throw "The selected connection must specify $variable." }
    }
    if (-not $overrides.PGPORT) { $overrides.PGPORT = '5432' }
    if ($overrides.PGSSLMODE) {
        $overrides.PGSSLMODE = ($overrides.PGSSLMODE -replace '[\s-]', '').ToLowerInvariant()
        if ($overrides.PGSSLMODE -eq 'verifyfull') { $overrides.PGSSLMODE = 'verify-full' }
        if ($overrides.PGSSLMODE -eq 'verifyca') { $overrides.PGSSLMODE = 'verify-ca' }
        if ($overrides.PGSSLMODE -notin @('disable', 'allow', 'prefer', 'require', 'verify-ca', 'verify-full')) {
            throw 'Unsupported SSL Mode; use the approved PostgreSQL connection settings.'
        }
    }
    # Prevent an unrelated libpq service/host-address from changing the selected target.
    $overrides.PGSERVICE = $null
    $overrides.PGHOSTADDR = $null
} elseif (-not $env:PGSERVICE -and (-not $env:PGHOST -or -not $env:PGDATABASE -or -not $env:PGUSER)) {
    throw 'Configure ConnectionStrings__TransitJazzDB, select an appsettings connection, or configure PGSERVICE / PGHOST, PGDATABASE, and PGUSER.'
}

$original = @{}
try {
    foreach ($variable in $overrides.Keys) {
        $original[$variable] = [Environment]::GetEnvironmentVariable($variable, 'Process')
        [Environment]::SetEnvironmentVariable($variable, $overrides[$variable], 'Process')
    }
    $OutputEncoding = [System.Text.UTF8Encoding]::new($false)
    $body = ($parameterSetup -join "`n") + "`nBEGIN TRANSACTION ISOLATION LEVEL REPEATABLE READ READ ONLY;`n" +
        (($prepared | ForEach-Object { $_ + "`n;" }) -join "`n") + "`nROLLBACK;"
    $body | & $psql.Source @arguments
    if ($LASTEXITCODE -ne 0) { throw "psql failed with exit code $LASTEXITCODE; no report should be presented as complete." }
} finally {
    foreach ($variable in $original.Keys) { [Environment]::SetEnvironmentVariable($variable, $original[$variable], 'Process') }
    $connection = $null
    $builder = $null
    $fields = $null
    $settings = $null
}
