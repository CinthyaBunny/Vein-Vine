<#
.SYNOPSIS
    Moves Vein & Vine to a new version: both manifests, the changelog, and
    optionally the commit and tag.

.DESCRIPTION
    The version lives in two files that nothing keeps in step - <Version> in
    VeinAndVine.csproj, which DalamudPackager stamps into the manifest inside
    latest.zip, and "AssemblyVersion" in repo.json, which the in-game installer
    compares against what the user already has. Letting them drift fails
    quietly: the installer either never offers the update, or offers one that
    appears not to apply.

    This script is the one place that moves them, and it also promotes the
    changelog's [Unreleased] section into a dated release and copies that text
    into both manifests, so what changed shows up in-game rather than only on
    GitHub.

    Nothing is written unless every edit can be applied - the script resolves
    all four files first and fails before touching any of them if one doesn't
    look the way it expects.

.PARAMETER Version
    The new four-part version, e.g. 0.1.0.0. Dalamud requires four parts. Must
    be higher than the current one: the installer compares versions, so going
    backwards means nobody is offered the update.

.PARAMETER Commit
    Also stage the changed files, commit them, and create an annotated v<version>
    tag. Off by default - a tag is awkward to retract once pushed. Nothing is
    ever pushed.

.PARAMETER SkipBuild
    Skip the Release build. By default the script builds afterwards, which both
    exercises the csproj version guard and leaves you the latest.zip to attach
    to the release.

.PARAMETER DryRun
    Print what would change and write nothing.

.PARAMETER Root
    Repository root. Defaults to the parent of the folder holding this script.

.EXAMPLE
    .\tools\bump-version.ps1 -Version 0.1.0.0 -DryRun

.EXAMPLE
    .\tools\bump-version.ps1 -Version 0.1.0.0 -Commit
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^\d+\.\d+\.\d+\.\d+$')]
    [string] $Version,

    [switch] $Commit,
    [switch] $SkipBuild,
    [switch] $DryRun,
    [string] $Root
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not $Root) {
    $Root = Split-Path -Parent $PSScriptRoot
}

$paths = @{
    Csproj    = Join-Path $Root 'VeinAndVine\VeinAndVine.csproj'
    Repo      = Join-Path $Root 'repo.json'
    Manifest  = Join-Path $Root 'VeinAndVine\VeinAndVine.json'
    Changelog = Join-Path $Root 'CHANGELOG.md'
}

foreach ($name in $paths.Keys) {
    if (-not (Test-Path $paths[$name])) {
        throw "Missing $name at $($paths[$name])."
    }
}

# ---------------------------------------------------------------- helpers ---

# The tree is pinned to LF by .gitattributes, so writing CRLF would show every
# touched file as wholly rewritten. UTF8Encoding($false) = no BOM, matching
# what is already on disk.
function Read-Text([string] $Path) {
    return [System.IO.File]::ReadAllText($Path)
}

function Write-Text([string] $Path, [string] $Text) {
    $lf = $Text -replace "`r`n", "`n"
    [System.IO.File]::WriteAllText($Path, $lf, (New-Object System.Text.UTF8Encoding($false)))
}

# Replaces exactly one occurrence, and refuses rather than guessing. A silent
# zero-match here is the whole failure mode this script exists to prevent.
function Set-Single([string] $Text, [string] $Pattern, [string] $Replacement, [string] $What) {
    $matched = [regex]::Matches($Text, $Pattern)
    if ($matched.Count -ne 1) {
        throw "Expected exactly one $What, found $($matched.Count). Pattern: $Pattern"
    }
    return [regex]::Replace($Text, $Pattern, $Replacement)
}

<#
    Runs a native tool, failing only on a non-zero exit code.

    Necessary because $ErrorActionPreference = 'Stop' turns anything a native
    command writes to stderr into a terminating error, and both git and dotnet
    use stderr for ordinary chatter - a CRLF warning from git would otherwise
    abort the release between writing the files and committing them.

    Deliberately not an advanced function: with no param block every token
    lands in $args verbatim. Given one, PowerShell would bind the tool's own
    switches as though they were this function's - "git tag -a" fails, because
    -a prefix-matches an -Arguments parameter.
