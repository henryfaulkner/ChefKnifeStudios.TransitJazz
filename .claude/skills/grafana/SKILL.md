---
name: grafana
description: Investigate Grafana metrics read-only by running PromQL and retrieving dashboards or panels through the configured Grafana tool. Use when a user asks to query monitoring data, open a Grafana dashboard link or UID, inspect panel PromQL, connect a panel to a refined query, or diagnose Grafana access. Do not use for creating, editing, deleting, or administering Grafana resources.
---

# Investigate Grafana

Use the configured Grafana integration as a read-only investigation tool. Grafana remains the place for visualization, dashboard editing, alert management, data-source configuration, users, and other administration.

## Find the available interface

- Prefer a registered Grafana tool when one is available; otherwise use the installed Grafana command-line tool.
- Treat the tool schema or command help as authoritative. If exact commands or flags are not already known, inspect the top-level help and only the relevant subcommand help before proceeding.
- If no Grafana integration is available, explain that the integration is not configured. Do not bypass it with raw Grafana HTTP requests because authentication is intended to remain automatic and hidden.

## Preserve the read-only boundary

Use only capabilities that:

- run instant or range PromQL queries;
- retrieve dashboards and their panels;
- inspect a panel's data source and PromQL; or
- diagnose authentication, connectivity, and authorization with `doctor`.

Never create, edit, import, delete, or administer dashboards, folders, data sources, alerts, annotations, users, service accounts, API keys, or other Grafana resources. If the user asks for a mutation, explain that this integration is read-only and direct them to Grafana's normal editing or administrative interface.

## Run PromQL

1. Preserve the user's PromQL exactly unless they ask for help refining it. For a refinement, show or describe the change so the new expression is auditable.
2. Map the requested time range, query resolution or step, and output format to the tool's supported arguments.
3. If the user supplies no time range, use a modest investigative range supported by the tool and state the chosen range. When continuing from a dashboard, prefer the dashboard URL's range.
4. Use human-readable table output for interactive work. Use JSON when the user requests structured output or the result will feed a script or deeper programmatic analysis.
5. Report the evaluated expression, effective range, and resolution with the result. Distinguish returned data from interpretation.

Keep broad queries bounded. Narrow the time range or label set when a query would return excessive data, but do not silently change its meaning.

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
