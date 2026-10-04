# Connection and access

## Configuration in the codebase

Paths are relative to the repository root.

| Source | Established behavior |
| --- | --- |
| `src/Server/ChefKnifeStudios.TransitJazz.Server.Data/ServiceCollectionExtensions.cs` | Registers an EF context factory with Npgsql. Requires the connection named `TransitJazzDB`. |
| `src/Server/ChefKnifeStudios.TransitJazz.Server.Data/AppDbContextFactory.cs` | EF design-time configuration reads the working directory's development/release appsettings file first, then `ConnectionStrings__TransitJazzDB`. This precedence is specific to the factory. |
| `src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI/Program.cs` | Reads `ConnectionStrings:TransitJazzDB` through normal ASP.NET configuration. Enabled historical or category collection requires database registration. |
| `bicep/modules/containerApp.bicep` | Injects `ConnectionStrings__TransitJazzDB` from the Container App secret alias `transitjazz-db`, backed by the supplied `transitJazzDbSecretUri` and managed identity. |
| `bicep/main.dev.bicepparam`, `bicep/main.prod.bicepparam` | Committed database secret URI defaults are empty. A deployment may override them. |
| `src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI/appsettings.json` | `ConnectionStrings.TransitJazzDB` is empty in the committed default. |
| `src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI/appsettings.Development.json` | A legacy entry named `PokerAttackDB` specifies database `transitjazz` on port 5432. The current application does not read that key. Select it explicitly only when this is the intended development target. |
| `docker-compose.yml` | Defines the WebAPI service; does not provision a PostgreSQL service. Do not assume a local database container exists. |

Use a caller-selected target or existing current connection. An unconfigured local app does not establish the production endpoint. Do not infer the live database from old cheat sheets, a shared Key Vault's name, or unrelated telemetry settings.

Never print or copy connection strings, passwords, secret payloads, or environment dumps into tool output, SQL, reports, or checked-in files. Read only the required configuration value in-process. The legacy `Data/docs/PostgresCheatSheet.txt` contains credential-bearing commands and is not an approved credential source to reproduce.

## Query helper

Requires PowerShell 5.1+ and `psql` on PATH with `\getenv` support (check `psql --help=commands`). Run from the repository root. The helper does not install software, change app configuration, or fetch secrets automatically.

```powershell
# Existing Npgsql connection in the process environment, without echoing it.
& ./skills/transitjazz-db/scripts/query.ps1 -Sql 'SELECT current_database(), current_user;'

# Explicitly selected current appsettings file/key.
& ./skills/transitjazz-db/scripts/query.ps1 `
    -AppSettingsPath '<selected-appsettings-file>' `
    -ConnectionName TransitJazzDB `
    -Sql 'SELECT current_database(), current_user;'

# Legacy development target, only when that database was selected.
& ./skills/transitjazz-db/scripts/query.ps1 `
    -AppSettingsPath ./src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI/appsettings.Development.json `
    -ConnectionName PokerAttackDB `
    -Sql 'SELECT current_database(), current_user;'
```

An explicit `-AppSettingsPath` is used instead of the connection environment variable. Otherwise the helper reads `ConnectionStrings__TransitJazzDB`, or a different variable selected with `-ConnectionEnvironmentVariable`. When no connection string exists, it uses existing libpq `PGSERVICE` or `PGHOST`/`PGDATABASE`/`PGUSER` settings. This is helper selection behavior, not a reproduction of ASP.NET configuration merging.

The helper translates Host, Port, Database, Username/User ID, Password, SSL Mode, and certificate paths from Npgsql format with the standard .NET connection-string parser. It preserves quoted values rather than splitting on semicolons. It maps `VerifyFull`/`VerifyCA` to libpq's `verify-full`/`verify-ca`. Pooling and EF-specific tuning are not transferred. For multi-host, client-certificate, or other advanced authentication setups, use the caller's approved libpq service configuration rather than guessing equivalent settings.

It temporarily sets connection variables, UTF-8, UTC, a 10-second connection timeout, and a 30-second statement timeout; `-StatementTimeoutSeconds` can change the latter. It restores the previous variables in `finally`. A connection-string password is supplied to the child process through temporary `PGPASSWORD`; it is not a command-line argument or saved file. Use the caller's existing passfile or service where appropriate. PostgreSQL notes that process environments can be visible to other processes on some systems; see [libpq environment variables](https://www.postgresql.org/docs/current/libpq-envars.html).

Every invocation runs the supplied SQL in one read-only Repeatable Read transaction and rolls it back after fetching results. Use reviewed SELECT/introspection SQL and a suitable database role. The helper is not an authorization parser and does not make arbitrary SQL safe. It refuses psql backslash-command lines and stops on SQL errors. Partial output from a failed invocation is not a complete report.

## Deployed connection discovery

If the caller selects the deployed server and no connection is already configured, read the configured secret reference using existing Azure access. The workflow currently names resource group `marta-jazz-dev-rg` and Container App `marta-jazz-dev-ca-server` in `.github/workflows/server.yml`; these are discovery hints, not a claim about current deployment state.

```powershell
# Returns only reference metadata; do not use `az containerapp secret list`.
az containerapp show --resource-group marta-jazz-dev-rg --name marta-jazz-dev-ca-server `
    --query "properties.configuration.secrets[?name=='transitjazz-db'].{name:name,keyVaultUrl:keyVaultUrl}" `
    --output json
```

Use the returned Key Vault URL, including its version when present. If existing identity has secret-read access, capture the required value directly into a temporary process variable and restore it after querying:

```powershell
$dbPreviousConnection = $env:ConnectionStrings__TransitJazzDB
try {
    $dbSecretValue = az keyvault secret show --id $dbSecretUri --query value --output tsv --only-show-errors
    if ($LASTEXITCODE -ne 0 -or -not $dbSecretValue) { throw 'Database secret read failed.' }
    $env:ConnectionStrings__TransitJazzDB = $dbSecretValue
    & ./skills/transitjazz-db/scripts/query.ps1 -Sql 'SELECT current_database(), current_user;'
} finally {
    $env:ConnectionStrings__TransitJazzDB = $dbPreviousConnection
    $dbSecretValue = $null
}
```

`$dbSecretUri` must come from the selected deployment's reference. Access to the Container App does not imply permission to read Key Vault secrets. If access is missing, report the failing layer and the required existing connection/access; do not change RBAC, secrets, firewall rules, or deployment configuration to complete a query.

## Troubleshooting

| Failure | Useful next read-only action |
| --- | --- |
| `psql` not found | Locate an existing PostgreSQL client or use an already available Npgsql-capable client. |
| Missing current connection | Check existence of `TransitJazzDB` without displaying its value; distinguish the legacy key. |
| DNS/refused/timeout | Confirm the selected host and port; inspect network access from this machine. |
| Authentication/Key Vault denied | Identify the failed access layer; use existing authorized credentials or identity. |
| TLS error | Preserve the selected SSL policy; inspect hostname/CA configuration. |
| Relation does not exist | Run schema and migration-history discovery. Do not apply migrations as part of a query request. |
| Empty statistics | Check retained bounds, enabled cities, definitions, and collection state. Empty rows alone do not establish inactivity. |

Native-client details: [psql options and variable quoting](https://www.postgresql.org/docs/current/app-psql.html), [Repeatable Read snapshots](https://www.postgresql.org/docs/current/transaction-iso.html).
