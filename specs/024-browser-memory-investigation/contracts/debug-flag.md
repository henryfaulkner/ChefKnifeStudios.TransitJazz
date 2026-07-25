# Contract: Production logging quiet-down + runtime debug flag

Addresses US3 / FR-006, FR-007, FR-008. Principle IV preserved (structured logging stays;
only Debug-severity hot-path noise is gated). Constitution exempts logging from resx.

## .NET side

| Item | Before | After |
|------|--------|-------|
| `appsettings.json` `Logging.LogLevel.Default` | `"Debug"` | `"Information"` (production) |
| `appsettings.Development.json` `Logging.LogLevel.Default` | `"Debug"` | `"Debug"` (unchanged — local dev) |
| `Program.cs:87` | `builder.Logging.SetMinimumLevel(LogLevel.Debug)` (hard-coded) | level read from `builder.Configuration` `Logging` section (config-driven); remove the hard-coded Debug floor |

**Rule**: After the change, the effective production floor MUST be `Information` (or higher), so
`Logger.LogDebug(...)` hot-path calls (`HandleVehicleBatchAsync`, `LoadRoutesAsync`, `RenderRoutesAsync`)
do not emit in prod. `LogWarning`/`LogError` MUST still emit (FR-008).

## JS side

| Item | Definition |
|------|------------|
| `window.__MJ_DEBUG` | boolean, bootstrapped in `index.html`, default `false` |
| `ChefMapAnimator._log` (`vehicle-animator.js:13`) | early-return (no `console[level]`) when `!window.__MJ_DEBUG` and `level` ∈ {`debug`,`info`,`log`}; always emit when `level` ∈ {`warn`,`error`} |
| `transit-synth.js` / `map-interop.js` diagnostic `console.log`/`console.debug` | gated behind `window.__MJ_DEBUG`; `console.warn`/`console.error` unconditional |

## Acceptance vectors

| # | Setup | Action | Expected |
|---|-------|--------|----------|
| B1 | Production build, `__MJ_DEBUG` default | run normally, process batches + frames | **no** `[ChefMapAnimator]` debug lines, **no** per-batch/per-frame `console.log`, **no** `LogDebug` output (SC-005, FR-006) |
| B2 | `window.__MJ_DEBUG = true` in console | observe | hot-path diagnostic output reappears (FR-007) |
| B3 | Production, a real failure occurs | trigger a warning/error path | `console.warn`/`console.error` and `LogWarning`/`LogError` still emitted (FR-008) |
| B4 | Dev build | run normally | `appsettings.Development.json` `Debug` still in effect; .NET debug logs flow for local work |
