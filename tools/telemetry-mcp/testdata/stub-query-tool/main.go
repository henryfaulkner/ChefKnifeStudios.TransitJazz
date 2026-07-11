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

	// Check that the query references the telemetry day-partitioned layout (mirrors
	// the constant source template the bridge assembles: {StorageURI}/dt={date}/*.parquet).
	lower := strings.ToLower(query)
	if !strings.Contains(lower, "telemetry") || !strings.Contains(lower, "dt=") {
		fmt.Fprintln(os.Stderr, "Query validation error: telemetry dataset reference missing")
		os.Exit(1)
	}

	// Return a telemetry-shaped mock result table.
	fmt.Println(`+----------+-------------+------------------+`)
	fmt.Println(`| event_id | event_type  | observation_utc  |`)
	fmt.Println(`+----------+-------------+------------------+`)
	fmt.Println(`| e-001    | FullCycle   | 2026-06-04       |`)
	fmt.Println(`+----------+-------------+------------------+`)
}
