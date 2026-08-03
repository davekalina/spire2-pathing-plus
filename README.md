# Pathing Plus

See every route before you commit to one.

On the map screen, click any node ahead of you to pin it: the mod draws every route
from your position that passes through your pins. Stack pins to narrow the options.
Once five or fewer routes remain, each gets its own color and a legend entry —
hover or select a legend row to see the route's room sequence as icons
(fight, ?, elite, rest, …) and watch that route light up white on the map while the
others fade. Click a pinned node again to unpin it.

Informational only: nothing about the run changes, and `?` nodes stay `?`.

Known gap: pinning nodes currently needs the mouse. The route legend is fully
controller-navigable (reach it downward from the map legend).

## Build

```powershell
dotnet build .\PathingPlus.csproj
dotnet test .\PathingPlus.Tests\PathingPlus.Tests.csproj
```

`Sts2PathDiscovery.props` finds the game through the Steam registry keys. If it cannot,
copy `Directory.Build.props.example` to `Directory.Build.props` and set `Sts2Path`.

Building copies `PathingPlus.json`, `PathingPlus.dll`, and `PathingPlus.pdb` into
`<game>/mods/PathingPlus/`. Pass `-p:SkipModInstall=true` to build without installing.
Close the game first, or the DLL will be locked.

Runtime diagnostics are in `%APPDATA%\SlayTheSpire2\logs\godot.log`. A successful start
logs `Pathing Plus v0.1.0 initialized`.

## Publish to the Steam Workshop

```powershell
.\scripts\package-workshop.ps1
```

That stages `workshop/content/` and prints the `ModUploader.exe upload -w …` command to
run next. Get the uploader from
<https://github.com/megacrit/sts2-mod-uploader/releases>.

`workshop/mod_id.txt` appears after the first upload. **Commit it** — it is the only link
between this repository and the published Workshop item.

See `docs/sts2-modding.md` for the full pipeline and `workshop/README.md` for the
`workshop.json` field reference.
