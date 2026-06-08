package validate

import (
	"fmt"
	"regexp"
	"strings"
	"time"
	"unicode"
)

const (
	MaxFilterLength = 256
)

// validDatasets is the literal set of dataset names this bridge serves. A dataset
// outside this set is rejected before any filter is parsed (FR-001).
var validDatasets = map[string]bool{
	"snap":  true,
	"lerp":  true,
	"cycle": true,
}

// dateRegex enforces a strict, zero-padded calendar date. It is structurally
// incapable of carrying path separators, globs, or SQL (FR-009).
var dateRegex = regexp.MustCompile(`^\d{4}-\d{2}-\d{2}$`)

// valueKind classifies the literal a column may be compared against.
type valueKind int

const (
	kindNumeric valueKind = iota
	kindString
	kindTimestamp
	kindBool
)

// datasetColumns maps each dataset to its column -> value-kind contract. These
// mirror specs/014-transit-datasets/data-model.md exactly (frozen feature-013
// snake_case contract). A column belongs to exactly one value kind within a
// dataset; all numeric parquet types (DOUBLE/INT32/INT64 + nullable variants)
// collapse to kindNumeric.
var datasetColumns = map[string]map[string]valueKind{
	"snap": {
		"cycle_id":          kindString,
		"observation_utc":   kindTimestamp,
		"vehicle_id":        kindString,
		"route_id":          kindString,
		"snap_outcome":      kindString,
		"raw_lat":           kindNumeric,
		"raw_lon":           kindNumeric,
		"snapped_lat":       kindNumeric,
		"snapped_lon":       kindNumeric,
		"snap_distance_km":  kindNumeric,
		"snap_index":        kindNumeric,
		"route_point_count": kindNumeric,
		"speed_mps":         kindNumeric,
		"bearing_deg":       kindNumeric,
		"is_stale":          kindBool,
	},
	"lerp": {
		"cycle_id":              kindString,
		"observation_utc":       kindTimestamp,
		"vehicle_id":            kindString,
		"prior_route_id":        kindString,
		"prior_snapped_lat":     kindNumeric,
		"prior_snapped_lon":     kindNumeric,
		"prior_observation_utc": kindTimestamp,
		"prior_speed_mps":       kindNumeric,
		"prior_bearing_deg":     kindNumeric,
		"pos_delta_km":          kindNumeric,
		"speed_delta":           kindNumeric,
		"bearing_delta":         kindNumeric,
		"time_delta_sec":        kindNumeric,
	},
	"cycle": {
		"cycle_id":                    kindString,
		"cycle_start_utc":             kindTimestamp,
		"cycle_end_utc":               kindTimestamp,
		"cycle_execution_seconds":     kindNumeric,
		"buses_processed":             kindNumeric,
		"buses_moved":                 kindNumeric,
		"buses_unchanged":             kindNumeric,
		"buses_stationary":            kindNumeric,
		"buses_stale":                 kindNumeric,
		"buses_skipped_no_route_id":   kindNumeric,
		"buses_skipped_unknown_route": kindNumeric,
		"feed_header_ts":              kindNumeric,
		"duplicate_feed":              kindBool,
		"active_route_ids":            kindString,
		"active_vehicle_ids":          kindString,
		"last_update_cache_size":      kindNumeric,
		"vehicle_state_cache_size":    kindNumeric,
		"sidecar_buffer_occupancy":    kindNumeric,
		"sidecar_dropped_records":     kindNumeric,
		"sidecar_persist_failures":    kindNumeric,
	},
}

var (
	forbiddenKeywords = map[string]bool{
		"WHERE":   true,
		"SELECT":  true,
		"FROM":    true,
		"UNION":   true,
		"JOIN":    true,
		"INSERT":  true,
		"UPDATE":  true,
		"DELETE":  true,
		"DROP":    true,
		"ATTACH":  true,
		"COPY":    true,
		"PRAGMA":  true,
		"INSTALL": true,
		"LOAD":    true,
		"SET":     true,
	}

	forbiddenChars = map[rune]bool{
		';':  true,
		'`':  true,
		'$':  true,
		'|':  true,
		'&':  true,
		'\\': true,
	}
)

type Token struct {
	Type  string // "column", "number", "string", "bool", "op", "and", "or", "lparen", "rparen"
	Value string
}

