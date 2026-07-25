Large SignalR payloads have several implications in this app:

Immediate — the current breakage:
- The worker→hub hop dies once the serialized batch crosses MaximumReceiveMessageSize (1 MB, Program.cs:49). That's the error in the logs — the whole batch is rejected, so NYMTA drops entirely rather than degrading gracefully.
- There's a second hop (hub→browser via TransitHub) with the default 32 KB limit. Even after you raise the inbound limit, that hop will fail next unless it's also raised — so a config bump has to touch both.

Memory and GC pressure:
- Each large batch is fully materialized as a List<EventEnvelope> on the worker, re-serialized on the hub, cached in ILastBatchCache, and re-serialized again per client group. That's several full copies of a multi-MB object per tick, every 10 seconds — meaningful allocation churn and GC pressure on both worker and API.

Latency and head-of-line effects:
- Bigger frames take longer to serialize, transmit, and deserialize. On a 10-second tick, a payload that takes seconds to push erodes the freshness budget and can bunch up behind the next tick.
- The browser client must parse the whole thing before it can act — a multi-MB JSON parse on the WASM client is a real hitch, competing with the audio/render loop.

Fan-out multiplication:
- The hub→client cost scales with connected clients. One fat batch × N browsers = N serializations and N transmissions. Large payloads make each additional user disproportionately expensive.

Scaling trajectory:
- The payload grows with fleet size and every new city added. A limit bump unblocks today but just moves the ceiling; the batch keeps growing. That's why the sensible framing is: raise the limits to unblock now, then separately decide on payload reduction (thinning fields, chunking, or delta-only records) so the size problem doesn't simply recur at the new threshold.

One caveat: I'm reasoning from the record shape and pipeline structure visible in context, not from a measured payload size for NYMTA — the actual byte count per tick hasn't been established here.




It would help, but only a slice of the pressure — and not the slice that dominates here.

What a recyclable/pooled list (e.g. ArrayPool<T>-backed) actually addresses: the allocation of the backing array for the List<EventEnvelope> (and the inner List<RouteNearestPointRecord>). By renting and returning the buffer instead of allocating a fresh one each tick, you avoid one large-object-heap allocation per city per 10s cycle. For a multi-MB batch, the backing array is large enough to land on the LOH, and LOH churn is exactly what pooling is good at. So yes, real and legitimate.

Why it doesn't fix the bulk of the churn in this pipeline:

1. The records themselves. Each RouteNearestPointRecord is a record (reference type) — pooling the list doesn't pool the ~thousands of individual record objects inside it. Those still allocate and still get collected. If you wanted to pool those too, they'd need to be structs, which is a much bigger change than swapping the list type.
2. Serialization dominates the allocations. The heaviest churn isn't the list — it's the JSON. The batch gets serialized on the worker, re-serialized on the hub, cached, and re-serialized per client group. Each serialization allocates buffers and string/byte output measured in megabytes. A recyclable list touches none of that; System.Text.Json's own pipeline is where those bytes live.
3. Multiple full copies across hops. The materialize→serialize→cache→re-serialize chain means several independent multi-MB copies exist per tick regardless of how the source list is allocated.

Bottom line: A recyclable list is a reasonable, low-risk micro-optimization for the LOH backing-array allocations, but it's treating a symptom downstream of the real problem. The dominant GC cost is proportional to payload size × number of serialization hops × client count — which is why payload reduction (fewer/thinner records, delta-only) attacks the churn at its source in a way pooling the container never can. If the payload were already small, you likely wouldn't reach for pooling at all.







No — a recyclable/pooled list wouldn't help your frontend movement smoothness, because it targets the wrong bottleneck. Here's why, and where the real problem is.

Why RecyclableList doesn't help the frontend:
- Pooling a list reuses a backing array to avoid allocations on the .NET server side. Your symptom is the browser (WASM) client failing to animate movement smoothly — a rendering/compute hitch, not a server-allocation problem.
- Even on the client, the smoothness problem isn't the List container — it's the volume of work per tick: parsing a multi-MB JSON payload, marshalling thousands of records across the Blazor↔JS interop boundary, and driving thousands of simultaneous tweens on the map. Pooling the list touches none of that.

What actually causes the stutter for NY/DC:
1. Multi-MB JSON parse on the WASM client, every 10 seconds, on the same thread that competes with your audio/render loop. This is the big one.
2. Interop marshalling — you build a List<object> of anonymous records and pass the whole thing to JS each tick (the TransitMap.razor.cs projection). Thousands of objects crossing the boundary is expensive.
3. The map rendering itself — animating thousands of vehicle tweens simultaneously.