#>
function Invoke-Tool {
    $tool = $args[0]
    $rest = @($args | Select-Object -Skip 1)

    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        & $tool @rest 2>&1 | ForEach-Object { Write-Host "  $_" }
        if ($LASTEXITCODE -ne 0) {
            throw "$tool $($rest -join ' ') failed with exit code $LASTEXITCODE."
        }
    }
    finally {
        $ErrorActionPreference = $previous
    }
}

function ConvertTo-JsonString([string] $Text) {
    $escaped = $Text.Replace('\', '\\').Replace('"', '\"')
    $escaped = $escaped -replace "`r`n", '\n'
    $escaped = $escaped -replace "`n", '\n'
    $escaped = $escaped -replace "`t", '\t'
    return $escaped
}

<#
    Flattens a changelog section into the plain text Dalamud shows in the
    installer: "### Added" becomes "Added:", wrapped bullets are joined back
    onto one line, and markdown emphasis is dropped since nothing renders it.
#>
function ConvertTo-PlainNotes([string] $Markdown) {
    $out = New-Object System.Collections.Generic.List[string]

    foreach ($raw in ($Markdown -split "`n")) {
        $line = $raw.TrimEnd()

        if ($line -match '^\s*$') { continue }

        if ($line -match '^#{2,}\s*(.+)$') {
            $out.Add('') | Out-Null
            $out.Add(($Matches[1].Trim() + ':')) | Out-Null
            continue
        }

        if ($line -match '^\s*[-*]\s+(.*)$') {
            $out.Add('- ' + $Matches[1].Trim()) | Out-Null
            continue
        }

        # A continuation of the bullet above; markdown wraps, plain text should not.
        if ($out.Count -gt 0 -and $out[$out.Count - 1] -ne '') {
            $out[$out.Count - 1] = $out[$out.Count - 1] + ' ' + $line.Trim()
        }
        else {
            $out.Add($line.Trim()) | Out-Null
        }
    }

    $text = ($out -join "`n").Trim()

    # Emphasis and code spans, in that order: **bold** first so the leftover
    # single-asterisk pass doesn't chew through half of it. Targeted rather
    # than stripping every asterisk, which would mangle any that are literal.
    $text = $text -replace '\*\*([^*]+)\*\*', '$1'
    $text = $text -replace '\*([^*\n]+)\*', '$1'
    $text = $text -replace '`([^`\n]+)`', '$1'
    return $text
}

# ------------------------------------------------------------ read current ---

$csprojText = Read-Text $paths.Csproj
$repoText = Read-Text $paths.Repo
$manifestText = Read-Text $paths.Manifest
$changelogText = Read-Text $paths.Changelog

$currentMatch = [regex]::Match($csprojText, '(?<=<Version>)\d+\.\d+\.\d+\.\d+(?=</Version>)')
if (-not $currentMatch.Success) {
    throw "Could not read <Version> from $($paths.Csproj)."
}
$current = $currentMatch.Value

if ([version]$Version -le [version]$current) {
    throw "New version $Version is not higher than the current $current. The in-game installer compares versions, so it would never offer the update."
}

# ------------------------------------------------------------- changelog ----

# Everything between "## [Unreleased]" and the next "## " heading.
$unreleased = [regex]::Match(
    $changelogText,
    '(?m)^##\s*\[Unreleased\]\s*\r?\n(?<body>.*?)(?=^##\s)',
    [System.Text.RegularExpressions.RegexOptions]::Singleline)

if (-not $unreleased.Success) {
    throw "Could not find a '## [Unreleased]' section followed by a later '## ' heading in $($paths.Changelog)."
}

$body = $unreleased.Groups['body'].Value.Trim()

# At least one bullet, not merely non-empty text. The placeholder this script
# leaves behind ("Nothing yet.") is non-empty, so a bare emptiness check would
# happily ship it as the release notes on a second run.
if ($body -notmatch '(?m)^\s*[-*]\s+\S') {
    throw "The [Unreleased] section has no entries. Write the release notes there first - that is the point of releasing."
}

$notes = ConvertTo-PlainNotes $body
if ($notes.Length -gt 2000) {
    Write-Warning "Release notes are $($notes.Length) characters; the in-game installer shows this in a small panel."
}

$date = Get-Date -Format 'yyyy-MM-dd'
$newChangelog = $changelogText.Remove($unreleased.Index, $unreleased.Length).Insert(
    $unreleased.Index,
    "## [Unreleased]`n`nNothing yet.`n`n## [$Version] - $date`n`n$body`n`n")

# --------------------------------------------------------------- rewrites ---

$newCsproj = Set-Single $csprojText `
    '(?<=<Version>)\d+\.\d+\.\d+\.\d+(?=</Version>)' $Version 'csproj <Version>'

$newRepo = Set-Single $repoText `
    '(?<="AssemblyVersion"\s*:\s*")\d+\.\d+\.\d+\.\d+(?=")' $Version 'repo.json AssemblyVersion'

$jsonNotes = ConvertTo-JsonString $notes
$newRepo = Set-Single $newRepo `
    '(?<="Changelog"\s*:\s*")(?:[^"\\]|\\.)*(?=")' $jsonNotes 'repo.json Changelog'

$newManifest = Set-Single $manifestText `
    '(?<="Changelog"\s*:\s*")(?:[^"\\]|\\.)*(?=")' $jsonNotes 'VeinAndVine.json Changelog'

# ------------------------------------------------------------------ apply ---

Write-Host ""
Write-Host "  $current  ->  $Version   ($date)" -ForegroundColor Cyan
Write-Host ""
Write-Host "Release notes going into both manifests:" -ForegroundColor Cyan
Write-Host ($notes -split "`n" | ForEach-Object { "  $_" } | Out-String).TrimEnd()
Write-Host ""

if ($DryRun) {
    Write-Host "Dry run - nothing written." -ForegroundColor Yellow
    return
}

Write-Text $paths.Csproj    $newCsproj
Write-Text $paths.Repo      $newRepo
Write-Text $paths.Manifest  $newManifest
Write-Text $paths.Changelog $newChangelog

Write-Host "Updated:" -ForegroundColor Green
foreach ($name in @('Csproj', 'Repo', 'Manifest', 'Changelog')) {
    Write-Host "  $($paths[$name])"
}

# ------------------------------------------------------------------ build ---

if (-not $SkipBuild) {
    Write-Host ""
    Write-Host "Building Release (also exercises the csproj version guard)..." -ForegroundColor Cyan

    Push-Location $Root
    try {
        Invoke-Tool dotnet build -c Release -v q --nologo
    }
    catch {
        throw "Release build failed. The version files are already updated; fix the build before committing. ($($_.Exception.Message))"
    }
    finally {
        Pop-Location
    }

    $zip = Join-Path $Root 'VeinAndVine\bin\x64\Release\VeinAndVine\latest.zip'
    if (Test-Path $zip) {
        Write-Host "Package: $zip" -ForegroundColor Green
    }
}

# ----------------------------------------------------------------- commit ---

$files = @($paths.Csproj, $paths.Repo, $paths.Manifest, $paths.Changelog)

if ($Commit) {
    $committed = $false
    Push-Location $Root
    try {
        # Only the version files, so an unrelated work-in-progress elsewhere in
        # the tree doesn't get swept into the release commit.
        Invoke-Tool git add -- @files
        Invoke-Tool git commit -m "Release $Version"
        $committed = $true
        Invoke-Tool git tag -a "v$Version" -m "Vein and Vine $Version"
    }
    catch {
        if ($committed) {
            throw "The release commit was made but tagging failed - the files are committed, there is just no v$Version tag yet. Add it with: git tag -a v$Version -m `"Vein and Vine $Version`". ($($_.Exception.Message))"
        }
        throw
    }
    finally {
        Pop-Location
    }

    Write-Host ""
    Write-Host "Committed and tagged v$Version. Not pushed - when you are ready:" -ForegroundColor Green
    Write-Host "  git push && git push origin v$Version"
}
else {
    Write-Host ""
    Write-Host "Not committed. To finish the release:" -ForegroundColor Yellow
    Write-Host "  git add CHANGELOG.md repo.json VeinAndVine/VeinAndVine.csproj VeinAndVine/VeinAndVine.json"
    Write-Host "  git commit -m `"Release $Version`""
    Write-Host "  git tag -a v$Version -m `"Vein & Vine $Version`""
    Write-Host "  git push && git push origin v$Version"
    Write-Host ""
    Write-Host "Then attach latest.zip to the GitHub release so repo.json's download links resolve."
}
