# Contract: SignalR Group Cutover

The group name is an unversioned wire contract carrying the city slug verbatim. This contract
governs how it changes without any city going silently dark.

---

## C1. The silent-failure mechanism

`TransitHub.JoinCity` (`TransitHub.cs:21`) calls
`Groups.AddToGroupAsync(Context.ConnectionId, city)` on the **raw, unvalidated** string. The
worker publishes to the group named by its `Cities[].Name`. Nothing cross-checks them.

When they disagree:

| Stage | Observable |
|---|---|
| Client joins group `atlanta` | success — no error |
| Worker publishes to group `marta` | success — no error |
| Client receives | **nothing** |
| Client shows | empty map, "0 active buses" |
| Logs show | a normal join, a normal publish |

No exception is thrown on either side. This is the feature's single highest risk (FR-008,
SC-003) — and it presents identically to a feed outage, so it is easy to misdiagnose.

---

## C2. Version gate (FR-009)

**MUST**: `HubMethods.JoinCity` → `JoinCityV2`, value `"JoinCity"` → `"JoinCityV2"`, and
`TransitHub.JoinCity` renamed to match. Signature unchanged.

**MUST NOT**: keep a `JoinCity` method as a compatibility shim. Retaining it re-creates the
silent failure it exists to prevent — an old client would join successfully under the old slug
and receive nothing.

Effect on a stale client (old WASM, updated server):

| With the gate | Without it |
|---|---|
| Invokes `"JoinCity"` — method not found | Joins group `marta` successfully |
| SignalR faults the invocation | No error |
| Client surfaces a connection error | Client shows an empty map indefinitely |
| **Loud** (FR-010) | **Silent** |

This is new work; no `V2` precedent exists (research R2).

---

## C3. Observability (FR-010)

**MUST**: the join failure appear in operator-visible output.

`TransitHub.JoinCityV2` already logs `connectionId`, `city`, and `replayCount`; keep that log
and ensure it names the **new** method, so a run of "no join logged, yet clients connected" is
recognisable as stale peers rather than an outage.

Recommended: log the joined group name at publish time too, so publish/join symmetry is
checkable from logs alone.

---

## C4. Reconnect (FR-011)

A client whose connection predates the deploy holds a group membership under the old slug.

**MUST**: on reconnect, the client re-invokes join with its current (new) slug — the existing
`SignalRNotificationService` reconnect path (`:101`, `:105`) already re-invokes on
reconnect, so it satisfies this once the method name is updated. Verify, don't assume.

A user who does not reconnect keeps a stale session until refresh. Acceptable: the version gate
means their next connection attempt fails loudly rather than silently.

---

## C5. Deploy order (FR-020, FR-021)

Three lanes: **server + worker are atomic**; **client is separate**; **`deploy/marta-jazz` must
land identically**.

Required order:

1. **Server + worker together** — new slugs in both `appsettings.json`, `JoinCityV2` live.
   Old clients now fail loudly at join (C2). *This is the user-visible window.*
2. **Client** — emits new slugs, invokes `JoinCityV2`. Window closes.
3. **`deploy/marta-jazz`** — same changes, or that deployment breaks.
4. **Verify all seven cities** (FR-022) before declaring done.

**MUST NOT** ship the client first: a new client invoking `JoinCityV2` against an old hub fails
for *every* user, not just stale sessions.

The window in step 1 is unavoidable given three lanes. The gate makes it loud and
self-resolving on refresh, rather than silent (the assessment's §7 rollout suggests a
server-side alias map to eliminate it; aliasing was explicitly declined, so the loud window is
the accepted trade).

---

## C6. Verification (FR-022, SC-001/002/003)

Per city, all seven — **observe data arriving**, never merely absence of errors:

- [ ] Load `#<slug>`; vehicles appear and move
- [ ] Vehicle count is non-zero and plausible
- [ ] A crossing produces audio
- [ ] Route shapes render
- [ ] Join logged server-side with the expected group name
- [ ] No client console errors

"No errors" alone is exactly what the silent failure looks like (C1). Arrival of vehicles is
the only sufficient evidence.
