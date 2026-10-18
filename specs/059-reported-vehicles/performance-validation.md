# Performance acceptance record

**State: Not run.** No matched-load benchmark, SQL `EXPLAIN ANALYZE`, memory profile, or source-cadence measurement was performed.

The disposable PostgreSQL tests verify migration, atomic writes, bounded fixture arithmetic, and query behavior on small synthetic datasets. They do not establish supported-bound latency, index/storage growth, cleanup lag, worker overhead, queue/clone reservation lifetime, whole Stats latency, or cache/response size under production load.

Run the matched protocol in [quickstart.md](quickstart.md) against the recorded feature-058 baseline and a selected disposable dataset with realistic retained identity populations. Record dataset sizes, PostgreSQL version/settings, query plans, p50/p95 timings, allocation and reservation peaks, retention projection, and source timing. Required thresholds remain those in [plan.md](plan.md); no value is certified by this stub.
