# Architecture Design Document: Ephemeral-Buffered Telemetry Pipeline

## 1. System Overview
To stay within a **$3/month budget** on Azure Container Apps (ACA) with a strict `min:1, max:1` instance count, this design replaces an expensive cloud database with a local, ephemeral transactional buffer that regularly flushes compressed analytical files to Azure Blob Storage. 

+-------------------------------------------------------------+
|               Azure Container App (Single Instance)         |
|                                                             |
| [Incoming Telemetry] -> [SQLite WAL] -> [Background Worker] |
+----------------------------------------------------|--------+
| (Every 15 mins / 5MB)
v (Uploads Parquet)
+--------------------------+
| Azure Blob Storage       |
| (Telemetry Lakehouse)    |
+--------------------------+
^
| (Direct S3-API Queries)
+-----------|--------------+
| Local Machine / CLI      |
| [DuckDB Engine]          |
+--------------------------+


## 2. Technical Decisions & Constraints
* **Storage Engine:** SQLite configured in Write-Ahead Logging (`WAL`) mode to handle concurrent, high-frequency writes to the ACA container's local `/data` directory.
* **Shipping Format:** Apache Parquet. Columnar compression reduces telemetry size by up to **80%**, drastically cutting down Azure network egress and storage costs.
* **Micro-batching Trigger:** A background thread flushes data either every **15 minutes** or when the local file reaches **5 MB**, minimizing data loss visibility in the event of an ephemeral container crash.
* **Query Engine:** DuckDB running locally. It uses HTTP range requests to selectively query chunks of Parquet files directly out of Azure Blob Storage without downloading the entire dataset.

---

## 3. C# Implementation (The Data Producer)

