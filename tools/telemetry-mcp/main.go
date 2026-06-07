package main

import (
	"context"
	"log/slog"
	"os"
	"time"

	"github.com/mark3labs/mcp-go/mcp"
	"github.com/mark3labs/mcp-go/server"
	"telemetry-mcp/internal/config"
	"telemetry-mcp/internal/query"
	"telemetry-mcp/internal/validate"
)

func main() {
	// Set up structured logging to stderr only (reserve stdout for MCP protocol)
	logger := slog.New(slog.NewJSONHandler(os.Stderr, &slog.HandlerOptions{
		Level: slog.LevelInfo,
	}))
	slog.SetDefault(logger)

	// Load config from environment
	cfg, err := config.Load()
	if err != nil {
		slog.Error("failed to load config", "error", err)
		os.Exit(1)
	}

	slog.Info("telemetry-mcp started",
		"storage_uri", cfg.StorageURI,
		"tool_path", cfg.ToolPath,
		"timeout_seconds", cfg.TimeoutSeconds,
		"max_output_bytes", cfg.MaxOutputBytes,
	)

	// Create MCP server
	s := server.NewMCPServer(
		"telemetry-mcp",
		"0.1.0",
	)

	// Define the query_telemetry tool
	tool := mcp.NewTool(
		"query_telemetry",
		mcp.WithDescription("Query the TransitJazz telemetry datasets produced by the logging sidecar. "+
			"Three datasets are available: "+
			"snap (one row per per-vehicle snap decision in a reconciliation cycle), "+
			"lerp (one row per per-vehicle position delta), and "+
			"cycle (one row per completed reconciliation cycle: counts, timing, health). "+
			"Supply a read-only filter over that dataset's columns. Only column names, "+
			"numeric/string/bool literals, comparison operators (<, <=, >, >=, =, !=), and "+
			"logical connectors (AND, OR) are allowed. Timestamp columns compare against date "+
			"strings (e.g. observation_utc > '2026-06-04'). Boolean columns compare against "+
			"true/false (e.g. is_stale = false)."),
		mcp.WithString("dataset",
			mcp.Required(),
			mcp.Description("Which dataset to query: one of \"snap\", \"lerp\", or \"cycle\"."),
		),
		mcp.WithString("date",
			mcp.Description("UTC day to query, format YYYY-MM-DD (e.g. 2026-06-04). Optional; defaults to today (UTC)."),
		),
		mcp.WithString("filter",
			mcp.Required(),
			mcp.Description("A read-only filter condition over the dataset's columns (e.g. 'snap_distance_km > 0.5 AND is_stale = false'). Only column names, numeric/string/bool literals, comparison operators (<, <=, >, >=, =, !=), and logical connectors (AND, OR) are allowed."),
		),
	)

	// Register the tool handler. Validation order is fixed and short-circuits on the
	// first failure, before any query assembly: dataset -> date -> filter.
	s.AddTool(tool, func(arguments map[string]interface{}) (*mcp.CallToolResult, error) {
		dataset, ok := arguments["dataset"].(string)
		if !ok {
			return mcp.NewToolResultError("missing or invalid dataset argument"), nil
		}

		filter, ok := arguments["filter"].(string)
		if !ok {
			return mcp.NewToolResultError("missing or invalid filter argument"), nil
		}

		// 1. Validate the dataset name before anything else touches the filter.
		if err := validate.ValidateDataset(dataset); err != nil {
			slog.Warn("dataset validation failed", "error", err)
			return mcp.NewToolResultError(err.Error()), nil
		}

		// 2. Validate the date (default to today UTC when omitted).
		date, _ := arguments["date"].(string)
		if date == "" {
			date = time.Now().UTC().Format("2006-01-02")
		}
		validatedDate, err := validate.ValidateDate(date)
		if err != nil {
			slog.Warn("date validation failed", "error", err)
			return mcp.NewToolResultError(err.Error()), nil
		}

		// 3. Validate the filter against the selected dataset's allow-list grammar.
		validatedFilter, err := validate.Filter(dataset, filter)
		if err != nil {
			slog.Warn("filter validation failed", "error", err)
			return mcp.NewToolResultError(err.Error()), nil
		}

		slog.Info("query validated", "dataset", dataset, "date", validatedDate, "filter", filter, "canonical", validatedFilter)

		// Run the validated query against the underlying tool.
		result, err := query.Run(context.Background(), cfg, dataset, validatedDate, validatedFilter)
		if err != nil {
			slog.Error("query execution failed", "error", err)
			return mcp.NewToolResultError(err.Error()), nil
		}

		return mcp.NewToolResultText(result), nil
	})

	// Start the server over stdio
	slog.Info("starting stdio server")
	if err := server.ServeStdio(s); err != nil {
		slog.Error("server error", "error", err)
		os.Exit(1)
	}
}