Levers that would actually help frontend smoothness:
- Payload reduction (the field-thinning already discussed) — smaller payload = faster parse = less main-thread blocking. This is the most direct win and it's already on the table.
- MessagePack instead of JSON — significantly cheaper to deserialize on the WASM client than a multi-MB JSON parse; this was named as the reserved escalation and this symptom is exactly when you reach for it.
- Batch/offload the interop and animation — pushing raw data to JS once and letting the JS map layer manage the tweens (rather than marshalling per-record structures) reduces boundary cost.
- Frame-budget the render — spreading tween setup across frames so one fat batch doesn't block a single frame.

So you're right that it's time to optimize — but the lever is payload size and deserialization/interop cost, not container pooling. A RecyclableList is a server-side LOH micro-optimization; it won't move the needle on client-side movement smoothness at all. If you want the single highest-impact change for the NY/DC stutter specifically, MessagePack + the field-thinning together attack the parse cost head-on.









It could be, for parts of the problem — but it's a real architectural tradeoff, not a clear win. Here's the honest picture based on your app's shape.

Where a cursor-based feed (client polls GET /feed?cursor=X, gets a delta + next cursor) genuinely helps:

- Fan-out cost. SignalR's cost multiplies per connected client — one fat batch × N browsers = N serializations + N pushes. A cursor feed is pull: you serialize each delta once and it's cacheable. Every client on the same cursor hits the same cached response (CDN/reverse-proxy edge caches love this). That's the single biggest scaling and cost win — it decouples cost from client count.
- Backpressure / catch-up. A struggling WASM client (your NY/DC stutter) pulls at its own pace. It can skip ahead to the latest cursor and drop intermediate deltas rather than being force-fed every 10s frame and falling behind. SignalR push has no natural "I'm behind, just give me current state" — that's why you needed ILastBatchCache for late-joiners.
- Statelessness at the edge. No persistent socket per client, no reconnect/group-rejoin dance, no hub holding connections. Scales horizontally more simply — any instance can serve any cursor, no sticky sessions or backplane needed. That's cheaper and less operationally complex at high client counts.

Where it does NOT help, or costs you:

- It doesn't shrink the payload. The dominant frontend problem you just identified — multi-MB JSON parse + interop marshalling + thousands of tweens on the WASM main thread — is identical whether the bytes arrive via push or pull. A feed changes delivery, not volume. You still need field-thinning / MessagePack / delta-encoding regardless. Don't expect a feed to fix the stutter by itself.
- Latency floor. Polling adds up to one poll-interval of staleness. On a 10s cadence that's fine — your data is already tick-based, not sub-second — so this is a mild cost here, but it's a real one versus push's immediacy.
- Delta bookkeeping moves to you. To be cheaper than "resend everything," the feed must serve deltas per cursor, which means the server tracks per-cursor state (what changed since cursor X). That's genuinely more complex than SignalR's fire-and-forget push. If you instead serve full snapshots per poll, you keep it simple but lose the bandwidth savings — you're back to shipping the whole fleet, just via HTTP.

Net assessment: A cursor feed is plausibly cheaper and simpler at scale specifically on the fan-out dimension (cost decoupled from client count, cacheable, stateless edge) and gives you natural client backpressure — which directly matches your "many clients, struggling WASM" concern. But it is not a substitute for payload reduction, and the delta-tracking version trades socket complexity for cursor-state complexity rather than eliminating complexity outright.

The pragmatic framing: your current crash and stutter are payload-size problems, and those want thinning/MessagePack no matter the transport. The feed question is a separate, later decision about whether push-to-N-clients or cache-and-pull is the better fan-out model as concurrent users grow — worth it if client count is your scaling axis, over-engineering if it isn't.




B — stop rebuilding the whole world. Restructure so you're not allocating 5,000 fresh Feature objects per tick — reuse a persistent feature array and mutate coordinates in place, or move vehicles to a symbol/circle layer with a more surgical update. Bigger change, bigger payoff, more risk.

B — gate the entire tick. Skip the whole function body (math included) unless 66ms elapsed. Cuts everything by 75%, including the extrapolation math. But now state.currentPos advances in 66ms jumps, and since your interpolation uses elapsed = now - state.startTime computed fresh each render, it still lands correct — but any per-frame smoothness in the phase-transition bookkeeping gets coarser. Slightly more risk for a marginal extra saving on the cheap half.