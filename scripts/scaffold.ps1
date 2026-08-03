<#
.SYNOPSIS
    Turns this template into a named Slay the Spire 2 mod, then deletes itself.

.DESCRIPTION
    Replaces the __MOD_ID__ / __MOD_NAME__ / __MOD_AUTHOR__ / __MOD_DESCRIPTION__ tokens
    throughout the repository and renames every file and folder that carries them.

    Run this once, immediately after creating a repository from the template. It is
    destructive and not idempotent, so commit or stash first if the tree is dirty.

.PARAMETER ModId
    The mod's permanent identity. Letters and digits only, starting with a letter.

    This becomes the install folder (<game>/mods/<ModId>/), the assembly and manifest
    filenames (<ModId>.dll, <ModId>.json), the C# root namespace, and the Steam Workshop
    conflict key. Changing it after publishing orphans the Workshop item, so choose it
    now and do not revisit it.

.PARAMETER DisplayName
    The name shown in Settings -> Mod Settings and on the Workshop page.

.PARAMETER Author

.PARAMETER Description
    One sentence for the manifest. Defaults to the display name.

.EXAMPLE
    .\scripts\scaffold.ps1 -ModId WinrateTracker -DisplayName "Winrate Tracker"
#>
param(
    [Parameter(Mandatory)][string]$ModId,
    [Parameter(Mandatory)][string]$DisplayName,
    [string]$Author = 'realtruegravy',
    [string]$Description
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

if ($ModId -notmatch '^[A-Za-z][A-Za-z0-9]*$') {
    throw "ModId '$ModId' is invalid. Use letters and digits only, starting with a letter: it has to work as a folder name, a filename, a Godot res:// path segment, and a C# identifier."
}
if (-not $Description) { $Description = $DisplayName }

$tokens = @{
    '__MOD_ID__'          = $ModId
    '__MOD_NAME__'        = $DisplayName
    '__MOD_AUTHOR__'      = $Author
    '__MOD_DESCRIPTION__' = $Description
}

$textExtensions = '.cs', '.csproj', '.json', '.md', '.props', '.ps1', '.godot', '.cfg', '.gitignore', '.gitattributes'

Write-Host "Scaffolding $DisplayName ($ModId)" -ForegroundColor Cyan

# 1. Rewrite file contents. Do this before renaming so paths stay predictable.
Get-ChildItem -LiteralPath $root -Recurse -File -Force |
    Where-Object { $_.FullName -notmatch '\\(\.git|bin|obj|\.godot)\\' } |
    Where-Object { $textExtensions -contains $_.Extension -or $_.Name -in '.gitignore', '.gitattributes' } |
    ForEach-Object {
        $original = [System.IO.File]::ReadAllText($_.FullName)
        $updated = $original
        foreach ($token in $tokens.GetEnumerator()) {
            $updated = $updated.Replace($token.Key, $token.Value)
        }
        if ($updated -ne $original) {
            [System.IO.File]::WriteAllText($_.FullName, $updated)
            Write-Host "  content  $($_.FullName.Substring($root.Length + 1))"
        }
    }

# 2. Rename paths, deepest first so parent renames cannot invalidate child paths.
Get-ChildItem -LiteralPath $root -Recurse -Force |
    Where-Object { $_.FullName -notmatch '\\(\.git|bin|obj|\.godot)\\' } |
    Where-Object { $_.Name -like '*__MOD_ID__*' } |
    Sort-Object { $_.FullName.Length } -Descending |
    ForEach-Object {
        $newName = $_.Name.Replace('__MOD_ID__', $ModId)
        Rename-Item -LiteralPath $_.FullName -NewName $newName
        Write-Host "  rename   $($_.Name) -> $newName"
    }

# 3. Remove the template's own scaffolding instructions from the README.
$readme = Join-Path $root 'README.md'
if (Test-Path -LiteralPath $readme) {
    $text = [System.IO.File]::ReadAllText($readme)
    $marker = '<!-- TEMPLATE-ONLY:START -->'
    $end = '<!-- TEMPLATE-ONLY:END -->'
    if ($text.Contains($marker) -and $text.Contains($end)) {
        $before = $text.Substring(0, $text.IndexOf($marker))
        $after = $text.Substring($text.IndexOf($end) + $end.Length)
        [System.IO.File]::WriteAllText($readme, ($before + $after).TrimStart())
        Write-Host "  trimmed  README.md template instructions"
    }
}

Write-Host "`nDone. Next:" -ForegroundColor Green
Write-Host "  1. dotnet build .\$ModId.csproj"
Write-Host "  2. dotnet test .\$ModId.Tests\$ModId.Tests.csproj"
Write-Host "  3. Fill in the 'This mod' table and 'Surfaces to audit' list in AGENTS.md"
Write-Host "  4. Replace workshop\image.png with real art before publishing"
Write-Host "  5. git add -A; git commit -m `"Scaffold $DisplayName`"`n"

# 4. Delete this script. PowerShell has already read it into memory.
Remove-Item -LiteralPath $PSCommandPath -Force
Write-Host "Removed scripts\scaffold.ps1; it only runs once." -ForegroundColor DarkGray
