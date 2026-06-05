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

	// Check if the query looks reasonable (contains iris or expected table)
	if !strings.Contains(strings.ToLower(query), "iris") &&
		!strings.Contains(strings.ToLower(query), "petal") &&
		!strings.Contains(strings.ToLower(query), "sepal") {
		fmt.Fprintln(os.Stderr, "Query validation error: dataset reference missing")
		os.Exit(1)
	}

	// Return a mock result table
	fmt.Println(`+----------+----------+----------+----------+----------+`)
	fmt.Println(`| count    | species  |`)
	fmt.Println(`+----------+----------+`)
	fmt.Println(`| 42       | setosa   |`)
	fmt.Println(`+----------+----------+`)
}
