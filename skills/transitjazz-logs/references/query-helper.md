# Constrained Basic-query helper

The helper is conditional and exists only because this checkout has no registered Azure Monitor
read-only interface to prove Basic-table support. It is `tools/transitjazz-logs-query/`.

- Workspace aliases are a fixed `dev`/`prod` allow-list resolved from the corresponding
  `TRANSITJAZZ_LOG_WORKSPACE_ID_*` environment value; no arbitrary workspace ID is accepted.
- Tables are exactly `ContainerAppConsoleLogs` and `ContainerAppSystemLogs`.
- The request requires a finite UTC range, a single approved table, `project`, and a final `take` of
  1–100. Basic-incompatible operators and cross-resource syntax are rejected.
- The only network operation is caller-identity-only `POST /v1/workspaces/{workspaceId}/search`.
  The endpoint, method, resource audience, and arguments are constants in the helper; callers cannot
  provide a URL, method, header, shell command, or token.
- Results are recursively sanitized before output. No credential material is accepted or printed.

If the preferred interface and this helper cannot query Basic, report `BasicQueryUnsupported` and
record the approved Analytics fallback. Do not broaden the helper.