// ValidateDataset rejects any dataset name not in the literal set {snap,lerp,cycle}.
// It must be called before the filter is parsed so an unknown dataset never reaches
// SQL (FR-001).
func ValidateDataset(dataset string) error {
	if !validDatasets[dataset] {
		return fmt.Errorf("unknown dataset %q: must be one of snap, lerp, cycle", dataset)
	}
	return nil
}

// ValidateDate enforces a strict YYYY-MM-DD format and a real calendar date,
// returning the validated date unchanged. The value is structurally incapable of
// redirecting the data source (FR-009).
func ValidateDate(date string) (string, error) {
	if !dateRegex.MatchString(date) {
		return "", fmt.Errorf("invalid date %q: must match YYYY-MM-DD", date)
	}
	if _, err := time.Parse("2006-01-02", date); err != nil {
		return "", fmt.Errorf("invalid date %q: not a real calendar date", date)
	}
	return date, nil
}

// Filter validates and canonicalizes a filter condition over the named dataset's
// columns. The dataset must already have passed ValidateDataset.
func Filter(dataset, input string) (string, error) {
	columns, ok := datasetColumns[dataset]
	if !ok {
		return "", fmt.Errorf("unknown dataset %q: must be one of snap, lerp, cycle", dataset)
	}

	if input == "" {
		return "", fmt.Errorf("filter is required")
	}

	trimmed := strings.TrimSpace(input)
	if trimmed == "" {
		return "", fmt.Errorf("filter cannot be empty or whitespace only")
	}

	if len(trimmed) > MaxFilterLength {
		return "", fmt.Errorf("filter exceeds maximum length of %d characters", MaxFilterLength)
	}

	// Quick checks for obviously forbidden content (comments, separators, etc.)
	if strings.Contains(trimmed, "--") || strings.Contains(trimmed, "/*") || strings.Contains(trimmed, "*/") {
		return "", fmt.Errorf("filter contains forbidden comment marker")
	}

	if strings.ContainsRune(trimmed, '#') {
		return "", fmt.Errorf("filter contains forbidden comment marker: #")
	}

	if strings.ContainsRune(trimmed, ';') {
		return "", fmt.Errorf("filter contains forbidden character: ;")
	}

	// Check for URL-like or path-like patterns
	lowerInput := strings.ToLower(trimmed)
	for _, pattern := range []string{"azure://", "http://", "https://", "file:", ".."} {
		if strings.Contains(lowerInput, pattern) {
			return "", fmt.Errorf("filter contains forbidden data source reference: %s", pattern)
		}
	}

	// Check for forbidden keywords (whole-word match only, skip quoted strings)
	upperInput := strings.ToUpper(trimmed)
	inQuote := false
	for keyword := range forbiddenKeywords {
		// Look for keyword as a complete word, but skip quoted regions
		for i := 0; i < len(upperInput); {
			if upperInput[i] == '\'' {
				inQuote = !inQuote
				i++
				continue
			}
			if inQuote {
				i++
				continue
			}
			// Check if keyword starts here
			if i+len(keyword) <= len(upperInput) && upperInput[i:i+len(keyword)] == keyword {
				// Check word boundaries before and after
				okBefore := i == 0 || !isIdentifierChar(rune(upperInput[i-1]))
				okAfter := i+len(keyword) >= len(upperInput) || !isIdentifierChar(rune(upperInput[i+len(keyword)]))
				if okBefore && okAfter {
					return "", fmt.Errorf("filter contains forbidden keyword: %s", keyword)
				}
			}
			i++
		}
	}

	// Tokenize - this will do detailed validation of each token including remaining forbidden chars
	tokens, err := tokenize(trimmed, columns)
	if err != nil {
		return "", err
	}

	// Parse
	parser := &parser{tokens: tokens, pos: 0, columns: columns}
	canonical, err := parser.parsePredicate()
	if err != nil {
		return "", err
	}

	if parser.pos < len(parser.tokens) {
		return "", fmt.Errorf("unexpected tokens after predicate")
	}

	return canonical, nil
}

type parser struct {
	tokens  []*Token
	pos     int
	columns map[string]valueKind
}

func (p *parser) parsePredicate() (string, error) {
	return p.parseOr()
}

func (p *parser) parseOr() (string, error) {
	left, err := p.parseAnd()
	if err != nil {
		return "", err
	}

	for p.peek() != nil && p.peek().Type == "or" {
		p.consume()
		right, err := p.parseAnd()
		if err != nil {
			return "", err
		}
		left = left + " OR " + right
	}

	return left, nil
}

