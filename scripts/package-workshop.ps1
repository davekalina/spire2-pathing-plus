<#
.SYNOPSIS
    Stages this mod's Steam Workshop upload payload into workshop/content/.

.DESCRIPTION
    Builds the mod in Release without installing it into the local game folder,
    then copies exactly the files the Workshop item should contain:

        <ModId>.json   the manifest
        <ModId>.dll    the compiled mod (when has_dll)
        <ModId>.pck    Godot resources (when has_pck)

    The .pdb is deliberately excluded: it is a local debugging aid, not something
    subscribers need.

    Nothing is uploaded. The command to run afterwards is printed at the end.

.PARAMETER Configuration
    Build configuration. Defaults to Release; Workshop builds should stay Release.

.PARAMETER SkipBuild
    Package whatever is already built instead of rebuilding.
#>
param(
    [string]$Configuration = 'Release',
    [switch]$SkipBuild
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

$project = Get-ChildItem -LiteralPath $root -Filter '*.csproj' |
    Where-Object { $_.Name -notlike '*.Tests.csproj' } |
    Select-Object -First 1
if (-not $project) { throw "No mod .csproj found in $root" }

$modId = [System.IO.Path]::GetFileNameWithoutExtension($project.Name)
$manifestPath = Join-Path $root "$modId.json"
if (-not (Test-Path -LiteralPath $manifestPath)) {
    throw "Manifest not found at $manifestPath. The manifest must be named after the mod id."
}

$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
if ($manifest.id -ne $modId) {
    throw "Manifest id '$($manifest.id)' does not match project name '$modId'. The loader requires <id>.dll, so they must match."
}

Write-Host "Packaging $($manifest.name) ($modId) $($manifest.version)" -ForegroundColor Cyan

if (-not $SkipBuild) {
    & dotnet build $project.FullName -c $Configuration -p:SkipModInstall=true
    if ($LASTEXITCODE -ne 0) { throw "Build failed with exit code $LASTEXITCODE" }
}

$targetPath = (& dotnet msbuild $project.FullName -getProperty:TargetPath -p:Configuration=$Configuration -p:SkipModInstall=true).Trim()
$modsPath   = (& dotnet msbuild $project.FullName -getProperty:ModsPath   -p:Configuration=$Configuration -p:SkipModInstall=true).Trim()

$content = Join-Path $root 'workshop\content'
if (Test-Path -LiteralPath $content) { Remove-Item -LiteralPath $content -Recurse -Force }
New-Item -ItemType Directory -Path $content -Force | Out-Null

Copy-Item -LiteralPath $manifestPath -Destination $content
Write-Host "  + $modId.json"

if ($manifest.has_dll) {
    if (-not (Test-Path -LiteralPath $targetPath)) { throw "has_dll is true but no assembly at $targetPath" }
    Copy-Item -LiteralPath $targetPath -Destination (Join-Path $content "$modId.dll")
    Write-Host "  + $modId.dll"
}

if ($manifest.has_pck) {
    $pck = Join-Path $modsPath "$modId\$modId.pck"
    if (-not (Test-Path -LiteralPath $pck)) {
        throw "has_pck is true but no .pck at $pck. Run 'dotnet publish' with GodotPath set in Directory.Build.props first."
    }
    Copy-Item -LiteralPath $pck -Destination $content
    Write-Host "  + $modId.pck"
}

$image = Join-Path $root 'workshop\image.png'
if (-not (Test-Path -LiteralPath $image)) {
    Write-Warning "workshop/image.png is missing. The uploader requires it."
} elseif ((Get-Item -LiteralPath $image).Length -ge 1MB) {
    Write-Warning "workshop/image.png is $([math]::Round((Get-Item -LiteralPath $image).Length / 1MB, 2)) MB. Steam rejects preview images of 1 MB or more."
}

$modIdFile = Join-Path $root 'workshop\mod_id.txt'
if (Test-Path -LiteralPath $modIdFile) {
    Write-Host "`nUpdating published item $(Get-Content -LiteralPath $modIdFile -Raw)" -ForegroundColor Yellow
} else {
    Write-Host "`nNo workshop/mod_id.txt yet: this will create a NEW Workshop item." -ForegroundColor Yellow
    Write-Host "Commit mod_id.txt immediately after the first upload." -ForegroundColor Yellow
}

Write-Host "`nStaged in $content" -ForegroundColor Green
Write-Host "Check workshop/workshop.json (especially changeNote), then run:`n"
Write-Host "    ModUploader.exe upload -w `"$(Join-Path $root 'workshop')`"`n"
