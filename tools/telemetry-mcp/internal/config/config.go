package config

import (
	"fmt"
	"os"
	"strconv"
	"time"
)

type Config struct {
	ToolPath       string
	StorageURI     string
	TimeoutSeconds time.Duration
	MaxOutputBytes int
}

func Load() (*Config, error) {
	toolPath := os.Getenv("TELEMETRY_TOOL_PATH")
	if toolPath == "" {
		return nil, fmt.Errorf("TELEMETRY_TOOL_PATH environment variable is required")
	}

	// StorageURI is the container base (e.g. azure://telemetry); the per-query source
	// glob {StorageURI}/{dataset}/dt={date}/*.parquet is assembled in the runner.
	// The legacy TELEMETRY_DATASET_URI is intentionally ignored.
	storageURI := os.Getenv("TELEMETRY_STORAGE_URI")
	if storageURI == "" {
		return nil, fmt.Errorf("TELEMETRY_STORAGE_URI environment variable is required")
	}

	timeoutStr := os.Getenv("TELEMETRY_TIMEOUT_SECONDS")
	if timeoutStr == "" {
		timeoutStr = "30"
	}
	timeout, err := strconv.Atoi(timeoutStr)
	if err != nil {
		return nil, fmt.Errorf("invalid TELEMETRY_TIMEOUT_SECONDS: %w", err)
	}

	maxOutputStr := os.Getenv("TELEMETRY_MAX_OUTPUT_BYTES")
	if maxOutputStr == "" {
		maxOutputStr = "65536"
	}
	maxOutput, err := strconv.Atoi(maxOutputStr)
	if err != nil {
		return nil, fmt.Errorf("invalid TELEMETRY_MAX_OUTPUT_BYTES: %w", err)
	}

	return &Config{
		ToolPath:       toolPath,
		StorageURI:     storageURI,
		TimeoutSeconds: time.Duration(timeout) * time.Second,
		MaxOutputBytes: maxOutput,
	}, nil
}
