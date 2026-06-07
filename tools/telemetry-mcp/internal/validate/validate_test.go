package validate

import (
	"strings"
	"testing"
)

// TestFilterValidation covers the contract's accept and reject vectors over the
// transit datasets (contracts/query_telemetry.tool.md).
func TestFilterValidation(t *testing.T) {
	tests := []struct {
		name      string
		dataset   string
		input     string
		shouldErr bool
		contains  string // substring expected in the error message
	}{
		// --- Accept vectors A1-A7 ---
		{name: "A1 snap numeric", dataset: "snap", input: "snap_distance_km > 0.5", shouldErr: false},
		{name: "A2 cycle numeric + bool", dataset: "cycle", input: "buses_stale > 10 AND duplicate_feed = false", shouldErr: false},
		{name: "A3 lerp numeric + string", dataset: "lerp", input: "pos_delta_km > 1.0 AND vehicle_id = 'v001'", shouldErr: false},
		{name: "A4 snap bool true", dataset: "snap", input: "is_stale = true", shouldErr: false},
		{name: "A5 snap timestamp vs date", dataset: "snap", input: "observation_utc > '2026-06-04'", shouldErr: false},
		{name: "A6 snap grouped predicate", dataset: "snap", input: "(snap_outcome = 'Moved' OR snap_outcome = 'Stale') AND raw_lat > 33.0", shouldErr: false},
		{name: "A7 cycle sidecar metric", dataset: "cycle", input: "sidecar_dropped_records > 0", shouldErr: false},

		// extra accept coverage: operators, whitespace, case-insensitive AND/OR/bool
		{name: "all operators <=", dataset: "snap", input: "snap_distance_km <= 5.0", shouldErr: false},
		{name: "inequality !=", dataset: "snap", input: "snap_outcome != 'Stale'", shouldErr: false},
		{name: "whitespace handling", dataset: "cycle", input: "  buses_stale  >  10  ", shouldErr: false},
		{name: "case insensitive and", dataset: "snap", input: "raw_lat > 33.0 and raw_lon < -84.0", shouldErr: false},
		{name: "case insensitive bool", dataset: "snap", input: "is_stale = FALSE", shouldErr: false},
		{name: "negative number", dataset: "snap", input: "bearing_deg > -1.5", shouldErr: false},

		// --- Reject vectors R2-R14 (R1 is ValidateDataset; tested separately) ---
		{name: "R2 iris column sepal.length", dataset: "snap", input: "sepal.length > 5", shouldErr: true, contains: "unknown column"},
		{name: "R3 dotted petal.length", dataset: "snap", input: "petal.length > 5", shouldErr: true, contains: "unknown column"},
		{name: "R4 dotted snap.outcome", dataset: "snap", input: "snap.outcome > 5", shouldErr: true, contains: "unknown column"},
		{name: "R5 cycle column on snap", dataset: "snap", input: "buses_stale > 10", shouldErr: true, contains: "unknown column"},
		{name: "R6 bool col with number", dataset: "snap", input: "is_stale = 1", shouldErr: true, contains: "expects bool"},
		{name: "R7 bool col with quoted", dataset: "snap", input: "is_stale = 'true'", shouldErr: true, contains: "expects bool"},
		{name: "R8 timestamp col with number", dataset: "snap", input: "observation_utc > 1234567", shouldErr: true, contains: "date string"},
		{name: "R9 full ISO timestamp colon", dataset: "snap", input: "observation_utc > '2026-06-04T12:00:00'", shouldErr: true, contains: "forbidden character"},
		{name: "R10 semicolon", dataset: "cycle", input: "buses_stale > 10; DROP TABLE x", shouldErr: true, contains: "forbidden character"},
		{name: "R11 forbidden keyword SELECT", dataset: "cycle", input: "SELECT * FROM cycle", shouldErr: true, contains: "forbidden keyword"},
		{name: "R12 comment marker", dataset: "snap", input: "vehicle_id = 'v001' -- x", shouldErr: true, contains: "forbidden comment"},
		{name: "R13 numeric col with string", dataset: "snap", input: "raw_lat = 'abc'", shouldErr: true, contains: "expects numeric"},
		{name: "R14 string col with number", dataset: "snap", input: "vehicle_id = 123", shouldErr: true, contains: "expects string"},

		// retained security guards over transit columns
		{name: "pipe char", dataset: "snap", input: "raw_lat > 5 | cat", shouldErr: true, contains: "forbidden character"},
		{name: "backtick char", dataset: "snap", input: "raw_lat > `cmd`", shouldErr: true, contains: "forbidden character"},
		{name: "dollar sign", dataset: "snap", input: "raw_lat > $VAR", shouldErr: true, contains: "forbidden character"},
		{name: "azure url", dataset: "snap", input: "vehicle_id = 'azure://blob'", shouldErr: true, contains: "forbidden data source"},
		{name: "http url", dataset: "snap", input: "vehicle_id = 'http://example'", shouldErr: true, contains: "forbidden data source"},
		{name: "unknown column generic", dataset: "snap", input: "password = 'secret'", shouldErr: true, contains: "unknown column"},

		// structural errors
		{name: "empty filter", dataset: "snap", input: "", shouldErr: true, contains: "required"},
		{name: "whitespace only", dataset: "snap", input: "   ", shouldErr: true},
		{name: "exceeds max length", dataset: "snap", input: strings.Repeat("a", 300), shouldErr: true, contains: "exceeds maximum length"},
		{name: "unterminated string", dataset: "snap", input: "vehicle_id = 'v001", shouldErr: true, contains: "unterminated string"},
		{name: "forbidden char in string", dataset: "snap", input: "vehicle_id = 'v@01'", shouldErr: true, contains: "forbidden character"},
		{name: "missing operator", dataset: "snap", input: "raw_lat 5.0", shouldErr: true, contains: "expected comparison operator"},
		{name: "missing literal", dataset: "snap", input: "raw_lat >", shouldErr: true, contains: "expected"},
		{name: "unclosed paren", dataset: "snap", input: "(raw_lat > 5.0", shouldErr: true, contains: "expected closing parenthesis"},
		{name: "invalid number", dataset: "snap", input: "raw_lat > 5.0.1", shouldErr: true, contains: "unexpected"},
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

// TestCanonicalForm verifies transit columns re-emit as bare snake_case identifiers
// (no dot-quoting), and that bool/timestamp literals canonicalize as expected.
func TestCanonicalForm(t *testing.T) {
	tests := []struct {
		dataset  string
		input    string
		expected string
	}{
		{dataset: "snap", input: "snap_distance_km > 0.5", expected: "snap_distance_km > 0.5"},
		{dataset: "cycle", input: "  buses_stale  >  10  ", expected: "buses_stale > 10"},
		{dataset: "lerp", input: "vehicle_id = 'v001'", expected: "vehicle_id = 'v001'"},
		{dataset: "snap", input: "is_stale = false", expected: "is_stale = false"},
		{dataset: "snap", input: "is_stale = FALSE", expected: "is_stale = false"},
		{dataset: "snap", input: "observation_utc > '2026-06-04'", expected: "observation_utc > '2026-06-04'"},
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

// TestValidateDataset covers reject vector R1 and the accept set.
func TestValidateDataset(t *testing.T) {
	for _, d := range []string{"snap", "lerp", "cycle"} {
		if err := ValidateDataset(d); err != nil {
			t.Errorf("expected %q to be accepted, got: %v", d, err)
		}
	}
	for _, d := range []string{"other", "iris", "SNAP", "", "snap "} {
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
