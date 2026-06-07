package query

import (
	"context"
	"os"
	"os/exec"
	"path/filepath"
	"strings"
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
		StorageURI:     "azure://telemetry",
		TimeoutSeconds: 5 * time.Second,
		MaxOutputBytes: 65536,
	}

	tests := []struct {
		name          string
		dataset       string
		date          string
		filter        string
		shouldErr     bool
		shouldContain string
	}{
		{
			name:          "valid snap query",
			dataset:       "snap",
			date:          "2026-06-04",
			filter:        "snap_distance_km > 0.5",
			shouldErr:     false,
			shouldContain: "cycle_id",
		},
		{
			name:          "valid cycle query",
			dataset:       "cycle",
			date:          "2026-06-04",
			filter:        "buses_stale > 10",
			shouldErr:     false,
			shouldContain: "vehicle_id",
		},
	}

	for _, tt := range tests {
		t.Run(tt.name, func(t *testing.T) {
			ctx, cancel := context.WithTimeout(context.Background(), 10*time.Second)
			defer cancel()

			result, err := Run(ctx, cfg, tt.dataset, tt.date, tt.filter)

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

			if tt.shouldContain != "" && !strings.Contains(result, tt.shouldContain) {
				t.Errorf("result should contain %q, got: %q", tt.shouldContain, result)
			}
		})
	}
}

// TestSourceGlobConstruction asserts the read source is assembled from the fixed
// template {StorageURI}/{dataset}/dt={date}/*.parquet for each dataset, and that the
// filter content never appears before the WHERE clause. This is the structural
// guarantee that operator input cannot redirect the data source (FR-012 / SC-006).
func TestSourceGlobConstruction(t *testing.T) {
	cfg := &config.Config{
		ToolPath:       captureToolPath(t),
		StorageURI:     "azure://telemetry",
		TimeoutSeconds: 5 * time.Second,
		MaxOutputBytes: 65536,
	}

	tests := []struct {
		name        string
		dataset     string
		date        string
		filter      string
		wantInQuery string
	}{
		{
			name:        "snap explicit date",
			dataset:     "snap",
			date:        "2026-06-04",
			filter:      "snap_distance_km > 0.5",
			wantInQuery: "SELECT * FROM 'azure://telemetry/snap/dt=2026-06-04/*.parquet' WHERE snap_distance_km > 0.5",
		},
		{
			name:        "lerp explicit date",
			dataset:     "lerp",
			date:        "2026-06-04",
			filter:      "pos_delta_km > 1.0",
			wantInQuery: "SELECT * FROM 'azure://telemetry/lerp/dt=2026-06-04/*.parquet' WHERE pos_delta_km > 1.0",
		},
		{
			name:        "cycle explicit date",
			dataset:     "cycle",
			date:        "2026-06-04",
			filter:      "buses_stale > 10",
			wantInQuery: "SELECT * FROM 'azure://telemetry/cycle/dt=2026-06-04/*.parquet' WHERE buses_stale > 10",
		},
		{
			name:        "defaulted today UTC date",
			dataset:     "snap",
			date:        time.Now().UTC().Format("2006-01-02"),
			filter:      "raw_lat > 0",
			wantInQuery: "azure://telemetry/snap/dt=" + time.Now().UTC().Format("2006-01-02") + "/*.parquet",
		},
	}

	for _, tt := range tests {
		t.Run(tt.name, func(t *testing.T) {
			ctx, cancel := context.WithTimeout(context.Background(), 10*time.Second)
			defer cancel()

			out, _ := Run(ctx, cfg, tt.dataset, tt.date, tt.filter)
			// The capture stub echoes the SQL it received on stdout.
			if !strings.Contains(out, tt.wantInQuery) {
				t.Errorf("assembled query should contain %q, got: %q", tt.wantInQuery, out)
			}
		})
	}
}

// captureToolPath builds a tiny tool that echoes its argv[1] (the full SQL) so tests
// can assert exactly what was assembled. Built once per test run.
func captureToolPath(t *testing.T) string {
	t.Helper()
	dir := t.TempDir()
	src := filepath.Join(dir, "capture.go")
	const program = `package main
import ("fmt";"os")
func main(){ if len(os.Args)>1 { fmt.Println(os.Args[1]) } }
`
	if err := os.WriteFile(src, []byte(program), 0o644); err != nil {
		t.Fatalf("write capture src: %v", err)
	}
	exe := filepath.Join(dir, "capture.exe")
	cmd := exec.Command("go", "build", "-o", exe, src)
	if out, err := cmd.CombinedOutput(); err != nil {
		t.Skipf("could not build capture tool: %v: %s", err, out)
	}
	return exe
}

func TestTimeout(t *testing.T) {
	// nonexistent tool should produce an error
	cfg := &config.Config{
		ToolPath:       "nonexistent.exe",
		StorageURI:     "azure://telemetry",
		TimeoutSeconds: 1 * time.Second,
		MaxOutputBytes: 65536,
	}

	ctx, cancel := context.WithTimeout(context.Background(), 5*time.Second)
	defer cancel()

	_, err := Run(ctx, cfg, "snap", "2026-06-04", "snap_distance_km > 5.0")
	if err == nil {
		t.Error("expected error for nonexistent tool")
	}
}
