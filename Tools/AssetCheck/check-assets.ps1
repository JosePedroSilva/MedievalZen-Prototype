<#
.SYNOPSIS
    Checks that the third-party asset packs MedievalZen needs are present in this working copy.

.DESCRIPTION
    The Synty packs, the polyperfect animals and the sound packs are licensed and weigh about
    8.5 GB, so they are git-ignored (see .gitignore) and every clone has to import them locally.
    This script reads Tools/AssetCheck/third-party-packs.txt, checks each pack's folder, file
    count and sentinel files, and lists what still has to be downloaded. It also looks for
    Git LFS files that are still pointers (a clone made without Git LFS installed).

    Runs on Windows PowerShell 5.1 and on PowerShell 7 (Windows, macOS, Linux). On Windows,
    check-assets.cmd next to this file runs it without touching the execution policy; on macOS
    without PowerShell, run check-assets.sh instead.

    Exit code 0: everything present. 1: something to download or fetch. 2: could not run.

.PARAMETER ProjectRoot
    The Unity project folder. Default: two levels above this script.

.PARAMETER Manifest
    The pack list to check against. Default: third-party-packs.txt next to this script.

.PARAMETER SkipLfs
    Do not look for unfetched Git LFS pointer files.

.EXAMPLE
    .\Tools\AssetCheck\check-assets.ps1

.EXAMPLE
    pwsh Tools/AssetCheck/check-assets.ps1 -ProjectRoot ~/Projects/MedievalZen
#>
[CmdletBinding()]
param(
    [string]$ProjectRoot,
    [string]$Manifest,
    [switch]$SkipLfs
)

