package main

import (
	"context"
	"log/slog"
	"os"

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
		"dataset_uri", cfg.DatasetURI,
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
		mcp.WithDescription("Query the iris.parquet telemetry dataset with a read-only filter condition"),
		mcp.WithString("filter",
			mcp.Required(),
			mcp.Description("A read-only filter condition (e.g., 'petal_length > 5.0 AND species = 'setosa''). Only column names, numeric/string literals, comparison operators (<, <=, >, >=, =, !=), and logical connectors (AND, OR) are allowed."),
		),
	)

	// Register the tool handler
	s.AddTool(tool, func(arguments map[string]interface{}) (*mcp.CallToolResult, error) {
		filter, ok := arguments["filter"].(string)
		if !ok {
			return mcp.NewToolResultError("missing or invalid filter argument"), nil
		}

		// Validate the filter against the allow-list grammar
		validatedFilter, err := validate.Filter(filter)
		if err != nil {
			slog.Warn("filter validation failed", "error", err)
			return mcp.NewToolResultError(err.Error()), nil
		}

		slog.Info("filter validated", "filter", filter, "canonical", validatedFilter)

		// Run the validated query against the underlying tool
		result, err := query.Run(context.Background(), cfg, validatedFilter)
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
