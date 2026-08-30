# Centralized logging removal audit

Complete this audit after the seven-day dual run and one centralized-logs-only release. It is a
guard against removing a shared telemetry consumer. Historical Parquet blobs are preserved unless
a separate archival/deletion approval explicitly says otherwise.

## Audit metadata

| Field | Value |
|---|---|
| Audit date (UTC) | `PENDING` |
| Release revision | `PENDING` |
| Auditor/approver | `PENDING` |
| Seven-day evidence record | `PENDING` |
| Centralized-only normal release | `PENDING` |

## Consumer inventory

| Surface | Reviewed location | Consumer found | Action/owner |
|---|---|---|---|
| Worker Parquet producer/sidecar | `src/Server/...TransitDataWorker` | `PENDING` | `PENDING` |
| Blob storage/RBAC/configuration | `bicep/`, app settings | `PENDING` | `PENDING` |
| Web API telemetry route/DTOs | `src/Server/`, `src/ChefKnifeStudios.TransitJazz.Shared/` | `PENDING` | `PENDING` |
| Client telemetry UI | `src/Client/` | `PENDING` | `PENDING` |
| Query tools/MCP | `tools/` | `PENDING` | `PENDING` |
| Agent skills/registrations | `skills/`, `.mcp.json`, `.codex/` | `PENDING` | `PENDING` |
| Documentation/contracts | `docs/`, `specs/` | `PENDING` | `PENDING` |

## Historical blob preservation

| Storage account/container | Existing blobs verified | Deletion approval | Result |
|---|---|---|---|
| `PENDING` | `PENDING` | `PENDING` | `PENDING` |

No cleanup task may delete historical blobs or their storage account from this feature without the
separate approval recorded above.