### Dependencies
Add these NuGet packages to your C# project:
```bash
dotnet add package Microsoft.Data.Sqlite
dotnet add package Parquet.Net
dotnet add package Azure.Storage.Blobs
Telemetry Pipeline Implementation
This background service handles writing to a local SQLite database, cutting it off periodically, converting it to Parquet, and shipping it to Azure Blob Storage.

C#
using System;
using System.IO;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Azure.Storage.Blobs;
using Parquet;
using Parquet.Data;
using Parquet.Schema;

public class TelemetryPayload
{
    public DateTime Timestamp { get; set; }
    public string Level { get; set; }
    public string Message { get; set; }
    public string Service { get; set; }
}

public class TelemetryPipeline
{
    private readonly string _dbPath = "/data/telemetry.db";
    private readonly string _connectionString = "Data Source=/data/telemetry.db;";
    private readonly string _azureConnectionString = "Your_Azure_Blob_Storage_Connection_String";
    private readonly string _containerName = "telemetry-lake";
    private static readonly SemaphoreSlim _lock = new SemaphoreSlim(1, 1);

    public TelemetryPipeline()
    {
        // 1. Initialize SQLite with high-performance WAL PRAGMAs
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        
        using var command = connection.CreateCommand();
        command.CommandText = @"
            PRAGMA journal_mode = WAL;
            PRAGMA synchronous = NORMAL;
            CREATE TABLE IF NOT EXISTS Logs (
                Timestamp TEXT,
                Level TEXT,
                Message TEXT,
                Service TEXT
            );";
        command.ExecuteNonQuery();
    }

    public async Task LogAsync(TelemetryPayload log)
    {
        // High-speed write path
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        
        using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO Logs VALUES ($ts, $lvl, $msg, $svc);";
        command.Parameters.AddWithValue("$ts", log.Timestamp.ToString("o"));
        command.Parameters.AddWithValue("$lvl", log.Level);
        command.Parameters.AddWithValue("$msg", log.Message);
        command.Parameters.AddWithValue("$svc", log.Service);
        
        await command.ExecuteNonQueryAsync();
    }

    public async Task FlushToBlobStorageAsync()
    {
        await _lock.WaitAsync();
        try
        {
            var fileInfo = new FileInfo(_dbPath);
            if (!fileInfo.Exists || fileInfo.Length == 0) return;

            string tempDbPath = $"/data/telemetry_{Guid.NewGuid()}.db";
            
            // Close connection & safely rotate the database file out
            SqliteConnection.ClearAllPools();
            File.Move(_dbPath, tempDbPath);

            // Re-initialize primary DB for seamless incoming app logs
            _ = new TelemetryPipeline();

            // Process the rotated file into Parquet
            string parquetPath = tempDbPath.Replace(".db", ".parquet");
            await ConvertSqliteToParquetAsync(tempDbPath, parquetPath);

            // Ship to Azure
            await UploadToAzureAsync(parquetPath);

            // Clean up disk footprint
            File.Delete(tempDbPath);
            File.Delete(parquetPath);
            File.Delete(tempDbPath + "-wal"); // Clean lingering WAL artifacts
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task ConvertSqliteToParquetAsync(string sqliteSrc, string parquetDest)
    {
        var schema = new ParquetSchema(
            new DataField<string>("Timestamp"),
            new DataField<string>("Level"),
            new DataField<string>("Message"),
            new DataField<string>("Service")
        );

        using var connection = new SqliteConnection($"Data Source={sqliteSrc};");
        await connection.OpenAsync();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Timestamp, Level, Message, Service FROM Logs;";
        using var reader = await command.ExecuteReaderAsync();

        var tsList = new List<string>();
        var lvlList = new List<string>();
        var msgList = new List<string>();
        var svcList = new List<string>();

        while (await reader.ReadAsync())
        {
            tsList.Add(reader.GetString(0));
            lvlList.Add(reader.GetString(1));
            msgList.Add(reader.GetString(2));
            svcList.Add(reader.GetString(3));
        }

        if (tsList.Count == 0) return;

        using Stream fileStream = File.Create(parquetDest);
        using var parquetWriter = await ParquetWriter.CreateAsync(schema, fileStream);
        using ParquetRowGroupWriter groupWriter = parquetWriter.CreateRowGroup();

        await groupWriter.WriteColumnAsync(new DataColumn(schema.Fields[0], tsList.ToArray()));
        await groupWriter.WriteColumnAsync(new DataColumn(schema.Fields[1], lvlList.ToArray()));
        await groupWriter.WriteColumnAsync(new DataColumn(schema.Fields[2], msgList.ToArray()));
        await groupWriter.WriteColumnAsync(new DataColumn(schema.Fields[3], svcList.ToArray()));
    }

    private async Task UploadToAzureAsync(string filePath)
    {
        var blobServiceClient = new BlobServiceClient(_azureConnectionString);
        var containerClient = blobServiceClient.GetBlobContainerClient(_containerName);
        await containerClient.CreateIfNotExistsAsync();

        // Organize blob storage paths in a Hive structure (year/month/day) for optimal analytical indexing
        string blobName = $"year={DateTime.UtcNow:yyyy}/month={DateTime.UtcNow:MM}/day={DateTime.UtcNow:dd}/{Path.GetFileName(filePath)}";
        var blobClient = containerClient.GetBlobClient(blobName);

        using FileStream uploadFileStream = File.OpenRead(filePath);
        await blobClient.UploadAsync(uploadFileStream, true);
    }
}
4. Querying the Data Store with DuckDB (The Consumer)
Because the C# data producer exports standardized Apache Parquet files, any DuckDB instance (CLI, Python script, Node.js tool, or DBeaver) can natively run analytical SQL over your Azure Blob container for free.

Step 1: Install DuckDB & Load the Azure extension
Open your local DuckDB CLI interface and load the required extensions:

SQL
INSTALL azure;
LOAD azure;
Step 2: Configure Azure Access Credentials
Provide DuckDB with your storage connection secrets so it can authenticate securely against Azure:

SQL
-- Authenticate via Connection String
SET azure_storage_connection_string = 'DefaultEndpointsProtocol=https;AccountName=your_account;AccountKey=your_key;EndpointSuffix=core.windows.net';

-- ALTERNATIVELY: Authenticate via Account Name and Key
SET azure_account_name = 'your_storage_account_name';
SET azure_credential_provider = 'secret';
SET azure_access_key = 'your_storage_account_access_key';
Step 3: Run High-Performance Analytics Queries
DuckDB handles globbing patterns natively (*). It will read the metadata of all matching remote Parquet files simultaneously and pull down only the specific byte regions required to fulfill your SQL statement.

Query 1: Count aggregate errors over a specific month
SQL
SELECT 
    Service, 
    COUNT(*) as ErrorCount 
FROM 'azure://telemetry-lake/year=2026/month=05/*/*.parquet'
WHERE Level = 'ERROR'
GROUP BY Service
ORDER BY ErrorCount DESC;
Query 2: Search logs for a specific incident string
SQL
SELECT Timestamp, Service, Message 
FROM 'azure://telemetry-lake/year=2026/month=05/day=31/*.parquet'
WHERE Message LIKE '%NullReferenceException%'
ORDER BY Timestamp DESC 
LIMIT 100;

---

## 5. Building the Go Query Tool (`telemetry-query-tool`)

The `telemetry-query-tool` is a small Go CLI that wraps DuckDB + the Azure
extension, so an agent (or operator) can run a single SQL query against the
Parquet lakehouse and get a clean ASCII table back. It depends on
`github.com/marcboeker/go-duckdb`, which is a **CGO** package — so the build
needs a C toolchain, and getting the Azure extension to load on Windows
requires linking DuckDB's official precompiled library rather than the
bundled amalgamation.

### Why the non-obvious setup is required

* **CGO needs a GCC/Clang toolchain.** `go-duckdb` cannot be built with
  `CGO_ENABLED=0` (you get `undefined: Conn`), and it cannot use MSVC
  (`cl.exe`) — Go's cgo only drives GCC/Clang-style compilers. We use
  **mingw-w64 gcc** (WinLibs).
* **A mingw build can't load DuckDB's prebuilt extensions.** A DuckDB engine
  compiled with mingw reports its platform as `windows_amd64_mingw`, and
  DuckDB publishes **no** prebuilt extensions for that platform (the `azure`
  download 404s). The `windows_amd64` (MSVC) extension is hard-rejected by the
  engine due to the platform-tag mismatch, and DuckDB v1.1.3 has no
  `custom_platform` override.
* **Fix: link the official `libduckdb`.** Building with the `duckdb_use_lib`
  tag against DuckDB's precompiled `windows_amd64` library makes the engine
  report `windows_amd64`, so the real `azure` extension installs and loads
  normally.

### Prerequisites (one-time)

1. **Go** (1.25.x used here).
2. **mingw-w64 gcc** — install via winget:
   ```powershell
   winget install --id BrechtSanders.WinLibs.POSIX.UCRT
   ```
   A new terminal picks up `gcc` on `PATH` automatically.
3. **Official precompiled `libduckdb` matching the bundled DuckDB version**
   (v1.1.3 here). Download `libduckdb-windows-amd64.zip` from the DuckDB
   release and extract it into a `libduckdb/` folder next to `main.go`. It
   contains `duckdb.dll`, `duckdb.h`, `duckdb.hpp`, and `duckdb.lib`:
   ```powershell
   $dest = ".\libduckdb"
   New-Item -ItemType Directory -Force -Path $dest | Out-Null
   $zip = "$env:TEMP\libduckdb.zip"
   Invoke-WebRequest `
     "https://github.com/duckdb/duckdb/releases/download/v1.1.3/libduckdb-windows-amd64.zip" `
     -OutFile $zip -UseBasicParsing
   Expand-Archive $zip -DestinationPath $dest -Force
   ```
4. **A mingw import library** generated from `duckdb.dll` (mingw can't link
   the MSVC `.lib` directly). `gendef` and `dlltool` ship with WinLibs:
   ```powershell
   cd .\libduckdb
   gendef duckdb.dll
   dlltool -d duckdb.def -l libduckdb.dll.a -D duckdb.dll
   cd ..
   ```

### Build command

```powershell
$env:CGO_ENABLED = '1'
$env:CC          = 'gcc'
$env:CGO_CFLAGS  = "-I$PWD\libduckdb"
$env:CGO_LDFLAGS = "-L$PWD\libduckdb -lduckdb"

