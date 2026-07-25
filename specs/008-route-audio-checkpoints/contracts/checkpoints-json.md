# Contract — `wwwroot/checkpoints.json`

**Feature**: `008-route-audio-checkpoints`
**Path (production)**: `src/Client/ChefKnifeStudios.TransitJazz.Client.WebApp/wwwroot/checkpoints.json`
**Path (served at runtime)**: `/checkpoints.json` (relative to the Blazor WASM app root)
**Consumed by**: `CheckpointLoader` (C# `HttpClient.GetFromJsonAsync<...>`) → mirrored to JS via `ChefMap.configureCheckpoints`
**Load timing**: Once at page init (parallel with route shapes). Never re-fetched.

This file is the *only* external interface this feature exposes. Editing it and reloading the app changes the live checkpoint set with no rebuild (FR-007).

---

## JSON Schema (informative)

```json
{
  "$schema": "https://json-schema.org/draft/2020-12/schema",
  "type": "object",
  "required": ["version", "checkpoints"],
  "properties": {
    "version": { "type": "integer", "const": 1 },
    "checkpoints": {
      "type": "array",
      "items": { "$ref": "#/$defs/checkpoint" }
    }
  },
  "$defs": {
    "checkpoint": {
      "type": "object",
      "required": ["id", "routeShortName", "position", "note"],
      "additionalProperties": false,
      "properties": {
        "id":             { "type": "string", "minLength": 1 },
        "routeShortName": { "type": "string", "minLength": 1 },
        "position": {
          "type": "object",
          "required": ["longitude", "latitude"],
          "additionalProperties": false,
          "properties": {
            "longitude": { "type": "number", "minimum": -180, "maximum": 180 },
            "latitude":  { "type": "number", "minimum":  -90, "maximum":  90 }
          }
        },
        "note": {
          "type": "object",
          "required": ["scaleDegree", "octave"],
          "additionalProperties": false,
          "properties": {
            "scaleDegree": { "type": "integer", "minimum": 0, "maximum": 4 },
            "octave":      { "type": "integer", "minimum": 2, "maximum": 6 }
          }
        }
      }
    }
  }
}
```

The JSON Schema is informative only — there is no runtime schema validator in the bundle. Validation is enforced by `CheckpointLoader` in C# per the rules in [`../data-model.md`](../data-model.md) § 1. The schema document above is the contract; it lists what the loader *expects* and what authoring the file *should* produce.

---

## Field reference

| Field | Type | Purpose |
|-------|------|---------|
| `version` | int (= `1`) | Schema version. Future schema changes bump this; the loader rejects unknown versions. |
| `checkpoints[].id` | string | Stable opaque identifier. Used as the GeoJSON feature id and as part of the cooldown key. Two checkpoints with the same `id` are an error (load-time validation rejects the second one). |
| `checkpoints[].routeShortName` | string | The GTFS `route_short_name` (e.g., `"74"`). Must match a route that the WebAPI returns via `GetAllRouteShapes` and that the client has loaded into `_routeShapeCache`. Case-sensitive. |
| `checkpoints[].position.longitude` | float | WGS84 longitude. |
| `checkpoints[].position.latitude` | float | WGS84 latitude. Combined with `longitude`, MUST land on or within 50 m of the route's polyline; up to 500 m is tolerated with snap-and-warn; beyond that the checkpoint is rejected with an error log. |
| `checkpoints[].note.scaleDegree` | int (0–4) | Index into the pentatonic-minor scale (see `data-model.md` § "Note derivation"). 0 = root, 1 = minor 3rd, 2 = perfect 4th, 3 = perfect 5th, 4 = minor 7th. |
| `checkpoints[].note.octave` | int (2–6) | MIDI octave (4 = middle C). Lower octaves sound darker / more rooted; higher octaves sparkle. |

---

## Worked example

A minimal viable file with three checkpoints on two routes (one route gets two checkpoints, satisfying spec edge case "two checkpoints close together on the same route" if their indices are close):

```json
{
  "version": 1,
  "checkpoints": [
    {
      "id": "ckpt-74-midtown",
      "routeShortName": "74",
      "position": { "longitude": -84.3880, "latitude": 33.7710 },
      "note": { "scaleDegree": 0, "octave": 4 }
    },
    {
      "id": "ckpt-74-buckhead",
      "routeShortName": "74",
      "position": { "longitude": -84.3672, "latitude": 33.8480 },
      "note": { "scaleDegree": 2, "octave": 4 }
    },
    {
      "id": "ckpt-118-decatur",
      "routeShortName": "118",
      "position": { "longitude": -84.2950, "latitude": 33.7745 },
      "note": { "scaleDegree": 0, "octave": 5 }
    }
  ]
}
```

The exact coordinates in the example are illustrative. Real checkpoint coordinates for the demo are picked by inspecting the route polyline in the running app (or in a GIS tool) and writing down a `(longitude, latitude)` pair that visibly sits on the line.

---

## Validation outcomes summary

| Outcome | Behaviour |
|---------|-----------|
| File missing or 404 | Page loads. No checkpoint markers render. No audio fires. Console: `[CheckpointLoader] checkpoints.json not found — feature disabled`. |
| File present but JSON-invalid | Page loads. No checkpoint markers render. Console: `[CheckpointLoader] checkpoints.json: parse error — feature disabled`. |
| File present, valid, zero checkpoints | Page loads. No markers. No audio. (FR-009 — routes without checkpoints behave unchanged.) |
| File present, valid, with one or more invalid entries | Valid entries are kept and rendered. Invalid entries are skipped with per-entry warning/error logs. (See `data-model.md` § 1 validation rules.) |

This "fail-open" behaviour (a broken file disables the feature instead of breaking the map) matches the POC's optional-overlay framing in the spec.

---

## Versioning

`version` is a single integer. The current and only version is `1`. If a future change to the schema is backward-incompatible (e.g., `note.tonic` becomes required), the new file uses `version: 2` and the loader gains a `version === 2` branch. Files of an unknown version are rejected with `[CheckpointLoader] unsupported version: N` and the feature is disabled for that load.

The intent is to make `checkpoints.json` editable by hand for the lifetime of the POC; a heavier authoring story can be designed later (out of scope per spec § Assumptions).
