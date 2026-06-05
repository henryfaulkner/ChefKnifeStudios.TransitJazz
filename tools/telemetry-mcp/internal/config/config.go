package config

import (
	"fmt"
	"os"
	"strconv"
	"time"
)

type Config struct {
	ToolPath         string
	DatasetURI       string
	TimeoutSeconds   time.Duration
	MaxOutputBytes   int
}

func Load() (*Config, error) {
	toolPath := os.Getenv("TELEMETRY_TOOL_PATH")
	if toolPath == "" {
		return nil, fmt.Errorf("TELEMETRY_TOOL_PATH environment variable is required")
	}

	datasetURI := os.Getenv("TELEMETRY_DATASET_URI")
	if datasetURI == "" {
		return nil, fmt.Errorf("TELEMETRY_DATASET_URI environment variable is required")
	}

	timeoutStr := os.Getenv("TELEMETRY_TIMEOUT_SECONDS")
	if timeoutStr == "" {
		timeoutStr = "10"
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
		DatasetURI:     datasetURI,
		TimeoutSeconds: time.Duration(timeout) * time.Second,
		MaxOutputBytes: maxOutput,
	}, nil
}
