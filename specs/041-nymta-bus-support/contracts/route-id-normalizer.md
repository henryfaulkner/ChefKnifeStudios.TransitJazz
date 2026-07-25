# Contract: `RouteIdNormalizer.Apply`

**Location**: `TransitDataWorker/Cities/RouteIdNormalizer.cs` (new)
**Signature**: `public static string Apply(string routeId, IReadOnlyList<string> steps)`

Pure, total, deterministic. No I/O. Applies each named step in order, left to right.

## Step semantics

| Step | Rule |
|------|------|
| `uppercase` | `routeId.ToUpperInvariant()` |
| `plusToSbs` | if `routeId` ends with `'+'`: drop the `'+'`, append `"-SBS"`; else unchanged |
| `stripLeadingZeros` | regex `^([A-Z]+)0*(\d.*)$`: if match, return `group1 + group2`; else unchanged |
| any other string | no-op passthrough (MUST NOT throw) |

## Accept vectors (become `[Theory]` / `[InlineData]` rows)

| # | Input `routeId` | `steps` | Expected output |
|---|-----------------|---------|-----------------|
| 1 | `"bx3"` | `["uppercase"]` | `"BX3"` |
| 2 | `"M15+"` | `["plusToSbs"]` | `"M15-SBS"` |
| 3 | `"Q06"` | `["stripLeadingZeros"]` | `"Q6"` |
| 4 | `"BX07"` | `["stripLeadingZeros"]` | `"BX7"` |
| 5 | `"Q06"` | `["uppercase","plusToSbs","stripLeadingZeros"]` | `"Q6"` |
| 6 | `"m15+"` | `["uppercase","plusToSbs","stripLeadingZeros"]` | `"M15-SBS"` |
| 7 | `"bx3"` | `["uppercase","plusToSbs","stripLeadingZeros"]` | `"BX3"` |
| 8 | `"S"` | `["uppercase","plusToSbs","stripLeadingZeros"]` | `"S"` (letters only, no digit → zero-strip unchanged) |
| 9 | `"Q6"` | `["stripLeadingZeros"]` | `"Q6"` (no leading zero → unchanged) |
| 10 | `"anything"` | `["bogusStep"]` | `"anything"` (unknown step = no-op, no throw) |
| 11 | `"M15+"` | `[]` | `"M15+"` (empty steps = identity) |
| 12 | `"Q006"` | `["stripLeadingZeros"]` | `"Q6"` (multiple leading zeros) |

## Invariants (property-style assertions)

- `Apply(x, [])` == `x` for all `x`.
- `Apply` never throws for any `(routeId, steps)` where `routeId` is non-null.
- Applying the full NYC pipeline is idempotent on already-normalized IDs: `Apply(Apply(x, full), full) == Apply(x, full)`.
