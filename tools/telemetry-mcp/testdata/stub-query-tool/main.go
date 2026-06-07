package main

import (
	"fmt"
	"os"
	"strings"
)

func main() {
	if len(os.Args) < 2 {
		fmt.Fprintln(os.Stderr, "Usage: stub-query-tool \"SELECT ...\"")
		os.Exit(1)
	}

	query := os.Args[1]

	// Simple stub: just echo a mock table
	fmt.Println("Fetching and analyzing telemetry data from Azure Blob Storage...")

	// Check that the query references the transit telemetry layout and one of the
	// three known datasets (mirrors the constant source template the bridge assembles).
	lower := strings.ToLower(query)
	if !strings.Contains(lower, "telemetry/") ||
		(!strings.Contains(lower, "snap") &&
			!strings.Contains(lower, "lerp") &&
			!strings.Contains(lower, "cycle")) {
		fmt.Fprintln(os.Stderr, "Query validation error: telemetry dataset reference missing")
		os.Exit(1)
	}

	// Return a transit-shaped mock result table.
	fmt.Println(`+----------+------------+------------------+`)
	fmt.Println(`| cycle_id | vehicle_id | observation_utc  |`)
	fmt.Println(`+----------+------------+------------------+`)
	fmt.Println(`| c-001    | v001       | 2026-06-04       |`)
	fmt.Println(`+----------+------------+------------------+`)
}
