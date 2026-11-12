$ErrorActionPreference = 'Stop'

$alertDirectory = $PSScriptRoot
$postgresDatasourceUid = 'cg086lcj6z6yob'
$metricsDatasourceUid = '__SET_METRICS_DATASOURCE_UID__'
if (-not [string]::IsNullOrWhiteSpace($env:CLIENT_EXPERIENCE_METRICS_DATASOURCE_UID)) {
    $metricsDatasourceUid = $env:CLIENT_EXPERIENCE_METRICS_DATASOURCE_UID
}

function New-ExpressionData {
    param([double] $Threshold)

    return @(
        @{
            refId = 'B'
            datasourceUid = '__expr__'
            model = @{
                conditions = @()
                datasource = @{ type = '__expr__'; uid = '__expr__' }
                expression = 'A'
                intervalMs = 1000
                maxDataPoints = 43200
                reducer = 'last'
                refId = 'B'
                settings = @{ mode = 'dropNN' }
                type = 'reduce'
            }
        },
        @{
            refId = 'C'
            datasourceUid = '__expr__'
            model = @{
                conditions = @(
                    @{
                        evaluator = @{ params = @($Threshold); type = 'gt' }
                        operator = @{ type = 'and' }
                        query = @{ params = @('B') }
                        reducer = @{ params = @(); type = 'last' }
                        type = 'query'
                    }
                )
                datasource = @{ type = '__expr__'; uid = '__expr__' }
                expression = 'B'
                intervalMs = 1000
                maxDataPoints = 43200
                refId = 'C'
                type = 'threshold'
            }
        }
    )
}

function New-SqlAlert {
    param(
        [string] $Uid,
        [string] $Title,
        [string] $SqlFile,
        [string] $Pending,
        [string] $Severity,
        [string] $Summary,
        [string] $Description,
        [int] $PanelId = 1
    )

    $rawSql = [System.IO.File]::ReadAllText((Join-Path $alertDirectory "sql/$SqlFile"))
    return @{
        uid = $Uid
        title = $Title
        condition = 'C'
        data = @(
            @{
                refId = 'A'
                queryType = ''
                relativeTimeRange = @{ from = 900; to = 0 }
                datasourceUid = $postgresDatasourceUid
                model = @{
                    datasource = @{ type = 'grafana-postgresql-datasource'; uid = $postgresDatasourceUid }
                    editorMode = 'code'
                    format = 'time_series'
                    intervalMs = 1000
                    maxDataPoints = 43200
                    rawQuery = $true
                    rawSql = $rawSql
                    refId = 'A'
                    sql = @{ columns = @(); groupBy = @(); limit = 50 }
                }
            }
        ) + (New-ExpressionData -Threshold 0)
        dashboardUid = 'transitjazz-client-experience'
        panelId = $PanelId
        noDataState = 'Alerting'
        execErrState = 'Alerting'
        missing_series_evals_to_resolve = 6
        'for' = $Pending
        keepFiringFor = '2m'
        annotations = @{
            summary = $Summary
            description = $Description
        }
        labels = @{
            alert_group = 'client_experience'
            severity = $Severity
        }
        isPaused = $false
    }
}

function New-PrometheusAlert {
    param(
        [string] $Uid,
        [string] $Title,
        [string] $Expression,
        [string] $Comparator,
        [double] $Threshold,
        [string] $Pending,
        [string] $Severity,
        [string] $Summary,
        [string] $Description
    )

    $expressionData = New-ExpressionData -Threshold $Threshold
    if ($Comparator -eq 'lt') {
        $expressionData[1].model.conditions[0].evaluator.type = 'lt'
    }

    return @{
        uid = $Uid
        title = $Title
        condition = 'C'
        data = @(
            @{
                refId = 'A'
                queryType = ''
                relativeTimeRange = @{ from = 900; to = 0 }
                datasourceUid = $metricsDatasourceUid
                model = @{
                    datasource = @{ type = 'prometheus'; uid = $metricsDatasourceUid }
                    editorMode = 'code'
                    expr = $Expression
                    intervalMs = 1000
                    maxDataPoints = 43200
                    instant = $true
                    range = $false
                    refId = 'A'
                }
            }
        ) + $expressionData
        noDataState = 'Alerting'
        execErrState = 'Alerting'
        missing_series_evals_to_resolve = 6
        'for' = $Pending
        keepFiringFor = '2m'
        annotations = @{ summary = $Summary; description = $Description }
        labels = @{ alert_group = 'client_experience'; severity = $Severity }
        isPaused = $false
    }
}

