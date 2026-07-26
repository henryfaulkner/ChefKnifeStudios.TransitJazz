namespace ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Logging;

/// <summary>
/// One denormalized telemetry row. event_type discriminates PerCityCycle vs FullCycle.
/// Nullable properties are populated only for the event type(s) that define them; they
/// serialize as null on the other type's rows (FR-004).
/// Property names are snake_case (not PascalCase) because Parquet.Net 5.6.1's
/// ParquetSerializer has no column-rename attribute — the C# property name IS the
/// parquet column name, and snake_case is the frozen wire contract (FR-006, FR-024).
/// </summary>
#pragma warning disable IDE1006 // naming rule violation — snake_case is intentional, see above
public sealed record TelemetryEvent : IEventArgs
{
    // ── Common (every row) ────────────────────────────────────────────────
    // Not `required`: ParquetSerializer.DeserializeAsync<T>() needs T : new(),
    // which is incompatible with required members (CS9040).
    public string event_type { get; init; } = "";        // "PerCityCycle" | "FullCycle"
    public string event_id { get; init; } = "";          // Guid "N"
    public DateTime observation_utc { get; init; }

    // ── PerCityCycle-only (null on FullCycle) ─────────────────────────────
    public string? city_name { get; init; }
    public double? feed_freshness_seconds { get; init; }

    // ── FullCycle-only (null on PerCityCycle) ─────────────────────────────
    public int? cities_processed_count { get; init; }
    public string? cities_processed_csv { get; init; }

    // ── Shared (both types; scope differs — per-city vs. tick-wide) ────────
    public double? time_taken_seconds { get; init; }
    public bool?   health_ok { get; init; }
    public int?    tones_emitted { get; init; }        // summed on FullCycle
    public int?    vehicles_processed { get; init; }   // summed on FullCycle
    public long?   gc_heap_bytes { get; init; }         // reused per tick
    public long?   process_working_set_bytes { get; init; } // reused per tick
    public int?    vehicle_state_cache_size { get; init; }        // summed on FullCycle
    public int?    crossing_baseline_cache_size { get; init; }    // summed on FullCycle
    public int?    route_index_size { get; init; }               // summed on FullCycle
    public int?    route_trigger_point_cache_size { get; init; }   // summed on FullCycle

    // ── Crossing-suppression attribution (feature 045, D3) — PerCityCycle-only, summed on
    // FullCycle. See specs/045-time-to-first-note/contracts/telemetry-schema.md — MUST stay
    // in sync with tools/telemetry-mcp/internal/validate/validate.go's kindNumeric allow-list.
    public int?    crossings_suppressed_first_seen { get; init; }   // summed on FullCycle
    public int?    crossings_suppressed_delta_leq0 { get; init; }   // summed on FullCycle
    public int?    crossings_suppressed_teleport { get; init; }     // summed on FullCycle
    public int?    crossings_suppressed_transfer { get; init; }     // summed on FullCycle

    // ── Egress measurement (feature 051, US1) — PerCityCycle-only, summed on FullCycle. ────
    // Exact MessagePack-serialized size of the List<EventEnvelope> published for this city
    // this tick (Worker.WireSize.Measure), NOT socket bytes (no SignalR framing/deflate).
    // NULL — never 0 — when the tick published nothing (empty batch, unhealthy tick, publish
    // returned false); a query on `batch_wire_bytes > 0` must equal "published ticks". MUST
    // stay in sync with tools/telemetry-mcp/internal/validate/validate.go's kindNumeric
    // allow-list (contract telemetry-observability C1/C2). Never rename.
    public long?   batch_wire_bytes { get; init; }                  // summed on FullCycle
}
#pragma warning restore IDE1006
