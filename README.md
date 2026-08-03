# Pathing Plus

See every route before you commit to one.

On the map screen, click any node ahead of you to pin it: the mod scores every route
from your position by how many pins it visits and draws the best matches as
hand-drawn dashed trails in the game's own colors. A route through **all** your pins
always wins; when the pins conflict, the best achievable coverage shows instead of
nothing; and near-miss routes (up to two pins short) fill the spare legend slots when
they fit. Each legend row shows its score ("Route 1 — 6/7"). Unpin as you commit and
the picture sharpens. Hover or select a legend row to preview it: one tooltip shows
the route's rooms vertically like the map (boss at the top), a second shows a
category table (elites, fires, events, combats, chests, shops — same order every
time, so routes compare row by row), and that route darkens to ink on the map while
the others fade. The map's Clear-drawings button also clears your pins.

On a controller, pull the **Right Trigger** (or click the button above the drawing
tools) to toggle **Plan Mode**: the d-pad walks the future map node by node with the
view following, and select pins the focused node. Select on a reachable node still
travels, and the route legend is reachable downward from the native map legend.

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
logs `Pathing Plus v0.4.0 initialized`.

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
