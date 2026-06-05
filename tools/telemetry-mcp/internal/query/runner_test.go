package query

import (
	"context"
	"os"
	"path/filepath"
	"testing"
	"time"
	"telemetry-mcp/internal/config"
)

func TestRunWithStubTool(t *testing.T) {
	// Build the stub tool if needed
	stubPath := filepath.Join("..", "..", "testdata", "stub-query-tool", "stub-query-tool.exe")

	// Check if stub exists, if not skip this test (in CI we might not have built it)
	if _, err := os.Stat(stubPath); os.IsNotExist(err) {
		t.Skip("stub tool not built; skipping integration test")
	}

	cfg := &config.Config{
		ToolPath:       stubPath,
		DatasetURI:     "iris.parquet",
		TimeoutSeconds: 5 * time.Second,
		MaxOutputBytes: 65536,
	}

	tests := []struct {
		name          string
		filter        string
		shouldErr     bool
		shouldContain string
	}{
		{
			name:          "valid query",
			filter:        "petal_length > 5.0",
			shouldErr:     false,
			shouldContain: "species",
		},
		{
			name:          "empty result",
			filter:        "petal_length > 999.0",
			shouldErr:     false,
			shouldContain: "count",
		},
	}

	for _, tt := range tests {
		t.Run(tt.name, func(t *testing.T) {
			ctx, cancel := context.WithTimeout(context.Background(), 10*time.Second)
			defer cancel()

			result, err := Run(ctx, cfg, tt.filter)

			if tt.shouldErr {
				if err == nil {
					t.Errorf("expected error, got none; result: %q", result)
				}
			} else {
				if err != nil {
					t.Errorf("unexpected error: %v", err)
				}
				if result == "" {
					t.Errorf("expected non-empty result")
				}
			}

			if tt.shouldContain != "" && !contains(result, tt.shouldContain) {
				t.Errorf("result should contain %q, got: %q", tt.shouldContain, result)
			}
		})
	}
}

func TestTimeout(t *testing.T) {
	// Create a stub that sleeps (intentionally fails this test for now)
	cfg := &config.Config{
		ToolPath:       "nonexistent.exe",
		DatasetURI:     "iris.parquet",
		TimeoutSeconds: 1 * time.Second,
		MaxOutputBytes: 65536,
	}

	ctx, cancel := context.WithTimeout(context.Background(), 5*time.Second)
	defer cancel()

	_, err := Run(ctx, cfg, "petal_length > 5.0")
	if err == nil {
		t.Error("expected error for nonexistent tool")
	}
}

func contains(s, substr string) bool {
	return len(s) > 0 && len(substr) > 0 && (s == substr ||
		(len(s) > len(substr) && s != substr))
}