func (p *parser) parseAnd() (string, error) {
	left, err := p.parseComparison()
	if err != nil {
		return "", err
	}

	for p.peek() != nil && p.peek().Type == "and" {
		p.consume()
		right, err := p.parseComparison()
		if err != nil {
			return "", err
		}
		left = left + " AND " + right
	}

	return left, nil
}

func (p *parser) parseComparison() (string, error) {
	if p.peek() == nil {
		return "", fmt.Errorf("expected comparison or grouped predicate")
	}

	// Handle parenthesized subexpression
	if p.peek().Type == "lparen" {
		p.consume()
		expr, err := p.parsePredicate()
		if err != nil {
			return "", err
		}
		if p.peek() == nil || p.peek().Type != "rparen" {
			return "", fmt.Errorf("expected closing parenthesis")
		}
		p.consume()
		return "(" + expr + ")", nil
	}

	// Parse column op literal
	if p.peek().Type != "column" {
		return "", fmt.Errorf("expected column name, got %s", p.peek().Type)
	}
	column := p.consume().Value

	if p.peek() == nil || p.peek().Type != "op" {
		return "", fmt.Errorf("expected comparison operator after column")
	}
	op := p.consume().Value

	if p.peek() == nil {
		return "", fmt.Errorf("expected literal after operator")
	}

	literal := p.peek()
	if literal.Type != "number" && literal.Type != "string" && literal.Type != "bool" {
		return "", fmt.Errorf("expected number, string, or bool literal, got %s", literal.Type)
	}

	// Type check: the literal kind must match the column's value kind.
	kind := p.columns[column]
	switch kind {
	case kindNumeric:
		if literal.Type != "number" {
			return "", fmt.Errorf("column %s expects numeric value, got %s", column, literal.Type)
		}
	case kindString:
		if literal.Type != "string" {
			return "", fmt.Errorf("column %s expects string value, got %s", column, literal.Type)
		}
	case kindTimestamp:
		if literal.Type != "string" {
			return "", fmt.Errorf("column %s expects a date string literal, got %s", column, literal.Type)
		}
	case kindBool:
		if literal.Type != "bool" {
			return "", fmt.Errorf("column %s expects bool literal true/false, got %s", column, literal.Type)
		}
	}

	p.consume()

	// Transit columns are bare snake_case identifiers; no quoting is needed.
	return column + " " + op + " " + literal.Value, nil
}

func (p *parser) peek() *Token {
	if p.pos < len(p.tokens) {
		return p.tokens[p.pos]
	}
	return nil
}

func (p *parser) consume() *Token {
	token := p.tokens[p.pos]
	p.pos++
	return token
}

func tokenize(input string, columns map[string]valueKind) ([]*Token, error) {
	var tokens []*Token
	i := 0

	for i < len(input) {
		// Skip whitespace
		if unicode.IsSpace(rune(input[i])) {
			i++
			continue
		}

		// Single-character tokens
		if input[i] == '(' {
			tokens = append(tokens, &Token{Type: "lparen", Value: "("})
			i++
			continue
		}
		if input[i] == ')' {
			tokens = append(tokens, &Token{Type: "rparen", Value: ")"})
			i++
			continue
		}

		// String literals: 'quoted-string'
		if input[i] == '\'' {
			str, endPos, err := parseStringLiteral(input, i)
			if err != nil {
				return nil, err
			}
			tokens = append(tokens, &Token{Type: "string", Value: str})
			i = endPos
			continue
		}

		// Check for forbidden shell/command chars before treating < > = as operators
		if forbiddenChars[rune(input[i])] {
			return nil, fmt.Errorf("filter contains forbidden character: %c", input[i])
		}

		// Operators: <=, >=, !=, <, >, =
		if i+1 < len(input) {
			two := input[i : i+2]
			if two == "<=" || two == ">=" || two == "!=" {
				tokens = append(tokens, &Token{Type: "op", Value: two})
				i += 2
				continue
			}
		}
		if input[i] == '<' || input[i] == '>' || input[i] == '=' {
			tokens = append(tokens, &Token{Type: "op", Value: string(input[i])})
			i++
			continue
		}

		// Numbers (including negative): -?\d+(\.\d+)?
		if input[i] == '-' || (input[i] >= '0' && input[i] <= '9') {
			num, endPos, err := parseNumber(input, i)
			if err != nil {
				return nil, err
			}
			tokens = append(tokens, &Token{Type: "number", Value: num})
			i = endPos
			continue
		}

		// Identifiers (column names, AND/OR, or bool literals)
		if isIdentifierStart(rune(input[i])) {
			ident, endPos := parseIdentifier(input, i)
			upper := strings.ToUpper(ident)
			if upper == "AND" {
				tokens = append(tokens, &Token{Type: "and", Value: "AND"})
			} else if upper == "OR" {
				tokens = append(tokens, &Token{Type: "or", Value: "OR"})
			} else if upper == "TRUE" {
				tokens = append(tokens, &Token{Type: "bool", Value: "true"})
			} else if upper == "FALSE" {
				tokens = append(tokens, &Token{Type: "bool", Value: "false"})
			} else if _, ok := columns[ident]; ok {
				tokens = append(tokens, &Token{Type: "column", Value: ident})
			} else {
				return nil, fmt.Errorf("unknown column: %s", ident)
			}
			i = endPos
			continue
		}

		// Catch any other character as forbidden
		if forbiddenChars[rune(input[i])] {
			return nil, fmt.Errorf("filter contains forbidden character: %c", input[i])
		}

		return nil, fmt.Errorf("unexpected character at position %d: %c", i, input[i])
	}

	return tokens, nil
}

