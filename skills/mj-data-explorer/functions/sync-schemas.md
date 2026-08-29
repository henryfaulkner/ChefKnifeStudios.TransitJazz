<!-- last verified: 2026-06-07 -->

# Function: Sync Schemas

Re-derive this skill's schema references from the actual code so they stay true to
reality. You were routed here because the user added a new telemetry dataset, added or
removed columns, or changed a column's type/value-kind, and the skill's docs need to
catch up.

The skill's reference files are a *cache* of a contract that lives in the repo. This
function refreshes that cache from the source of truth and bumps the `last verified`
dates.

## The source of truth (in priority order)

The validator is what the tool **actually enforces**, so it wins any disagreement:

1. **`tools/telemetry-mcp/internal/validate/validate.go`** — AUTHORITATIVE.
   - `validDatasets` — the literal set of dataset names the tool serves.
   - `datasetColumns` — per-dataset `column -> valueKind` map. This is the real
     allow-list. `kindNumeric` / `kindString` / `kindTimestamp` / `kindBool` map
     directly to the schema reference's value kinds.
   - `forbiddenKeywords`, `forbiddenChars`, `isStringCharValid`, `isIdentifierChar`,
     `MaxFilterLength`, `dateRegex` — the grammar rules the query guide documents.
2. **`tools/telemetry-mcp/main.go`** — the tool description + per-arg descriptions
   (dataset list, examples). Should agree with `validate.go`.
3. **`specs/01X-.../contracts/parquet-schemas.md`** — parquet types (`DOUBLE`,
   `INT32`, `INT64`, `TIMESTAMP`, `BOOLEAN`, nullability) and human meaning. Types
   are needed to confirm a column's value kind; this is also where column *meaning*
   comes from. Find the newest feature folder under `specs/` that touches telemetry.
4. **`specs/01X-.../data-model.md`** — the value-kind classification table (mirrors
   `validate.go`); good cross-check.
5. **`tools/telemetry-mcp/testdata/stub-query-tool/main.go`** — what dataset paths the
   test stub recognizes (sanity only).

> If `validate.go` and a spec disagree, trust `validate.go` and tell the user the spec
> is out of date.

## Procedure

1. **Read the authoritative map.** Open `validate.go` and extract `validDatasets` and
   the full `datasetColumns` map (every dataset, every column, every `valueKind`).
2. **Read the types + meanings.** Open the newest telemetry `parquet-schemas.md` (and
   `data-model.md`) to get each column's parquet type, nullability, and what it means.
   For a brand-new dataset with no spec yet, get types/meaning from wherever the
   columns are produced (the writer) or ask the user for one-line descriptions.
3. **Diff against the current references.** Compare what you found to
   [../references/telemetry-schema.md](../references/telemetry-schema.md):
   - datasets added / removed
   - columns added / removed (per dataset)
   - value-kind changes (e.g. a column that became `bool` or `timestamp`)
   - type / nullability changes
   Present the diff to the user in plain language before writing.
4. **Update `references/telemetry-schema.md`:**
   - Add/remove dataset sections to match `validDatasets`.
   - For each dataset, make the column table exactly match `datasetColumns` (name +
     kind), with nullability + a one-line meaning from the spec/writer.
   - Refresh the "one-line each" dataset summaries and the "Quick column-to-question
     map" if datasets/columns changed.
   - Update the `last verified` date at the top to today.
5. **Update `references/telemetry-query-guide.md` only if the grammar changed:** the
   accept/reject example tables, forbidden lists, value-kind rules, or
   `MaxFilterLength` / `dateRegex`. Most schema-only changes don't touch this file —
   but bump its `last verified` date if you edited it. (Refresh example filters that
   referenced a renamed/removed column.)
6. **Propagate to SKILL.md if the dataset set changed:** the router's "The datasets
   (one-line each)" list and any dataset-specific routing hints must match
   `validDatasets`. Bump its `last verified` date if edited.
7. **Flag drift you can't fix here.** If `main.go`'s tool description or a spec doc is
   now stale relative to `validate.go`, tell the user — those live in the repo and are
   the user's to change (this function only owns the skill's reference files unless the
   user asks otherwise).

## Value-kind mapping cheat-sheet

| `validate.go` kind | parquet types | schema-reference kind | filter literal |
|--------------------|---------------|-----------------------|----------------|
| `kindNumeric` | DOUBLE, INT32, INT64 (+ nullable) | numeric | bare number |
| `kindString` | STRING | string | `'quoted'` |
| `kindTimestamp` | TIMESTAMP | timestamp | `'YYYY-MM-DD'` date string |
| `kindBool` | BOOLEAN | bool | bare `true`/`false` |

## When done

Summarize what changed (datasets/columns/kinds added, removed, or retyped), which
files you updated, and any repo-side drift the user still needs to fix (e.g. a stale
`main.go` description or spec). Confirm the `last verified` dates were bumped.

## If nothing changed

If the references already match `validate.go`, say so and just bump the `last
verified` date on the files you confirmed — that's the whole point of the date: a
recent date means "checked against reality recently."