go build -tags "duckdb_use_lib no_duckdb_arrow" -o telemetry-query-tool.exe .
```

* `duckdb_use_lib` — links the official precompiled `libduckdb` instead of
  building the bundled amalgamation from source.
* `no_duckdb_arrow` — disables the wrapper's Arrow C-data integration, whose
  helper symbols (`ArrowArrayRelease`, `ArrowSchemaRelease`, …) are **not**
  exported by the official `libduckdb` and would otherwise fail at link time.
  The tool scans rows as raw `[]byte`, so Arrow support is unused.

### Running

`duckdb.dll` must sit next to the built executable at runtime (it is now
dynamically linked):

```powershell
Copy-Item .\libduckdb\duckdb.dll .\duckdb.dll -Force

.\telemetry-query-tool.exe "SELECT variety, COUNT(*) AS Count FROM 'azure://parquet/iris.parquet' GROUP BY variety ORDER BY variety"
```

Expected output:

```
Fetching and analyzing telemetry data from Azure Blob Storage...
┌────────────┬───────┐
│  VARIETY   │ COUNT │
├────────────┼───────┤
│ Setosa     │ 50    │
│ Versicolor │ 50    │
│ Virginica  │ 50    │
└────────────┴───────┘
```

### Notes / gotchas

* **Version match matters.** The precompiled `libduckdb` must match the
  DuckDB version bundled by your `go-duckdb` release (v1.1.3 here). If you bump
  `go-duckdb`, re-download the matching `libduckdb` and regenerate the import
  library.
* **The Azure connection string is a secret.** Provide it via environment /
  Azure secrets — do not hardcode the storage `AccountKey` in source.
* **Distribution.** Ship `duckdb.dll` alongside the `.exe`. The `azure`
  extension is auto-installed into `~/.duckdb/extensions/...` on first run
  (requires outbound HTTPS to `extensions.duckdb.org`).