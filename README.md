# Pathing Plus

See every route before you commit to one.

On the map screen, click any node ahead of you to pin it: the mod scores every route
from your position by how many pins it visits and draws the best matches as
hand-drawn dashed trails in the game's own colors. A route through **all** your pins
always wins; when the pins conflict, the best achievable coverage shows instead of
nothing; and near-miss routes (up to two pins short) fill the spare table rows when
they fit. Unpin as you commit and the picture sharpens.

Up to five routes show in the routes table: one colored row per route, with count
columns for elites, fires, combats, shops, chests, and events (map icons as column
headers), so what each route offers is a one-look decision — say, farming three
elites. Hover or select a row to preview it: a tooltip shows the route's rooms
vertically like the map (boss at the top), and that route darkens to ink on the map
while the others fade. Pinned nodes are circled with the game's own hand-inked ring
in a lighter shade than a visited node's. The map's Clear-drawings button also
clears your pins.

Pins and the locked route belong to the map they were made on: they survive closing
the map screen and even restarting the game. A **Zoom** button in the upper right
toggles between the standard view and a zoomed-out view of the whole act; while
zoomed out, scrolling is switched off entirely — nothing needs it.

On a controller, pull the **Right Trigger** (or click Zoom): the zoomed-out view is
also controller mode. The d-pad walks the map node by node — every node, not just
reachable ones — with a gold ink ring marking the cursor, which stays put after each
press. Select pins the focused node; select on a travelable node still travels.

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
logs `Pathing Plus v0.7.0 initialized`.

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