$healthRules = @(
    (New-SqlAlert 'ce_journey_failure_high' 'Client journey failures elevated' 'journey-failure.sql' '3m' 'warning' `
        'Confirmed journey failures reached 10% with adequate fresh traffic.' `
        'The query enforces N >= 20, three affected sessions, a 15-minute receipt-time cohort ending two minutes ago, and unresolved share <= 10%.')
    (New-SqlAlert 'ce_journey_coverage_advisory' 'Client journey coverage low' 'journey-coverage.sql' '3m' 'advisory' `
        'Unresolved journey starts exceed 10% of an adequately sized cohort.' `
        'This advisory suppresses the journey-health conclusion until result coverage recovers.')
    (New-SqlAlert 'ce_capability_degradation_high' 'Client capability degradation elevated' 'capability-degradation.sql' '3m' 'warning' `
        'Degraded or unavailable capability time reached 10% with adequate observation.' `
        'The query requires ten observed session-minutes, three sessions, fully bounded recent windows, and unknown plus gap coverage at or below 10%.')
    (New-SqlAlert 'ce_capability_coverage_advisory' 'Client capability coverage low' 'capability-coverage.sql' '3m' 'advisory' `
        'Unknown plus gap time exceeds 10% of an adequately observed capability window.' `
        'This advisory suppresses the capability-health conclusion until coverage recovers.')
    (New-SqlAlert 'ce_audio_stop_still_playing' 'Audio stop remains active' 'audio-stop-still-playing.sql' '0s' 'critical' `
        'A fresh confirmed audio-stop result reports still_playing.' `
        'The rule requires a matching stop attempt and trustworthy event delivery within two minutes; presentation and traffic volume do not gate it.')
    (New-SqlAlert 'ce_toast_volume_advisory' 'Client toast volume elevated' 'toast-volume-advisory.sql' '0s' 'advisory' `
        'Committed warning or error presentations exceeded the diagnostic volume threshold.' `
        'Diagnostic only: 10 errors across three sessions in five minutes, or 30 warnings across five sessions in ten minutes.')
)

$maintenanceRules = @(
    (New-SqlAlert 'ce_trend_maintenance_stalled' 'Client trend maintenance stalled' 'maintenance-stalled.sql' '10m' 'warning' `
        'The client-experience aggregation watermark is missing or more than two hours behind.' `
        'Repeated cleanup failures are reported by the separate maintenance failure counter rule.')
)

$collectionRules = @(
    (New-PrometheusAlert 'ce_ingestion_persistence_failures' 'Client experience persistence failures' `
        'sum(increase({__name__="client_experience_intake_requests_total",outcome="persistence_unavailable"}[5m])) or vector(0)' `
        'gt' 4 '2m' 'critical' 'Five or more client-experience persistence failures occurred in five minutes.' `
        'Check API database connectivity, schema readiness, and transaction errors; this counter excludes client-side loss.')
    (New-PrometheusAlert 'ce_database_probe_unavailable' 'Client experience database probe unavailable' `
        'sum(increase({__name__="client_experience_probe_runs_total",outcome=~"unavailable|timeout"}[2m])) or vector(0)' `
        'gt' 0 '2m' 'critical' 'The intake database probe reported unavailable storage or timed out.' `
        'Check the health-probe endpoint and database/schema status. Query execution errors use Grafana Error state.')
    (New-PrometheusAlert 'ce_database_probe_missing' 'Client experience database probe series missing' `
        'count(count_over_time({__name__="client_experience_probe_runs_total"}[10m])) or vector(0)' `
        'lt' 1 '2m' 'critical' 'No database-probe metric samples were scraped in ten minutes.' `
        'Verify that the health endpoint is being called and the OpenTelemetry metrics pipeline exports the probe meter.')
    (New-PrometheusAlert 'ce_maintenance_repeated_failures' 'Client trend maintenance repeatedly failing' `
        'sum(increase({__name__="client_experience_maintenance_runs_total",outcome="failed"}[30m])) or vector(0)' `
        'gt' 1 '10m' 'warning' 'At least two client-experience maintenance runs failed in thirty minutes.' `
        'Review aggregation and retention health; a single run failure does not trigger this alert.')
)

$syntheticRules = @(
    (New-PrometheusAlert 'ce_synthetic_app_readiness_failure' 'TransitJazz browser readiness failed twice' `
        'min(min_over_time({__name__="transitjazz_synthetic_app_readiness_failure"}[6m]))' `
        'gt' 0 '0s' 'critical' 'The scheduled browser check failed app readiness on consecutive runs.' `
        'The five-minute Grafana browser check reports shell/map/live readiness separately from runner failures.')
    (New-PrometheusAlert 'ce_synthetic_runner_failure' 'TransitJazz browser check runner failed' `
        'max(max_over_time({__name__="transitjazz_synthetic_runner_failure"}[10m]))' `
        'gt' 0 '0s' 'warning' 'The scheduled browser-check runner or browser setup failed.' `
        'Investigate runner configuration, browser startup, and probe execution; this is separate from application readiness.')
)

$document = [ordered]@{
    apiVersion = 1
    groups = @(
        @{
            orgId = 1
            name = 'transitjazz-client-experience-health'
            folder = 'TransitJazz'
            interval = '1m'
            rules = $healthRules
        },
        @{
            orgId = 1
            name = 'transitjazz-client-experience-maintenance'
            folder = 'TransitJazz'
            interval = '5m'
            rules = $maintenanceRules
        },
        @{
            orgId = 1
            name = 'transitjazz-client-experience-collection'
            folder = 'TransitJazz'
            interval = '1m'
            rules = $collectionRules
        },
        @{
            orgId = 1
            name = 'transitjazz-client-experience-synthetic'
            folder = 'TransitJazz'
            interval = '5m'
            rules = $syntheticRules
        }
    )
}

$outputPath = Join-Path $alertDirectory 'transitjazz-client-experience-alerts.json'
$json = ConvertTo-Json -InputObject $document -Depth 100
[System.IO.File]::WriteAllText($outputPath, $json + [Environment]::NewLine, [System.Text.UTF8Encoding]::new($false))
Write-Output "Wrote $outputPath"
