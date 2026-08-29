[CmdletBinding()]
param(
    [ValidateSet('Check', 'Sync', 'Watch')]
    [string]$Mode = 'Sync',

    [switch]$Adopt,

    [string[]]$Targets = @('claude', 'codex', 'opencode')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$skillsRoot = Join-Path $repoRoot 'skills'
$catalogPath = Join-Path $skillsRoot '_skill-sync\catalog.json'
$targetRoots = [ordered]@{
    claude   = Join-Path $repoRoot '.claude\skills'
    codex    = Join-Path $repoRoot '.agents\skills'
    opencode = Join-Path $repoRoot '.opencode\skills'
}
$selectedTargets = @(
    $Targets | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim().ToLowerInvariant() } |
        Where-Object { $_ } | Select-Object -Unique
)

foreach ($target in $selectedTargets) {
    if (-not $targetRoots.Contains($target)) {
        throw "Unknown target '$target'. Use claude, codex, or opencode."
    }
}

function Write-Utf8File {
    param([string]$Path, [string]$Content)
    [System.IO.File]::WriteAllText($Path, $Content, [System.Text.UTF8Encoding]::new($false))
}

function Get-FileList {
    param([string]$Root)
    if (-not (Test-Path -LiteralPath $Root)) { return @() }

    $prefix = (Resolve-Path -LiteralPath $Root).Path.TrimEnd('\') + '\'
    return @(
        Get-ChildItem -LiteralPath $Root -File -Recurse -Force | Sort-Object FullName | ForEach-Object {
            $relative = $_.FullName.Substring($prefix.Length).Replace('\', '/')
            "$relative|$((Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash)"
        }
    )
}

function Test-DirectoryEqual {
    param([string]$Left, [string]$Right)
    $leftFiles = @(Get-FileList $Left)
    $rightFiles = @(Get-FileList $Right)
    return $null -eq (Compare-Object -ReferenceObject $leftFiles -DifferenceObject $rightFiles)
}

function Assert-PortableSkill {
    param([string]$Name, [string]$Directory)

    $path = Join-Path $Directory 'SKILL.md'
    if (-not (Test-Path -LiteralPath $path)) { throw "$Name is missing SKILL.md." }
    if ($Name -notmatch '^[a-z0-9]+(-[a-z0-9]+)*$') { throw "$Name must be lowercase kebab-case." }

    $content = Get-Content -LiteralPath $path -Raw
    $frontMatter = [regex]::Match($content, '\A---\r?\n(?<header>.*?)\r?\n---\r?\n.+', 'Singleline')
    if (-not $frontMatter.Success) { throw "$Name must begin with frontmatter and a Markdown body." }
    if ($frontMatter.Groups['header'].Value -notmatch "(?m)^name:\s*$([regex]::Escape($Name))\s*$") {
        throw "$Name must have matching name frontmatter."
    }
    if ($frontMatter.Groups['header'].Value -notmatch '(?m)^description:\s*\S+') {
        throw "$Name must have a non-empty description."
    }
    foreach ($token in @('.claude/skills', '.agents/skills', '.opencode/skills', '${CLAUDE_', '$ARGUMENTS', '!`')) {
        if ($content.Contains($token)) { throw "$Name contains host-specific '$token'. Use an overlay instead." }
    }
}

function Add-Overlay {
    param([string]$Source, [string]$Stage, [string]$Target)

    $overlayPath = Join-Path $Source ('.skill-sync\' + $Target + '.json')
    if (-not (Test-Path -LiteralPath $overlayPath)) { return }
    $overlay = Get-Content -LiteralPath $overlayPath -Raw | ConvertFrom-Json
    foreach ($property in $overlay.PSObject.Properties) {
        if (@('frontmatterLines', 'assets') -notcontains $property.Name) {
            throw "$overlayPath has unsupported property '$($property.Name)'."
        }
    }
    $frontmatterLines = if ($null -ne $overlay.PSObject.Properties['frontmatterLines']) { $overlay.frontmatterLines } else { $null }
    $assets = if ($null -ne $overlay.PSObject.Properties['assets']) { $overlay.assets } else { $null }

    if ($null -ne $frontmatterLines) {
        $skillPath = Join-Path $Stage 'SKILL.md'
        $skill = Get-Content -LiteralPath $skillPath -Raw
        $match = [regex]::Match($skill, '\A---\r?\n(?<header>.*?)\r?\n---(?<body>\r?\n.*)\z', 'Singleline')
        if (-not $match.Success) { throw "$skillPath has invalid frontmatter." }
        $lines = @($frontmatterLines | ForEach-Object { [string]$_ })
        if ($lines.Count -eq 0 -or $lines | Where-Object { -not $_ -or $_ -match '^---\s*$' }) {
            throw "$overlayPath has invalid frontmatterLines."
        }
        Write-Utf8File $skillPath ("---`n$($match.Groups['header'].Value.TrimEnd())`n$($lines -join "`n")`n---$($match.Groups['body'].Value)")
    }

    if ($null -ne $assets) {
        $assetRoot = Join-Path $Source ('.skill-sync\' + $Target)
        foreach ($asset in @($assets)) {
            $from = [string]$asset.from; $to = [string]$asset.to
            if (-not $from -or -not $to -or [IO.Path]::IsPathRooted($from) -or [IO.Path]::IsPathRooted($to) -or $from -match '(^|[\\/])\.\.' -or $to -match '(^|[\\/])\.\.') {
                throw "$overlayPath contains an unsafe asset path."
            }
            $sourceAsset = Join-Path $assetRoot $from
            if (-not (Test-Path -LiteralPath $sourceAsset -PathType Leaf)) { throw "$sourceAsset does not exist." }
            $destinationAsset = Join-Path $Stage $to
            New-Item -ItemType Directory -Path (Split-Path -Parent $destinationAsset) -Force | Out-Null
            Copy-Item -LiteralPath $sourceAsset -Destination $destinationAsset -Force
        }
    }
}

function Render-Plans {
    param($Catalog, [string]$StageRoot)

    $plans = @()
    foreach ($entry in $Catalog.skills.PSObject.Properties | Sort-Object Name) {
        $name = $entry.Name
        $source = Join-Path $skillsRoot $name
        Assert-PortableSkill $name $source
        foreach ($target in @($entry.Value.targets)) {
            $target = [string]$target
            if ($selectedTargets -notcontains $target) { continue }
            if (-not $targetRoots.Contains($target)) { throw "$name targets unknown host '$target'." }

            $stage = Join-Path $StageRoot (Join-Path $target $name)
            New-Item -ItemType Directory -Path $stage -Force | Out-Null
            Get-ChildItem -LiteralPath $source -Force | Where-Object Name -ne '.skill-sync' |
                ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination $stage -Recurse -Force }
            Add-Overlay $source $stage $target
            $plans += [pscustomobject]@{
                Name = $name; Target = $target; Stage = $stage; Destination = Join-Path $targetRoots[$target] $name
            }
        }
    }
    return $plans
}

function Test-TargetManaged {
    param([string]$Target)
    return Test-Path -LiteralPath (Join-Path $targetRoots[$Target] '.skill-sync.manifest.json')
}

function Assert-DirectoryWritable {
    param([string]$Directory)
    if (-not (Test-Path -LiteralPath $Directory)) { return }
    foreach ($file in Get-ChildItem -LiteralPath $Directory -File -Recurse -Force) {
        try {
            $stream = [IO.File]::Open($file.FullName, [IO.FileMode]::Open, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
            $stream.Dispose()
        }
        catch {
            throw "Cannot update '$($file.FullName)'. Close or restart the agent holding this skill, then rerun sync."
        }
    }
}

function Write-Manifest {
    param([string]$Target, $Plans)
    $root = $targetRoots[$Target]
    New-Item -ItemType Directory -Path $root -Force | Out-Null
    $manifest = [ordered]@{ schemaVersion = 1; skills = @($Plans | Where-Object Target -eq $Target | ForEach-Object Name | Sort-Object) }
    Write-Utf8File (Join-Path $root '.skill-sync.manifest.json') ($manifest | ConvertTo-Json)
}

function Invoke-Sync {
    param([switch]$WriteChanges, [switch]$AdoptExisting)

    if (-not (Test-Path -LiteralPath $catalogPath)) { throw "Skill catalog not found at $catalogPath." }
    $catalog = Get-Content -LiteralPath $catalogPath -Raw | ConvertFrom-Json
    if ($catalog.schemaVersion -ne 1 -or $null -eq $catalog.skills) { throw 'Invalid skill catalog.' }

    $stageRoot = Join-Path ([IO.Path]::GetTempPath()) ('transit-jazz-skill-sync-' + [guid]::NewGuid())
    try {
        New-Item -ItemType Directory -Path $stageRoot -Force | Out-Null
        $plans = Render-Plans $catalog $stageRoot
        foreach ($plan in $plans) {
            if ((Test-Path -LiteralPath $plan.Destination) -and -not (Test-TargetManaged $plan.Target) -and -not $AdoptExisting) {
                throw "Refusing to replace unmanaged $($plan.Target)/$($plan.Name). Rerun with -Adopt after reviewing it."
            }
        }

        $changed = @($plans | Where-Object { -not (Test-DirectoryEqual $_.Stage $_.Destination) })
        foreach ($plan in $plans) {
            $status = if ($changed -contains $plan) { if ($WriteChanges) { 'sync' } else { 'would sync' } } else { 'unchanged' }
            Write-Host ("[{0}] {1}: {2}" -f $plan.Target, $plan.Name, $status)
        }
        if (-not $WriteChanges) { return }

        foreach ($plan in $changed) { Assert-DirectoryWritable $plan.Destination }
        $backupRoot = $null
        foreach ($plan in $changed) {
            if ($AdoptExisting -and (Test-Path -LiteralPath $plan.Destination) -and -not (Test-TargetManaged $plan.Target)) {
                if ($null -eq $backupRoot) { $backupRoot = Join-Path $repoRoot ('.skill-sync-backups\' + (Get-Date -Format 'yyyyMMdd-HHmmss')) }
                $backup = Join-Path $backupRoot (Join-Path $plan.Target $plan.Name)
                New-Item -ItemType Directory -Path (Split-Path -Parent $backup) -Force | Out-Null
                Copy-Item -LiteralPath $plan.Destination -Destination $backup -Recurse -Force
            }
            if (Test-Path -LiteralPath $plan.Destination) { Remove-Item -LiteralPath $plan.Destination -Recurse -Force }
            New-Item -ItemType Directory -Path (Split-Path -Parent $plan.Destination) -Force | Out-Null
            Copy-Item -LiteralPath $plan.Stage -Destination $plan.Destination -Recurse -Force
        }
        foreach ($target in $selectedTargets) { Write-Manifest $target $plans }
    }
    finally {
        if (Test-Path -LiteralPath $stageRoot) { Remove-Item -LiteralPath $stageRoot -Recurse -Force }
    }
}

if ($Mode -eq 'Watch') {
    $watcher = [IO.FileSystemWatcher]::new($skillsRoot)
    $watcher.IncludeSubdirectories = $true; $watcher.EnableRaisingEvents = $true
    $script:pending = $true; $script:lastChange = Get-Date
    $action = { $script:pending = $true; $script:lastChange = Get-Date }
    $events = @('Changed', 'Created', 'Deleted', 'Renamed' | ForEach-Object { Register-ObjectEvent -InputObject $watcher -EventName $_ -Action $action })
    Write-Host 'Watching skills/. Press Ctrl+C to stop.'
    try {
        while ($true) {
            Start-Sleep -Milliseconds 500
            if ($script:pending -and ((Get-Date) - $script:lastChange).TotalMilliseconds -ge 500) {
                $script:pending = $false; Invoke-Sync -WriteChanges
            }
        }
    }
    finally { $events | ForEach-Object { Unregister-Event -SubscriptionId $_.Id -ErrorAction SilentlyContinue }; $watcher.Dispose() }
}
elseif ($Mode -eq 'Sync') { Invoke-Sync -WriteChanges -AdoptExisting:$Adopt }
else { Invoke-Sync -AdoptExisting:$Adopt }
