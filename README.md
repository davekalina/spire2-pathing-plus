# Pathing Plus

See every route before you commit to one.

On the map screen, click any node ahead of you to pin it: the mod draws every route
from your position that passes through your pins, as hand-placed dotted trails in the
game's own colors. Stack pins to narrow the options. Once five or fewer routes remain,
each gets its own color and a legend entry — hover or select a legend row to preview
it: the route's room sequence appears vertically like the map (boss at the top) with
a consistent category summary (elites, fires, events, combats, chests, shops), and
that route darkens to ink on the map while the others fade. Click a pinned node again
to unpin it.

On a controller, toggle **Plan Mode** — the button beside the drawing tools. The
d-pad then walks the future map node by node (the view follows), and select pins the
focused node. Select on a reachable node still travels, and the route legend is
reachable downward from the native map legend.

Informational only: nothing about the run changes, and `?` nodes stay `?`.

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
logs `Pathing Plus v0.2.0 initialized`.

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