$ErrorActionPreference = 'Continue'
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
if (-not $ProjectRoot) { $ProjectRoot = Join-Path $scriptDir '../..' }
if (-not (Test-Path -LiteralPath $ProjectRoot -PathType Container)) {
    Write-Host "Project folder not found: $ProjectRoot" -ForegroundColor Red
    exit 2
}
$ProjectRoot = (Resolve-Path -LiteralPath $ProjectRoot).Path.TrimEnd('\', '/')
if (-not $Manifest) { $Manifest = Join-Path $scriptDir 'third-party-packs.txt' }

if (-not (Test-Path -LiteralPath (Join-Path $ProjectRoot 'ProjectSettings/ProjectVersion.txt') -PathType Leaf)) {
    Write-Host "Not a Unity project root (no ProjectSettings/ProjectVersion.txt): $ProjectRoot" -ForegroundColor Red
    exit 2
}
if (-not (Test-Path -LiteralPath $Manifest -PathType Leaf)) {
    Write-Host "Manifest not found: $Manifest" -ForegroundColor Red
    exit 2
}

function Get-PackFileCount([string]$Dir) {
    # Files of the pack, .meta files excluded (Unity rewrites those freely).
    $n = 0
    Get-ChildItem -LiteralPath $Dir -Recurse -File -Force -ErrorAction SilentlyContinue |
        ForEach-Object { if ($_.Extension -ne '.meta') { $n++ } }
    return $n
}

function Test-LfsPointer([string]$Path) {
    # A Git LFS file that was never fetched is a small text file starting with this line.
    try {
        $stream = [System.IO.File]::OpenRead($Path)
        try {
            $buffer = New-Object byte[] 48
            $read = $stream.Read($buffer, 0, 48)
        } finally {
            $stream.Dispose()
        }
        $head = [System.Text.Encoding]::ASCII.GetString($buffer, 0, $read)
        return $head.StartsWith('version https://git-lfs.github.com/spec/v1')
    } catch {
        return $false
    }
}

# --- Read the manifest ---------------------------------------------------------------------
$packs = @()
$lineNo = 0
foreach ($raw in (Get-Content -LiteralPath $Manifest)) {
    $lineNo++
    $line = $raw.Trim()
    if ($line -eq '' -or $line.StartsWith('#')) { continue }
    $fields = @($line.Split('|') | ForEach-Object { $_.Trim() })
    if ($fields.Count -ne 6) {
        Write-Host "  manifest line $lineNo has $($fields.Count) fields, expected 6; skipped" -ForegroundColor Yellow
        continue
    }
    $sentinels = @()
    if ($fields[4] -ne '') {
        $sentinels = @($fields[4].Split(';') | ForEach-Object { $_.Trim() } | Where-Object { $_ -ne '' })
    }
    $packs += [pscustomobject]@{
        Name      = $fields[0]
        Folder    = $fields[1]
        MinFiles  = [int]$fields[2]
        Required  = ($fields[3].ToLowerInvariant() -eq 'required')
        Sentinels = $sentinels
        Source    = $fields[5]
    }
}

Write-Host ''
Write-Host 'MedievalZen third-party asset check' -ForegroundColor Cyan
Write-Host "  project:  $ProjectRoot"
Write-Host "  manifest: $Manifest"
Write-Host ''

# --- Check every pack ----------------------------------------------------------------------
$countOk = 0
$countMissing = 0
$countPartial = 0
$countOptionalMissing = 0
$toDownload = @()

foreach ($pack in $packs) {
    $dir = Join-Path $ProjectRoot $pack.Folder
    $status = ''
    $detail = ''
    if (-not (Test-Path -LiteralPath $dir -PathType Container)) {
        if ($pack.Required) { $status = 'MISSING'; $countMissing++ } else { $status = 'OPTIONAL'; $countOptionalMissing++ }
        $detail = 'folder not found'
    } else {
        $files = Get-PackFileCount $dir
        $missingSentinels = @()
        foreach ($s in $pack.Sentinels) {
            if (-not (Test-Path -LiteralPath (Join-Path $dir $s))) { $missingSentinels += $s }
        }
        if ($files -lt $pack.MinFiles -or $missingSentinels.Count -gt 0) {
            $status = 'PARTIAL'
            $countPartial++
            $parts = @()
            if ($files -lt $pack.MinFiles) { $parts += "$files files, expected at least $($pack.MinFiles)" }
            if ($missingSentinels.Count -gt 0) { $parts += "missing: $($missingSentinels -join ', ')" }
            $detail = $parts -join '; '
        } else {
            $status = 'OK'
            $countOk++
            $detail = "$files files"
        }
    }
    $color = switch ($status) { 'OK' { 'Green' } 'OPTIONAL' { 'DarkGray' } 'PARTIAL' { 'Yellow' } default { 'Red' } }
    Write-Host ('  [{0,-8}] ' -f $status) -ForegroundColor $color -NoNewline
    Write-Host ('{0,-44} {1}' -f $pack.Name, $pack.Folder)
    if ($status -ne 'OK') {
        Write-Host ('             {0}' -f $detail) -ForegroundColor $color
        if ($status -ne 'OPTIONAL') { $toDownload += $pack }
    }
}

# --- Git LFS pointers that were never fetched ----------------------------------------------
$lfsTracked = 0
$lfsPointers = @()
$lfsNote = ''
if ($SkipLfs) {
    $lfsNote = 'skipped (-SkipLfs)'
} elseif (-not (Get-Command git -ErrorAction SilentlyContinue)) {
    $lfsNote = 'git is not on PATH; LFS check skipped'
} else {
    $inside = & git -C $ProjectRoot rev-parse --is-inside-work-tree 2>$null
    if ($LASTEXITCODE -ne 0 -or "$inside".Trim() -ne 'true') {
        $lfsNote = 'not a git work tree; LFS check skipped'
    } else {
        # Paths go on the command line in batches: Windows PowerShell 5.1 would prepend a byte-order
        # mark and append CR to anything piped into `git check-attr --stdin`, and git would then
        # look up attributes for paths that do not exist.
        $tracked = @(& git -C $ProjectRoot ls-files)
        $attrs = @()
        $batch = @()
        $batchLength = 0
        foreach ($p in $tracked) {
            if ($batch.Count -gt 0 -and ($batchLength + $p.Length + 3) -gt 6000) {
                $attrs += @(& git -C $ProjectRoot check-attr filter -- $batch)
                $batch = @()
                $batchLength = 0
            }
            $batch += $p
            $batchLength += $p.Length + 3
        }
        if ($batch.Count -gt 0) { $attrs += @(& git -C $ProjectRoot check-attr filter -- $batch) }
        foreach ($line in $attrs) {
            if ($line -match '^(.*): filter: lfs$') {
                $lfsTracked++
                $rel = $Matches[1]
                if ($rel.Length -ge 2 -and $rel.StartsWith('"') -and $rel.EndsWith('"')) { $rel = $rel.Substring(1, $rel.Length - 2) }
                $full = Join-Path $ProjectRoot $rel
                if ((Test-Path -LiteralPath $full -PathType Leaf) -and (Test-LfsPointer $full)) { $lfsPointers += $rel }
            }
        }
    }
}

Write-Host ''
if ($lfsNote -ne '') {
    Write-Host "  Git LFS: $lfsNote" -ForegroundColor DarkGray
} elseif ($lfsPointers.Count -gt 0) {
    Write-Host ('  Git LFS: {0} of {1} LFS files are unfetched pointers, e.g. {2}' -f $lfsPointers.Count, $lfsTracked, $lfsPointers[0]) -ForegroundColor Red
    & git lfs version 2>$null | Out-Null
    if ($LASTEXITCODE -ne 0) {
        Write-Host '           install Git LFS (https://git-lfs.com), then run: git lfs install; git lfs pull' -ForegroundColor Red
    } else {
        Write-Host '           run: git lfs pull' -ForegroundColor Red
    }
} else {
    Write-Host ('  Git LFS: {0} tracked files, all fetched' -f $lfsTracked) -ForegroundColor Green
}

# --- Summary -------------------------------------------------------------------------------
Write-Host ''
Write-Host ('  {0} ok, {1} missing, {2} partial, {3} optional not installed' -f $countOk, $countMissing, $countPartial, $countOptionalMissing)
if ($toDownload.Count -gt 0) {
    Write-Host ''
    Write-Host '  To download or re-import:' -ForegroundColor Yellow
    foreach ($pack in $toDownload) {
        Write-Host ('   - {0}' -f $pack.Name) -ForegroundColor Yellow
        Write-Host ('       into:  {0}' -f $pack.Folder)
        Write-Host ('       from:  {0}' -f $pack.Source)
    }
    Write-Host ''
    Write-Host '  Import each pack with its own .meta files (the .unitypackage from the store, or a copy of the folder'
    Write-Host '  from another checkout): first-party prefabs, scenes and palettes reference these assets by GUID.'
}
Write-Host ''
$failed = ($countMissing -gt 0) -or ($countPartial -gt 0) -or ($lfsPointers.Count -gt 0)
if ($failed) {
    Write-Host '  RESULT: FAIL - the project will not open cleanly until the items above are in place.' -ForegroundColor Red
    exit 1
}
Write-Host '  RESULT: OK - every required pack is present.' -ForegroundColor Green
exit 0
