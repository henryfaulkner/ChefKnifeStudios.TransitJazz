package validate

import (
	"strings"
	"testing"
)

// TestFilterValidation covers the contract's accept and reject vectors over the
// telemetry dataset (specs/038-telemetry-denormalization/contracts/query-validator.md).
func TestFilterValidation(t *testing.T) {
	tests := []struct {
		name      string
		dataset   string
		input     string
		shouldErr bool
		contains  string // substring expected in the error message
	}{
		// --- Accept vectors ---
		{name: "event_type PerCityCycle", dataset: "telemetry", input: "event_type = 'PerCityCycle'", shouldErr: false},
		{name: "event_type FullCycle", dataset: "telemetry", input: "event_type = 'FullCycle'", shouldErr: false},
		{name: "health_ok false", dataset: "telemetry", input: "health_ok = false", shouldErr: false},
		{name: "numeric + bool AND", dataset: "telemetry", input: "vehicles_processed > 0 AND health_ok = true", shouldErr: false},
		{name: "city_name string", dataset: "telemetry", input: "city_name = 'MARTA'", shouldErr: false},
		{name: "numeric OR", dataset: "telemetry", input: "tones_emitted >= 5 OR feed_freshness_seconds > 60", shouldErr: false},
		{name: "timestamp vs date", dataset: "telemetry", input: "observation_utc > '2026-07-11'", shouldErr: false},
		{name: "grouping + numeric", dataset: "telemetry", input: "(event_type = 'FullCycle' OR event_type = 'PerCityCycle') AND gc_heap_bytes > 100000000", shouldErr: false},
		{name: "new cache-size numeric columns", dataset: "telemetry", input: "route_index_size > 0 AND crossing_baseline_cache_size >= 0", shouldErr: false},
		{name: "crossing suppression columns (feature 045)", dataset: "telemetry", input: "crossings_suppressed_first_seen >= 0 AND crossings_suppressed_delta_leq0 > 0 AND crossings_suppressed_teleport >= 0 AND crossings_suppressed_transfer >= 0", shouldErr: false},
		// P0-G1 (feature 051, contract telemetry-observability C2)
		{name: "batch_wire_bytes greater than", dataset: "telemetry", input: "batch_wire_bytes > 100000", shouldErr: false},
		{name: "batch_wire_bytes greater-or-equal zero", dataset: "telemetry", input: "batch_wire_bytes >= 0", shouldErr: false},

		// extra accept coverage: operators, whitespace, case-insensitive AND/OR/bool
		{name: "all operators <=", dataset: "telemetry", input: "time_taken_seconds <= 5.0", shouldErr: false},
		{name: "inequality !=", dataset: "telemetry", input: "event_type != 'FullCycle'", shouldErr: false},
		{name: "whitespace handling", dataset: "telemetry", input: "  vehicles_processed  >  10  ", shouldErr: false},
		{name: "case insensitive and", dataset: "telemetry", input: "vehicles_processed > 0 and tones_emitted > 0", shouldErr: false},
		{name: "case insensitive bool", dataset: "telemetry", input: "health_ok = FALSE", shouldErr: false},
		{name: "negative number", dataset: "telemetry", input: "feed_freshness_seconds > -1.5", shouldErr: false},

		// --- Reject vectors ---
		{name: "retired snap column", dataset: "telemetry", input: "snap_distance_km > 0.5", shouldErr: true, contains: "unknown column"},
		{name: "retired lerp column", dataset: "telemetry", input: "pos_delta_km > 1.0", shouldErr: true, contains: "unknown column"},
		{name: "dropped dead column", dataset: "telemetry", input: "last_update_cache_size > 0", shouldErr: true, contains: "unknown column"},
		{name: "bool col with number", dataset: "telemetry", input: "health_ok = 1", shouldErr: true, contains: "expects bool"},
		{name: "bool col with quoted", dataset: "telemetry", input: "health_ok = 'true'", shouldErr: true, contains: "expects bool"},
		{name: "string col unquoted", dataset: "telemetry", input: "event_type = PerCityCycle", shouldErr: true},
		{name: "numeric col with string", dataset: "telemetry", input: "tones_emitted = 'five'", shouldErr: true, contains: "expects numeric"},
		{name: "full ISO timestamp colon", dataset: "telemetry", input: "observation_utc > '2026-07-11T00:00:00'", shouldErr: true, contains: "forbidden character"},
		{name: "dotted identifier", dataset: "telemetry", input: "event_type.value = 'x'", shouldErr: true, contains: "unexpected character"},
		{name: "forbidden keyword SELECT", dataset: "telemetry", input: "SELECT * FROM telemetry", shouldErr: true, contains: "forbidden keyword"},
		{name: "semicolon", dataset: "telemetry", input: "event_type = 'x'; DROP TABLE t", shouldErr: true, contains: "forbidden character"},
		{name: "comment marker", dataset: "telemetry", input: "city_name = 'MARTA' -- x", shouldErr: true, contains: "forbidden comment"},
		{name: "string col with number", dataset: "telemetry", input: "city_name = 123", shouldErr: true, contains: "expects string"},

		// retained security guards over telemetry columns
		{name: "pipe char", dataset: "telemetry", input: "vehicles_processed > 5 | cat", shouldErr: true, contains: "forbidden character"},
		{name: "backtick char", dataset: "telemetry", input: "vehicles_processed > `cmd`", shouldErr: true, contains: "forbidden character"},
		{name: "dollar sign", dataset: "telemetry", input: "vehicles_processed > $VAR", shouldErr: true, contains: "forbidden character"},
		{name: "azure url", dataset: "telemetry", input: "city_name = 'azure://blob'", shouldErr: true, contains: "forbidden data source"},
		{name: "http url", dataset: "telemetry", input: "city_name = 'http://example'", shouldErr: true, contains: "forbidden data source"},
		{name: "unknown column generic", dataset: "telemetry", input: "password = 'secret'", shouldErr: true, contains: "unknown column"},
		{name: "near-miss suppression column name", dataset: "telemetry", input: "crossings_suppressed_deltaleq0 > 0", shouldErr: true, contains: "unknown column"},
		// P0-G1 (feature 051, contract telemetry-observability C2)
		{name: "batch_wire_bytes with quoted string", dataset: "telemetry", input: "batch_wire_bytes = 'x'", shouldErr: true, contains: "expects numeric"},
		{name: "batch_wire_bytes near-miss unknown column", dataset: "telemetry", input: "batch_wire_byte > 1", shouldErr: true, contains: "unknown column"},

		// structural errors
		{name: "empty filter", dataset: "telemetry", input: "", shouldErr: true, contains: "required"},
		{name: "whitespace only", dataset: "telemetry", input: "   ", shouldErr: true},
		{name: "exceeds max length", dataset: "telemetry", input: strings.Repeat("a", 300), shouldErr: true, contains: "exceeds maximum length"},
		{name: "unterminated string", dataset: "telemetry", input: "city_name = 'MARTA", shouldErr: true, contains: "unterminated string"},
		{name: "forbidden char in string", dataset: "telemetry", input: "city_name = 'MAR@TA'", shouldErr: true, contains: "forbidden character"},
		{name: "missing operator", dataset: "telemetry", input: "vehicles_processed 5", shouldErr: true, contains: "expected comparison operator"},
		{name: "missing literal", dataset: "telemetry", input: "vehicles_processed >", shouldErr: true, contains: "expected"},
		{name: "unclosed paren", dataset: "telemetry", input: "(vehicles_processed > 5", shouldErr: true, contains: "expected closing parenthesis"},
		{name: "invalid number", dataset: "telemetry", input: "vehicles_processed > 5.0.1", shouldErr: true, contains: "unexpected"},
	}

	for _, tt := range tests {
		t.Run(tt.name, func(t *testing.T) {
			result, err := Filter(tt.dataset, tt.input)

			if tt.shouldErr {
				if err == nil {
					t.Errorf("expected error, got none; result: %q", result)
				} else if tt.contains != "" && !strings.Contains(err.Error(), tt.contains) {
					t.Errorf("error message should contain %q, got: %v", tt.contains, err)
				}
			} else {
				if err != nil {
					t.Errorf("unexpected error: %v", err)
				}
				if result == "" {
					t.Errorf("expected non-empty canonical result")
				}
			}
		})
	}
}