func parseStringLiteral(input string, start int) (string, int, error) {
	// input[start] == '\''
	i := start + 1
	var result strings.Builder
	result.WriteRune('\'')

	for i < len(input) {
		if input[i] == '\'' {
			// Check for escaped quote ''
			if i+1 < len(input) && input[i+1] == '\'' {
				result.WriteRune('\'')
				result.WriteRune('\'')
				i += 2
				continue
			}
			// End of string
			result.WriteRune('\'')
			return result.String(), i + 1, nil
		}

		ch := rune(input[i])
		// Only allow safe characters: letters, digits, spaces, -, _
		if !isStringCharValid(ch) {
			return "", 0, fmt.Errorf("string literal contains forbidden character: %c", ch)
		}

		result.WriteRune(ch)
		i++
	}

	return "", 0, fmt.Errorf("unterminated string literal starting at position %d", start)
}

func isStringCharValid(ch rune) bool {
	// Only alphanumeric, space, dash, and underscore are allowed in string literals.
	// Note: ':' and 'T' are NOT allowed, so timestamp comparisons are date-granularity
	// only (e.g. '2026-06-04'); full ISO timestamp literals are a deliberate non-goal.
	return (ch >= 'a' && ch <= 'z') ||
		(ch >= 'A' && ch <= 'Z') ||
		(ch >= '0' && ch <= '9') ||
		ch == ' ' || ch == '-' || ch == '_'
	// All other characters (including @, #, :, etc.) are forbidden
}

func parseNumber(input string, start int) (string, int, error) {
	i := start
	if input[i] == '-' {
		i++
	}

	// At least one digit
	if i >= len(input) || input[i] < '0' || input[i] > '9' {
		return "", 0, fmt.Errorf("invalid number at position %d", start)
	}

	for i < len(input) && input[i] >= '0' && input[i] <= '9' {
		i++
	}

	// Optional decimal part
	if i < len(input) && input[i] == '.' {
		i++
		if i >= len(input) || input[i] < '0' || input[i] > '9' {
			return "", 0, fmt.Errorf("invalid decimal number at position %d", start)
		}
		for i < len(input) && input[i] >= '0' && input[i] <= '9' {
			i++
		}
	}

	return input[start:i], i, nil
}

func parseIdentifier(input string, start int) (string, int) {
	i := start
	for i < len(input) && isIdentifierChar(rune(input[i])) {
		i++
	}
	return input[start:i], i
}

func isIdentifierStart(ch rune) bool {
	return (ch >= 'a' && ch <= 'z') || (ch >= 'A' && ch <= 'Z') || ch == '_'
}

func isIdentifierChar(ch rune) bool {
	// '.' is deliberately NOT an identifier char: it kills dotted-path identifiers
	// (e.g. snap.outcome, petal.length) so they are rejected as unknown columns
	// rather than parsed as struct-field access.
	return (ch >= 'a' && ch <= 'z') || (ch >= 'A' && ch <= 'Z') || (ch >= '0' && ch <= '9') || ch == '_'
}
