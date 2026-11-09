# Client Experience Capture Inventory

This inventory is the version 1 owner and catalog review for feature 063. It records evidence the application can establish and the remaining gaps before the dashboard is used for operational decisions.

## Journey and owner coverage

| Observation | Evidence owner | Version 1 evidence | Status |
| --- | --- | --- | --- |
| Bootstrap | `App.razor`, `ApplicationViewModel.InitializeAsync`, `RoutesLoadedTask` | Accepted application initialization attempt and current route-data outcome | Implemented; route readiness does not prove a usable map. |
| Route data | `ApplicationViewModel.LoadRoutesAsync` | Current-city usable route geometry accepted by the application model | Implemented as a capability transition. |
| Map startup | `Map.razor.cs`, `MapReadinessState` | Current generation has usable route geometry and required map layers | Implemented; browser acceptance pending. |
| Live delivery | `SignalRNotificationService`, `LiveDeliveryState` | Desired delivery plus confirmed join for the current city and connection generation | Implemented; transport connection alone is not readiness. Browser acceptance pending. |
| Audio unlock/resume | `TransitMap.razor.cs`, `AudioUnlockAttemptState` | Accepted native gesture and non-stale context completion; stale results are discarded | Implemented with unit coverage of stale state rejection; real mobile/desktop acceptance pending. |
| Requested audio health | `AudioCapabilityHealthTracker` | Running context plus required voice resources | Implemented as requested capability transitions. This does not prove physical audibility; browser acceptance pending. |
| Settings effect and persistence | `SettingsChangeController` | Fixed action outcome and separate requested save result under one parent action | Implemented with fixed action codes. Raw selected values are excluded; storage-failure browser acceptance pending. |
| Stop/mute | `TransitMap.razor.cs`, audio stop owner | Confirmed stopped result or verified `still_playing` failure | Implemented; real audio stop acceptance pending. |
| City navigation | `CityNavigationController` | Source action result; destination map startup is a separate activation | Implemented; source result does not claim destination readiness. |
| Toast presentation | `NotificationCoordinator.PresentationCommitted` | Copied city/time/severity and summary arrays, reported after accepted presentation | Implemented independently of the remountable host; durable-view and browser acceptance pending. |
| Visibility and observation windows | `ClientExperienceReporter`, `ClientExperienceObservationTracker` | Hidden time is excluded, delayed callbacks become gaps, and owners are re-read on foreground return | Implemented locally; browser suspension/recovery acceptance pending. |

The wire catalog currently permits these journey codes: `bootstrap_start`, `city_map_start`, `live_start`, `live_resume`, `audio_unlock`, `audio_resume`, `settings_effect`, `settings_persistence`, `audio_stop`, and `city_navigation`. Fixed action codes are `display_setting`, `map_style`, `audio_filter`, `audio_texture`, `voice_selection`, `delivery_stop`, `audio_stop`, `audio_setting`, and `city_navigation`. Capabilities are `routes`, `map`, `live_delivery`, and `audio`; no source cadence or physical-audibility capability is defined.

## Notification issue catalog

The wire catalog explicitly covers all 26 current `NotificationIssueCode` values:

`LiveConnectionFailed`, `LiveJoinFailed`, `LiveInterrupted`, `LiveStopped`, `LiveResumeFailed`, `LiveVersionRejected`, `LiveProcessingFailed`, `RouteLoadFailed`, `RoutesUnavailable`, `SelectedRouteUnavailable`, `MapUnavailable`, `MapUnsupportedBrowser`, `MapStyleChangeFailed`, `MapTransitDisplayFailed`, `MapLayerUnavailable`, `DisplaySettingFailed`, `CityNavigationFailed`, `AudioResumeFailed`, `AudioUnavailable`, `RequiredVoicesPartiallyUnavailable`, `RequiredVoicesUnavailable`, `AudioTextureChangeFailed`, `AudioFilterFailed`, `AudioStopFailed`, `SettingsNotSaved`, and `SettingsApplyFailed`.

## Evidence gaps and disabled claims

- Local owner capture is implemented for bootstrap/map/live/audio/settings/stop/navigation/toast and foreground owner reassessment. End-to-end browser evidence remains open for audio gestures, map/live joins, settings storage failures, visibility recovery, and toast reading/host remount.
- Freshness remains diagnostic/unknown until a source cadence contract exists. No freshness code is in the capability catalog.
- Audio readiness measures browser context and required voice resources only. The client cannot prove that sound reached a visitor's speakers.
- Bootstrap reports initialize the app and route data; they do not stand in for the separate map-start journey.
- Capture remains disabled in `index.html` and server configuration until a compatible server contract is deployed and release routing is verified.

## Local acceptance evidence

- Controlled attempt tests cover accepted start/terminal correlation, foreground deadlines, hidden-time exclusion, 24-hour supersession, action IDs, and parent-action correlation.
- Owner tests cover connected-but-not-joined live delivery and source-only failed city navigation. Existing audio-state tests cover stale completion and continue-muted behavior.
- Notification tests verify snapshots are emitted only after successful presentation and copy the accepted city, time, issue codes, and resource keys.
- Explicit navigation tests verify the source city activation is correlated to the action; API privacy tests reject unmodeled sensitive fields.
- These tests do not replace PostgreSQL-derived-view fixtures or real-browser acceptance. No physical audibility, long-term duration health, or production collection coverage is claimed.
