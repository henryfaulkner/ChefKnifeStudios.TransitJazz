---
name: grafana
description: Investigate Grafana metrics, dashboards, panels, and read-only alert firing history through configured Grafana tools or gcx. Use when a user asks to query monitoring data, open a Grafana dashboard link or UID, inspect panel PromQL, review alerts that fired, or diagnose Grafana access. Do not use for creating, editing, deleting, or administering Grafana resources.
---

# Investigate Grafana

Use the configured Grafana integration as a read-only investigation tool. Grafana remains the place for visualization, dashboard editing, alert management, data-source configuration, users, and other administration.

## Find the available interface

- When the user explicitly requests `gcx`, check that command first and inspect `gcx --help` plus only relevant subcommand help. Use a documented read-only alert-history capability when available; do not guess command names, flags, or query syntax.
- Otherwise prefer a registered Grafana tool when available, with an installed Grafana command-line tool as a fallback.
- Treat the tool schema or command help as authoritative. If exact commands or flags are not already known, inspect the top-level help and only the relevant subcommand help before proceeding.
- If the requested command is unavailable, use another already-configured Grafana interface only if present. Do not install tools or bypass the integration with raw Grafana HTTP requests because authentication is intended to remain automatic and hidden. If no interface is available, explain what is missing.

## Preserve the read-only boundary

Use only capabilities that:

- run instant or range PromQL queries;
- retrieve dashboards and their panels;
- inspect a panel's data source and PromQL;
- retrieve alert state history or firing events; and
- diagnose authentication, connectivity, and authorization with `doctor`.

Never create, edit, import, delete, or administer dashboards, folders, data sources, alerts, annotations, users, service accounts, API keys, or other Grafana resources. If the user asks for a mutation, explain that this integration is read-only and direct them to Grafana's normal editing or administrative interface.

## Run PromQL

1. Preserve the user's PromQL exactly unless they ask for help refining it. For a refinement, show or describe the change so the new expression is auditable.
2. Map the requested time range, query resolution or step, and output format to the tool's supported arguments.
3. If the user supplies no time range, use a modest investigative range supported by the tool and state the chosen range. When continuing from a dashboard, prefer the dashboard URL's range.
4. Use human-readable table output for interactive work. Use JSON when the user requests structured output or the result will feed a script or deeper programmatic analysis.
5. Report the evaluated expression, effective range, and resolution with the result. Distinguish returned data from interpretation.

Keep broad queries bounded. Narrow the time range or label set when a query would return excessive data, but do not silently change its meaning.

## Review alert firing history

1. Use the read-only alert-history or state-history query exposed by the selected Grafana tool or CLI. Do not use alert-rule, silence, notification-policy, or contact-point mutation commands.
2. Respect any time range, alert name, rule UID, folder, namespace, and labels supplied by the user. If no range is supplied, use the last seven days when supported and state the exact range and timezone.
3. Include returned transitions into a firing state and resolved/normal transitions when available. Preserve the source's state names; current state alone is not full history.
4. Summarize each result with its alert/rule name and UID when available, firing time, resolved time or current status, relevant labels, and history source. Keep annotations concise and omit sensitive label values.
5. State reported history retention or backend limitations. Alertmanager notifications, incidents, and Grafana rule state history are different records; identify which source was queried. A zero-row result means only that no matching records were returned for that source and range.

Keep history queries bounded to the requested period and filters. If an alert name matches multiple rules, show the matches and ask which one to inspect before giving a detailed history.

## Investigate dashboards and panels

- Accept either a copied Grafana URL or a dashboard UID. Prefer the URL when both are available because it may carry the time range, variable selections, and panel context.
- Preserve URL time parameters and template variables. Explicit values in the user's request override values copied from the URL.
- Without a panel selection, retrieve the dashboard and summarize its relevant panels. When the user identifies a panel, narrow the retrieval to that panel.
- Select panels by stable panel ID when available. If a title matches multiple panels, show the matches and ask which one the user means rather than guessing.
- Show a panel's PromQL when the user asks for it or when moving from a suspicious visualization into detailed metric analysis. Include the panel's data source and effective variable values when available.
- Do not claim that a panel query was executed when the tool returned only dashboard metadata or a query definition.

The preferred investigative path is:

1. Open the dashboard URL or UID.
2. Identify the suspicious panel and its effective time range and variables.
3. Inspect the panel's PromQL.
4. Rerun that PromQL independently.
5. Refine the query while preserving the original as context.

## Authentication and diagnostics

- Let the integration acquire short-lived authentication automatically. Never ask the user to copy a token, inspect credential files, or paste secrets into the conversation.
- Never print tokens, authorization headers, secret environment values, or credential-file contents, including in debug output.
- When a Grafana operation fails, run `doctor` before speculating. Use its result to distinguish among authentication acquisition, network or Grafana connectivity, and Grafana permissions or resource access.
- Report the failing layer and a secret-free next action. Avoid repeated retries when `doctor` identifies a persistent configuration or permission problem.

## Present the investigation

Lead with the finding. Include enough provenance to reproduce it: dashboard UID or link, panel ID or title, PromQL, effective variables, time range, and resolution as applicable. Keep normal output concise and human-readable; preserve structured JSON when the user requested it.
