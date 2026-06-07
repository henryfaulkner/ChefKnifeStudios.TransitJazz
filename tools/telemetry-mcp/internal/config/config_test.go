package config

import (
	"strings"
	"testing"
	"time"
)

// TestLoadMigration covers the config / migration vectors from the contract:
// TELEMETRY_STORAGE_URI replaces the removed TELEMETRY_DATASET_URI, an unset
// storage URI yields a startup error naming the new var, and a legacy-only
// TELEMETRY_DATASET_URI is ignored (FR-015 / SC-005).
func TestLoadMigration(t *testing.T) {
	// Ensure a clean baseline; t.Setenv restores prior values after the test.
	t.Setenv("TELEMETRY_TOOL_PATH", "")
	t.Setenv("TELEMETRY_STORAGE_URI", "")
	t.Setenv("TELEMETRY_DATASET_URI", "")
	t.Setenv("TELEMETRY_TIMEOUT_SECONDS", "")
	t.Setenv("TELEMETRY_MAX_OUTPUT_BYTES", "")

	t.Run("both set starts OK with 30s default timeout", func(t *testing.T) {
		t.Setenv("TELEMETRY_TOOL_PATH", "C:\\tool.exe")
		t.Setenv("TELEMETRY_STORAGE_URI", "azure://telemetry")
		cfg, err := Load()
		if err != nil {
			t.Fatalf("unexpected error: %v", err)
		}
		if cfg.StorageURI != "azure://telemetry" {
			t.Errorf("StorageURI = %q, want azure://telemetry", cfg.StorageURI)
		}
		if cfg.TimeoutSeconds != 30*time.Second {
			t.Errorf("default TimeoutSeconds = %v, want 30s", cfg.TimeoutSeconds)
		}
	})

	t.Run("storage URI unset -> error names TELEMETRY_STORAGE_URI", func(t *testing.T) {
		t.Setenv("TELEMETRY_TOOL_PATH", "C:\\tool.exe")
		t.Setenv("TELEMETRY_STORAGE_URI", "")
		_, err := Load()
		if err == nil {
			t.Fatal("expected error when TELEMETRY_STORAGE_URI is unset")
		}
		if !strings.Contains(err.Error(), "TELEMETRY_STORAGE_URI") {
			t.Errorf("error should name TELEMETRY_STORAGE_URI, got: %v", err)
		}
	})

	t.Run("legacy-only TELEMETRY_DATASET_URI is ignored", func(t *testing.T) {
		t.Setenv("TELEMETRY_TOOL_PATH", "C:\\tool.exe")
		t.Setenv("TELEMETRY_STORAGE_URI", "")
		t.Setenv("TELEMETRY_DATASET_URI", "azure://telemetry/iris.parquet")
		_, err := Load()
		if err == nil {
			t.Fatal("expected error: legacy TELEMETRY_DATASET_URI must not satisfy the requirement")
		}
		if !strings.Contains(err.Error(), "TELEMETRY_STORAGE_URI") {
			t.Errorf("error should point to TELEMETRY_STORAGE_URI, got: %v", err)
		}
	})

	t.Run("tool path unset -> error names TELEMETRY_TOOL_PATH", func(t *testing.T) {
		t.Setenv("TELEMETRY_TOOL_PATH", "")
		t.Setenv("TELEMETRY_STORAGE_URI", "azure://telemetry")
		_, err := Load()
		if err == nil {
			t.Fatal("expected error when TELEMETRY_TOOL_PATH is unset")
		}
		if !strings.Contains(err.Error(), "TELEMETRY_TOOL_PATH") {
			t.Errorf("error should name TELEMETRY_TOOL_PATH, got: %v", err)
		}
	})
}
