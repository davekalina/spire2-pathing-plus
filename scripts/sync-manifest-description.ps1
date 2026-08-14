<#
.SYNOPSIS
    Copies text/description.txt into the mod manifest's `description` field.

.DESCRIPTION
    The in-game mod list reads its description from <ModId>.json, but that text is
    written for players and should be editable as prose rather than as a JSON string.
    This runs before every build, so editing the text file is all that is needed.

    Comment lines (#) are dropped and the rest is joined into one line, since the mod
    list gives the description a single narrow row.

    Does nothing if the text file is absent, and never fails the build: a mod that
    cannot be built because a description would not sync is worse than a stale one.
#>
param(
    [Parameter(Mandatory)][string]$Root
)

$ErrorActionPreference = 'Stop'

$textPath = Join-Path $Root 'text\description.txt'
if (-not (Test-Path -LiteralPath $textPath)) { return }

$project = Get-ChildItem -LiteralPath $Root -Filter '*.csproj' |
    Where-Object { $_.Name -notlike '*.Tests.csproj' } |
    Select-Object -First 1
if (-not $project) { return }

$manifestPath = Join-Path $Root ("{0}.json" -f [System.IO.Path]::GetFileNameWithoutExtension($project.Name))
if (-not (Test-Path -LiteralPath $manifestPath)) { return }

$description = (Get-Content -LiteralPath $textPath |
    Where-Object { $_.TrimStart() -notlike '#*' -and $_.Trim().Length -gt 0 } |
    ForEach-Object { $_.Trim() }) -join ' '
if ([string]::IsNullOrWhiteSpace($description)) { return }

$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
# `description` is optional in the manifest, and assigning to a property that is not
# there throws -- which the build target's ContinueOnError would swallow, leaving the
# mod list blank with nothing to say why.
if ($manifest.PSObject.Properties.Name -notcontains 'description') {
    $manifest | Add-Member -NotePropertyName description -NotePropertyValue ''
}
if ($manifest.description -eq $description) { return }

$manifest.description = $description
$manifest | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $manifestPath -Encoding utf8
Write-Host "Manifest description updated from text/description.txt"