// TestCanonicalForm verifies telemetry columns re-emit as bare snake_case identifiers
// (no dot-quoting), and that bool/timestamp literals canonicalize as expected.
func TestCanonicalForm(t *testing.T) {
	tests := []struct {
		dataset  string
		input    string
		expected string
	}{
		{dataset: "telemetry", input: "tones_emitted > 5", expected: "tones_emitted > 5"},
		{dataset: "telemetry", input: "  vehicles_processed  >  10  ", expected: "vehicles_processed > 10"},
		{dataset: "telemetry", input: "city_name = 'MARTA'", expected: "city_name = 'MARTA'"},
		{dataset: "telemetry", input: "health_ok = false", expected: "health_ok = false"},
		{dataset: "telemetry", input: "health_ok = FALSE", expected: "health_ok = false"},
		{dataset: "telemetry", input: "observation_utc > '2026-07-11'", expected: "observation_utc > '2026-07-11'"},
	}

	for _, tt := range tests {
		t.Run(tt.input, func(t *testing.T) {
			result, err := Filter(tt.dataset, tt.input)
			if err != nil {
				t.Fatalf("unexpected error: %v", err)
			}
			if result != tt.expected {
				t.Errorf("expected %q, got %q", tt.expected, result)
			}
		})
	}
}

// TestValidateDataset covers the single-dataset accept/reject set (FR-025).
func TestValidateDataset(t *testing.T) {
	if err := ValidateDataset("telemetry"); err != nil {
		t.Errorf("expected %q to be accepted, got: %v", "telemetry", err)
	}
	for _, d := range []string{"snap", "lerp", "cycle", "other", "iris", "TELEMETRY", "", "telemetry "} {
		if err := ValidateDataset(d); err == nil {
			t.Errorf("expected %q to be rejected", d)
		}
	}
}

// TestValidateDate covers the contract's date argument vectors.
func TestValidateDate(t *testing.T) {
	tests := []struct {
		date      string
		shouldErr bool
	}{
		{"2026-06-04", false},
		{"2026-6-4", true},             // not zero-padded
		{"2026-13-40", true},           // not a real calendar date
		{"../secret", true},            // regex fail; cannot redirect source
		{"2026-06-04/*.parquet", true}, // regex fail
		{"", true},
	}

	for _, tt := range tests {
		t.Run(tt.date, func(t *testing.T) {
			got, err := ValidateDate(tt.date)
			if tt.shouldErr {
				if err == nil {
					t.Errorf("expected error for %q, got %q", tt.date, got)
				}
			} else {
				if err != nil {
					t.Errorf("unexpected error for %q: %v", tt.date, err)
				}
				if got != tt.date {
					t.Errorf("expected date returned unchanged %q, got %q", tt.date, got)
				}
			}
		})
	}
}
