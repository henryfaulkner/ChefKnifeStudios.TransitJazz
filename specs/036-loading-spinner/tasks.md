---

description: "Task list for feature 036-loading-spinner"
---

# Tasks: Loading Spinner

**Input**: Design documents from `specs/036-loading-spinner/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/boot-theme-read.md, quickstart.md

**Tests**: None automated. This is a pre-boot HTML/CSS feature verified manually
per `quickstart.md` (no test framework covers the boot page). No test tasks are
generated.

**Organization**: Two user stories. US1 = the spinner appears/animates/self-removes.
US2 = the spinner themes to the saved dark/light preference. Both touch the same
two files (`index.html`, `wwwroot/css/app.css`), so most tasks are sequential
(same-file), not parallel. `[P]` is used only where files genuinely differ.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: US1 or US2
- Exact file paths are absolute-from-repo-root.

## Path Conventions

Single frontend touch under
`src/Client/ChefKnifeStudios.MartaJazz.Client.WebApp/wwwroot/`.

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: No project init needed — files already exist. This phase just
confirms the baseline before editing.

- [X] T001 Confirm the two target files exist and note the code to replace: `src/Client/ChefKnifeStudios.MartaJazz.Client.WebApp/wwwroot/index.html` (the `<div id="app">Loading...</div>` on line ~20) and `src/Client/ChefKnifeStudios.MartaJazz.Client.WebApp/wwwroot/css/app.css` (the existing `.loading-indicator` block to be replaced by the new spinner CSS).

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: None. This feature has no shared prerequisite work — each user story
edits its own concern within the two files. Proceed directly to Phase 3.

*(No foundational tasks.)*

---

## Phase 3: User Story 1 - Branded loading spinner while the app boots (Priority: P1) 🎯 MVP

**Goal**: Replace the bare "Loading..." text with an animated rotating-ring
spinner + centered "loading..." label that shows during WASM cold-start and is
removed automatically when the app renders.

**Independent Test**: Throttle to Slow 3G, hard-reload; a rotating ring +
"loading..." shows continuously until the app UI renders, then disappears with no
residue (quickstart tests 1 & 2).

### Implementation for User Story 1

- [X] T002 [US1] Add the spinner markup **inside** `<div id="app">…</div>` in `src/Client/ChefKnifeStudios.MartaJazz.Client.WebApp/wwwroot/index.html`, replacing the literal `Loading...`. Structure: an outer `.app-loader` container holding a `.app-loader__ring` element and a `<span class="app-loader__label">loading...</span>`. Keeping it inside `#app` means Blazor's first render tears it down automatically (no removal code) — see research.md R3.
- [X] T003 [US1] Add the spinner CSS to `src/Client/ChefKnifeStudios.MartaJazz.Client.WebApp/wwwroot/css/app.css`, replacing the existing `.loading-indicator` block. Translate the user's example to project standards (FR-008): `.app-loader` = full-viewport centered flex container; `.app-loader__ring` = a square with `border-radius:50%`, a visible arc via one-side border (`border` transparent + `border-right`/`border-top` colored, ~0.3rem), `animation: app-loader-spin 2s linear infinite`; `.app-loader__label` centered. Add `@keyframes app-loader-spin { 0%{transform:rotate(0)} 100%{transform:rotate(360deg)} }`. Light colors: background `var(--background)`, ring + label `var(--on-surface)` (defined in `variables.css`). Do NOT use the example's raw `white` (fails on the light `#F5F5F5` background).

**Checkpoint**: Spinner appears on cold load, spins, and self-removes when the app
renders (quickstart 1 & 2 pass). At this point it always renders in LIGHT styling —
US2 adds dark. This is a shippable MVP.

---

## Phase 4: User Story 2 - Spinner matches the visitor's saved light/dark preference (Priority: P2)

**Goal**: The spinner reflects the persisted dark/light preference from the first
frame, read directly from `localStorage` before Blazor boots, with a safe light
fallback for missing/corrupt data.

