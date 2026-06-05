package main

import (
	"database/sql"
	"fmt"
	"os"
	"strings"

	_ "github.com/marcboeker/go-duckdb"
	"github.com/olekukonko/tablewriter"
)

func main() {
	if len(os.Args) < 2 {
		fmt.Println("Usage: go run main.go \"SELECT * FROM ...\"")
		os.Mkdir("Example:", 0755)
		fmt.Println("Usage: telemetry-tool \"SELECT count(*) FROM 'azure://...'\"")
		os.Exit(1)
	}

	query := os.Args[1]

	// 1. Fetch the connection string from the environment safely
	azureConnString := os.Getenv("AZURE_STORAGE_CONNECTION_STRING")
	if azureConnString == "" {
		fmt.Fprintln(os.Stderr, "Error: AZURE_STORAGE_CONNECTION_STRING environment variable is not set.")
		os.Exit(1)
	}

	// 2. Open an in-memory DuckDB connection
	db, err := sql.Open("duckdb", "")
	if err != nil {
		fmt.Fprintf(os.Stderr, "Failed to connect to DuckDB: %v\n", err)
		os.Exit(1)
	}
	defer db.Close()

	// 3. Bootstrapping: Auto-install/load Azure features and feed the credentials
	bootstrapQueries := []string{
		"INSTALL azure;",
		"LOAD azure;",
		fmt.Sprintf("SET azure_storage_connection_string = '%s';", strings.ReplaceAll(azureConnString, "'", "''")),
	}

	for _, bq := range bootstrapQueries {
		if _, err := db.Exec(bq); err != nil {
			fmt.Fprintf(os.Stderr, "Initialization Error during [%s]: %v\n", bq, err)
			os.Exit(1)
		}
	}

	// 4. Run the Agent's Query
	fmt.Println("Fetching and analyzing telemetry data from Azure Blob Storage...")
	rows, err := db.Query(query)
	if err != nil {
		fmt.Fprintf(os.Stderr, "SQL Query Error: %v\n", err)
		os.Exit(1)
	}
	defer rows.Close()

	// 5. Dynamic Table Rendering (Format outputs gracefully for LLM context windows)
	cols, err := rows.Columns()
	if err != nil {
		fmt.Fprintf(os.Stderr, "Failed to read columns: %v\n", err)
		os.Exit(1)
	}

	table := tablewriter.NewWriter(os.Stdout)
	table.Header(cols)

	// Scan arbitrary SQL results dynamically
	rawResult := make([][]byte, len(cols))
	dest := make([]interface{}, len(cols))
	for i := range rawResult {
		dest[i] = &rawResult[i]
	}

	for rows.Next() {
		err = rows.Scan(dest...)
		if err != nil {
			fmt.Fprintf(os.Stderr, "Failed to scan row: %v\n", err)
			os.Exit(1)
		}

		var rowValues []string
		for _, raw := range rawResult {
			if raw == nil {
				rowValues = append(rowValues, "NULL")
			} else {
				rowValues = append(rowValues, string(raw))
			}
		}
		if err := table.Append(rowValues); err != nil {
			fmt.Fprintf(os.Stderr, "Failed to append row: %v\n", err)
			os.Exit(1)
		}
	}

	// Flush the clean ascii table to stdout for the agent to scrape
	if err := table.Render(); err != nil {
		fmt.Fprintf(os.Stderr, "Failed to render table: %v\n", err)
		os.Exit(1)
	}
}
