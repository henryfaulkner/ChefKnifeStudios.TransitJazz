# Quickstart: Loading Spinner

Manual verification (no automated harness for pre-boot HTML). Run the WebApp and
observe the boot window. Use browser DevTools → Network → throttle to "Slow 3G"
to make the spinner visible for long enough to inspect.

## Prerequisites
- Run the Client.WebApp (or the full Aspire AppHost) and open it in a browser.
- DevTools open, Application → Local Storage available.

## Tests

1. **Spinner appears on cold load (FR-001/002/003, SC-001)**
   - Throttle to Slow 3G, hard-reload (Ctrl+Shift+R).
   - EXPECT: a rotating ring + centered "loading..." appears immediately and
     spins continuously until the map/app UI renders.

2. **Spinner is removed after load (FR-004, SC-004)**
   - After the app finishes loading in test 1.
   - EXPECT: no ring, no "loading..." text, no leftover overlay anywhere.

3. **Dark preference themes the spinner from first frame (FR-005, SC-002)**
   - In the running app, enable dark mode (dark-mode FAB), then hard-reload with
     Slow 3G throttling.
   - EXPECT: spinner shows dark background (#1A1C1E) with light ring/text from the
     first frame — NO flash of the light spinner first.

4. **Light preference (FR-005)**
   - Disable dark mode, hard-reload throttled.
   - EXPECT: light spinner (light background, dark ring/text).

5. **First visit / no saved preference (FR-006, SC-003)**
   - DevTools → Application → Local Storage → delete the `Setting` key. Reload.
   - EXPECT: spinner renders in light styling; no console error; app boots normally.

6. **Corrupt saved preference (FR-007, SC-003)**
   - Set the `Setting` key value to `not json{` in DevTools. Reload throttled.
   - EXPECT: spinner still renders in light styling; no uncaught error in console.

7. **PascalCase field tolerance (contract A3)**
   - Set `Setting` to `{"isDarkModeEnabled":false,"IsDarkModeEnabled":true}` is
     unnecessary; simpler: set `{"IsDarkModeEnabled":true}` and reload.
   - EXPECT: dark spinner (case-insensitive read).

## Done when
- Tests 1–6 pass. Test 7 confirms the read is robust to serializer casing.
