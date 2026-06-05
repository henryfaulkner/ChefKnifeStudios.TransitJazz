package validate

import (
	"strings"
	"testing"
)

func TestFilterValidation(t *testing.T) {
	tests := []struct {
		name      string
		input     string
		shouldErr bool
		contains  string // for error messages
	}{
		// Valid cases
		{
			name:      "simple numeric comparison",
			input:     "petal.length > 5.0",
			shouldErr: false,
		},
		{
			name:      "simple string comparison",
			input:     "variety = 'setosa'",
			shouldErr: false,
		},
		{
			name:      "AND conjunction",
			input:     "petal.length > 5.0 AND variety = 'setosa'",
			shouldErr: false,
		},
		{
			name:      "OR conjunction",
			input:     "petal.length > 5.0 OR variety = 'versicolor'",
			shouldErr: false,
		},
		{
			name:      "parenthesized expression",
			input:     "(petal.length > 5.0 AND sepal.width < 3.0)",
			shouldErr: false,
		},
		{
			name:      "mixed AND/OR with parens",
			input:     "(petal.length > 5.0 OR variety = 'setosa') AND sepal.length > 4.0",
			shouldErr: false,
		},
		{
			name:      "negative numbers",
			input:     "sepal.length > -1.5",
			shouldErr: false,
		},
		{
			name:      "all comparison operators",
			input:     "petal.length <= 5.0",
			shouldErr: false,
		},
		{
			name:      "inequal operator",
			input:     "variety != 'setosa'",
			shouldErr: false,
		},
		{
			name:      "whitespace handling",
			input:     "  petal.length   >   5.0  ",
			shouldErr: false,
		},
		{
			name:      "case insensitive AND/OR",
			input:     "petal.length > 5.0 and variety = 'setosa'",
			shouldErr: false,
		},

		// Invalid cases
		{
			name:      "empty filter",
			input:     "",
			shouldErr: true,
			contains:  "required",
		},
		{
			name:      "whitespace only",
			input:     "   ",
			shouldErr: true,
		},
		{
			name:      "exceeds max length",
			input:     strings.Repeat("a", 300),
			shouldErr: true,
			contains:  "exceeds maximum length",
		},
		{
			name:      "statement separator semicolon",
			input:     "petal.length > 5; DROP TABLE x",
			shouldErr: true,
			contains:  "forbidden character",
		},
		{
			name:      "SQL comment --",
			input:     "petal.length > 5 -- comment",
			shouldErr: true,
			contains:  "forbidden comment",
		},
		{
			name:      "SQL comment /* */",
			input:     "petal.length > 5 /* comment */",
			shouldErr: true,
			contains:  "forbidden comment",
		},
		{
			name:      "forbidden keyword SELECT",
			input:     "SELECT * FROM iris",
			shouldErr: true,
			contains:  "forbidden keyword",
		},
		{
			name:      "forbidden keyword UNION",
			input:     "petal.length > 5 UNION SELECT * FROM users",
			shouldErr: true,
			contains:  "forbidden keyword",
		},
		{
			name:      "path reference",
			input:     "file > 'C:\\Users\\data'",
			shouldErr: true,
			contains:  "unknown column", // "file" is not an allowed column
		},
		{
			name:      "Azure URL reference",
			input:     "data_source = 'azure://blob/file'",
			shouldErr: true,
			contains:  "forbidden data source",
		},
		{
			name:      "HTTP URL reference",
			input:     "url = 'http://example.com'",
			shouldErr: true,
			contains:  "forbidden data source",
		},
		{
			name:      "shell pipe character",
			input:     "petal.length > 5 | cat",
			shouldErr: true,
			contains:  "forbidden character",
		},
		{
			name:      "backtick character",
			input:     "petal.length > `command`",
			shouldErr: true,
			contains:  "forbidden character",
		},
		{
			name:      "dollar sign (shell variable)",
			input:     "petal.length > $VAR",
			shouldErr: true,
			contains:  "forbidden character",
		},
		{
			name:      "unknown column",
			input:     "password = 'secret'",
			shouldErr: true,
			contains:  "unknown column",
		},
		{
			name:      "invalid string literal - unterminated",
			input:     "variety = 'setosa",
			shouldErr: true,
			contains:  "unterminated string",
		},
		{
			name:      "invalid string literal - forbidden character in string",
			input:     "variety = 'set@sa'",
			shouldErr: true,
			contains:  "forbidden character",
		},
		{
			name:      "type mismatch - string column with number",
			input:     "variety = 123",
			shouldErr: true,
			contains:  "expects string value",
		},
		{
			name:      "type mismatch - numeric column with string",
			input:     "petal.length = 'five'",
			shouldErr: true,
			contains:  "expects numeric value",
		},
		{
			name:      "missing operator",
			input:     "petal.length 5.0",
			shouldErr: true,
			contains:  "expected comparison operator",
		},
		{
			name:      "missing literal",
			input:     "petal.length >",
			shouldErr: true,
			contains:  "expected",
		},
		{
			name:      "unclosed parenthesis",
			input:     "(petal.length > 5.0",
			shouldErr: true,
			contains:  "expected closing parenthesis",
		},
		{
			name:      "invalid number",
			input:     "petal.length > 5.0.1",
			shouldErr: true,
			contains:  "unexpected",
		},
	}

	for _, tt := range tests {
		t.Run(tt.name, func(t *testing.T) {
			result, err := Filter(tt.input)

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

func TestCanonicalForm(t *testing.T) {
	tests := []struct {
		input    string
		expected string
	}{
		{
			input:    "petal.length > 5.0",
			expected: `"petal.length" > 5.0`,
		},
		{
			input:    "  petal.length   >   5.0  ",
			expected: `"petal.length" > 5.0`,
		},
		{
			input:    "variety = 'setosa'",
			expected: "variety = 'setosa'",
		},
	}

	for _, tt := range tests {
		t.Run(tt.input, func(t *testing.T) {
			result, err := Filter(tt.input)
			if err != nil {
				t.Fatalf("unexpected error: %v", err)
			}
			if result != tt.expected {
				t.Errorf("expected %q, got %q", tt.expected, result)
			}
		})
	}
}
