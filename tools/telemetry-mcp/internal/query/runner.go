package query

import (
	"bytes"
	"context"
	"fmt"
	"os"
	"os/exec"
	"strings"
	"telemetry-mcp/internal/config"
)

// Run executes a validated filter against the named dataset/day partition using the
// underlying tool. The data source is assembled from a constant template filled only
// with the already-validated dataset and date, so operator input can never redirect it.
func Run(ctx context.Context, cfg *config.Config, dataset, date, validatedFilter string) (string, error) {
	// Build the read source from the fixed template: {StorageURI}/{dataset}/dt={date}/*.parquet
	sourceGlob := fmt.Sprintf("%s/%s/dt=%s/*.parquet", cfg.StorageURI, dataset, date)

	// Build the full SQL query: the data source is fixed, only the filter varies
	fullQuery := fmt.Sprintf("SELECT * FROM '%s' WHERE %s", sourceGlob, validatedFilter)

	// Create the command with the configured timeout context
	cmdCtx, cancel := context.WithTimeout(ctx, cfg.TimeoutSeconds)
	defer cancel()

	cmd := exec.CommandContext(cmdCtx, cfg.ToolPath, fullQuery)

	// Capture stdout and stderr
	var stdout, stderr bytes.Buffer
	cmd.Stdout = &stdout
	cmd.Stderr = &stderr

	// Run the command
	if err := cmd.Run(); err != nil {
		// Check if it's a timeout
		if cmdCtx.Err() == context.DeadlineExceeded {
			return "", fmt.Errorf("query execution timed out after %v", cfg.TimeoutSeconds)
		}

		// Check if the executable was not found
		if _, isNotExist := err.(*exec.ExitError); isNotExist {
			stderrText := stderr.String()
			if stderrText != "" {
				// Try to extract just the error message without paths
				return "", fmt.Errorf("query tool error: %s", sanitizeError(stderrText))
			}
			return "", fmt.Errorf("query execution failed")
		}

		// For exec.ExitError and other errors, check stderr
		stderrText := stderr.String()
		if stderrText != "" {
			return "", fmt.Errorf("query tool error: %s", sanitizeError(stderrText))
		}
		return "", fmt.Errorf("query execution failed: %v", err)
	}

	// Get stdout and apply size limit
	result := stdout.String()
	if len(result) > cfg.MaxOutputBytes {
		result = result[:cfg.MaxOutputBytes] + "\n...[truncated due to size]"
	}

	// Optionally trim the status line (first line if it starts with "Fetching")
	lines := strings.Split(result, "\n")
	if len(lines) > 0 && strings.HasPrefix(lines[0], "Fetching") {
		result = strings.Join(lines[1:], "\n")
	}

	return strings.TrimSpace(result), nil
}

// sanitizeError removes absolute paths and sensitive configuration from error messages
func sanitizeError(errMsg string) string {
	// Remove common absolute path prefixes (Windows and Unix)
	replacements := []string{
		"C:\\", "/root/", "/home/", "/Users/", "/var/", "/etc/",
		os.Getenv("AZURE_STORAGE_CONNECTION_STRING"),
	}

	result := errMsg
	for _, replacement := range replacements {
		if replacement != "" {
			result = strings.ReplaceAll(result, replacement, "[sanitized]")
		}
	}

	// Only keep the first line of stderr for brevity
	lines := strings.Split(result, "\n")
	if len(lines) > 0 {
		return lines[0]
	}

	return result
}
