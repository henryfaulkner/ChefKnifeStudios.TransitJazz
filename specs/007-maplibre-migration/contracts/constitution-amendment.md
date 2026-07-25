# Contract: Constitution Amendment (Principle II)

**Feature**: 007-maplibre-migration | **Date**: 2026-05-18

This document is the exact text contract for the constitution edit that this feature ships. The task generator should treat this as a copy-from-here, paste-into-`.specify/memory/constitution.md` artifact, applying the three sections below in order.

---

## 1. Sync Impact Report block (top of file)

**Current block** (lines 1–13, to be replaced):

```text
<!--
Sync Impact Report:
- Version change: 1.0.0 → 2.0.0 (Major rewrite reflecting actual architecture)
- Modified principles: III rewritten (two-pass pipeline with spatial reconciliation), new VI added (GTFS ID mapping)
- Added sections: Solution Structure, Two-Pass Processing Pipeline, GTFS Data Pipeline, SignalR Event System
- Removed sections: Music Engine Flow (outdated naming), duplicate Event Message JSON (was identical to input)
- Restructured: Tech Stack & Architecture now reflects actual 11-project solution
- Templates requiring updates:
  - ✅ .specify/templates/plan-template.md (Constitution Check section exists)
  - ✅ .specify/templates/spec-template.md (scope/requirements alignment verified)
  - ✅ .specify/templates/tasks-template.md (task categorization reviewed)
- Follow-up TODOs: None
-->
```

**Replacement block**:

```text
<!--
Sync Impact Report:
- Version change: 2.0.0 → 3.0.0 (Principle II redefined: auth model changed from Azure Maps Auth Function to MapTiler URL-restricted public key)
- Modified principles: II rewritten (no-frontend-secrets now expressed via URL-restricted public-key + origin enforcement, not via server-issued tokens)
- Modified sections: "Tech Stack & Architecture → Frontend (Blazor WASM)" — Azure Maps references replaced with MapLibre GL JS + MapTiler
- Templates requiring updates:
  - ✅ .specify/templates/plan-template.md (Constitution Check section unchanged in shape)
  - ✅ .specify/templates/spec-template.md (no template changes required)
  - ✅ .specify/templates/tasks-template.md (no template changes required)
- Follow-up TODOs: None
-->
```

---

## 2. Principle II rewrite

**Current text** (the entire `### II. No Frontend Secrets` section):

```text
### II. No Frontend Secrets
The frontend MUST NEVER hold secrets. To authenticate with Azure Maps, the frontend MUST call the Azure Maps Auth Function to request a temporary token. All secrets (Client ID, Client Secret) MUST be stored securely in the Azure Function, not in client-side code.
```

**Replacement text**:

```text
### II. No Frontend Secrets
The frontend MUST NEVER hold secrets. The map provider (MapTiler) is accessed via a URL-restricted public API key embedded in the WASM bundle's configuration. The key is bounded at the provider side by an origin allowlist limiting it to the project's known domains (`https://localhost:*` for dev, `https://www.martajazz.com` for production). Because the key cannot be used by any other origin, it is not a secret — it is a usage-attribution token whose abuse vector is closed by the provider-enforced origin check.

This principle therefore distinguishes between two classes of credential:
- **Secrets** (passwords, private keys, server-issued bearer tokens, OAuth client secrets): MUST NEVER appear in the frontend bundle.
- **Public, origin-restricted attribution keys** (the MapTiler API key, and similar keys from comparable providers like Mapbox or Google Maps JS): MAY appear in the frontend bundle, provided the provider's origin restriction is configured before the key is committed.

Before committing any such key, the developer MUST verify the URL restriction is active in the provider's console. A key without a configured restriction is treated as a secret and MUST be moved to `appsettings.Development.json` (gitignored) or to user secrets until the restriction is in place.
```

---

## 3. "Frontend (Blazor WASM)" tech-stack section rewrite

**Current text** (under `### Frontend (Blazor WASM)`):

```text
### Frontend (Blazor WASM)
- Hosted as an Azure Static Web App
- Connects to WebAPI's SignalR hub to receive `EventEnvelope` batches
- Renders Azure Maps with vehicle animation along route geometries
- Calls Azure Maps Auth Function for temporary tokens (no direct secrets)
- Two rendering modes:
  - **V2 (primary)**: Animates vehicles along route polylines using `RouteNearestPointBatchEvent` records
  - **V1 (fallback)**: Plots raw vehicle positions when no nearest-point events arrive
```

**Replacement text**:

```text
### Frontend (Blazor WASM)
- Hosted as an Azure Static Web App
- Connects to WebAPI's SignalR hub to receive `EventEnvelope` batches
- Renders MapLibre GL JS against MapTiler vector tiles with vehicle animation along route geometries
- Authenticates to MapTiler via a URL-restricted public API key embedded in `wwwroot/appsettings.json` (see Principle II)
- Two rendering modes:
  - **V2 (primary)**: Animates vehicles along route polylines using `RouteNearestPointBatchEvent` records, driven by a `requestAnimationFrame` loop that rebuilds a single GeoJSON source per tick
  - **V1 (fallback)**: Plots raw vehicle positions when no nearest-point events arrive
```

---

## 4. "Security & Authentication" section adjustment

**Current text** (under `### Security & Authentication`, first bullet):

```text
- Azure Maps: Azure Function handles Client ID & Secret securely, returning short-lived tokens to Blazor WASM app
```

**Replacement text** (first bullet):

```text
- Map provider (MapTiler): URL-restricted public API key embedded in the WASM bundle; origin enforcement is the compensating control (see Principle II)
```

The other two bullets in that section (`All inter-service communication ... HTTPS/WSS` and `Microsoft Identity Web for Azure AD integration (WebAPI)`) are unchanged.

---

## 5. Footer version + date update

**Current footer** (last line of constitution):

```text
**Version**: 2.0.0 | **Ratified**: 2026-05-03 | **Last Amended**: 2026-05-14
```

**Replacement footer**:

```text
**Version**: 3.0.0 | **Ratified**: 2026-05-03 | **Last Amended**: 2026-05-18
```

---

## Validation

After the amendment is applied, the constitution MUST satisfy:

1. A grep for `Azure Maps` returns zero hits in `.specify/memory/constitution.md`.
2. The string `URL-restricted public API key` appears in Principle II.
3. The version footer reads `3.0.0`.
4. The Sync Impact Report block at the top of the file references the `2.0.0 → 3.0.0` transition.
5. No `[NEEDS CLARIFICATION]` markers appear anywhere in the file.
