# telemetry-mcp

A small, standalone Go MCP server that exposes a single `query_telemetry` tool to
Claude Code over stdio. It validates an untrusted `filter` against a strict
allow-list grammar, then invokes the existing `telemetry-query-tool.exe` to run a
read-only query over the fixed `iris.parquet` dataset.

> Full build, configuration, registration, and acceptance steps live in
> [`specs/012-telemetry-mcp-bridge/quickstart.md`](../../specs/012-telemetry-mcp-bridge/quickstart.md).