**Independent Test**: Enable dark mode, hard-reload throttled → dark spinner from
the first frame (no light flash). First-visit / corrupt value → light spinner, no
error (quickstart tests 3–7).

### Implementation for User Story 2

- [X] T004 [US2] Add the inline pre-boot theme `<script>` to `src/Client/ChefKnifeStudios.MartaJazz.Client.WebApp/wwwroot/index.html`. Place it high in `<head>` (before the app renders) so `data-theme` is set before first paint — avoids flash of wrong theme. Behavior per contracts/boot-theme-read.md: read `localStorage.getItem("Setting")`; `JSON.parse`; find a boolean property whose lowercased name is `isdarkmodeenabled` (case-insensitive — Blazored writes camelCase `isDarkModeEnabled`, see research.md R2); set `document.documentElement.setAttribute("data-theme", isDark ? "dark" : "light")`. Wrap the whole thing in `try/catch`; on ANY failure set `data-theme="light"`. Never throw.
- [X] T005 [US2] Add the dark-mode CSS override to `src/Client/ChefKnifeStudios.MartaJazz.Client.WebApp/wwwroot/css/app.css`: under `[data-theme="dark"]`, set `.app-loader` background `#1A1C1E` and `.app-loader__ring` / `.app-loader__label` color `#E2E2E6` (from `ColorConstants.Dark.Background` / `.OnSurface` — constitution XIII requires dark values from that palette, not ad-hoc hexes). Keep the light rule as the default (no `[data-theme]` needed for light; the pre-boot script sets `data-theme="light"` explicitly anyway).

**Checkpoint**: US1 AND US2 both work — spinner animates, self-removes, AND themes
correctly from the saved preference with a light fallback (quickstart 1–7 pass).

---

## Phase 5: Polish & Cross-Cutting Concerns

- [ ] T006 Run all of `specs/036-loading-spinner/quickstart.md` (tests 1–7): cold-load spinner, self-removal, dark-from-first-frame, light, first-visit fallback, corrupt-value fallback, PascalCase tolerance. Fix any that fail.
- [X] T007 Verify no leftover `.loading-indicator` references remain anywhere (it was replaced in T003). Grep `wwwroot/` for `loading-indicator`; remove or repoint any stragglers.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: T001 first — trivial confirmation.
- **Foundational (Phase 2)**: none.
- **User Story 1 (Phase 3)**: after T001.
- **User Story 2 (Phase 4)**: after US1 exists (US2's CSS overrides US1's `.app-loader` rules; the `data-theme` script has nothing to theme until the spinner markup from T002 exists).
- **Polish (Phase 5)**: after US1 + US2.

### User Story Dependencies

- **US1 (P1)**: standalone MVP — spinner works, always light.
- **US2 (P2)**: builds on US1 (same elements). Not independently shippable without US1's markup, but independently *testable* once US1 is in (toggle preference, observe theme).

### Within Each Story

- T002 (markup) before T003 (CSS) — CSS targets the markup's class names.
- T004 (script sets `data-theme`) before/with T005 (CSS keys off `[data-theme="dark"]`).

### Parallel Opportunities

- Minimal. T002 (`index.html`) and T003 (`app.css`) touch **different files** and could run `[P]`, but T003's selectors must match T002's chosen class names, so do T002 first to lock names. Same for T004/T005. No safe cross-file parallelism worth the coordination on a 4-task feature — do them in order.

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. T001 → T002 → T003.
2. **STOP and VALIDATE**: quickstart tests 1 & 2 (spinner shows + self-removes).
3. Shippable: a branded, animated loading screen (light only).

### Incremental Delivery

1. US1 → animated spinner (MVP, light only).
2. US2 → dark/light reactivity from saved preference.
3. Polish → full quickstart pass + dead-CSS cleanup.

---

## Notes

- No automated tests: pre-boot HTML/CSS is verified via quickstart.md manual steps.
- The "loading..." label is a boot-time literal (not `.resx`) — localization runtime
  doesn't exist pre-boot; documented deferral in spec Assumptions + plan XII note.
- Total: 7 tasks across 2 files (`index.html`, `app.css`).
