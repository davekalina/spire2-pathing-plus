# Pathing Plus

See every route before you commit to one.

On the map screen, click any node ahead of you to pin it — or **double-click** to
pin every node of that kind at once (all the elites, all the fires; double-click
again to clear them). The mod scores every route
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
the map screen and even restarting the game. The **Zoom** button in the upper right
cycles three views with animated transitions: the game's normal scrolling view, the
whole act on one screen, and the whole act rotated on its side — start on the left,
boss on the right. In both zoomed views scrolling is switched off entirely.

The mod also replaces the map Legend with its own, bottom right on the same
parchment: node types as rows, one column per computed route (up to six) headed by
its colored letter. Hover a type icon to light up every node of that type on the map
(the game's own highlight), hover a column to preview that route, select it to lock.
The legend hotkey works as before — it just lands here now. In the rotated view the
node icons stay upright while the map turns beneath them.

On a controller, pull the **Right Trigger** (or click Zoom): the zoomed-out view is
also controller mode. The d-pad walks the map node by node — every node, not just
reachable ones — with a gold ink ring marking the cursor, which stays put after each
press. While zoomed out you are planning, not moving: select toggles a pin on any
node, travelable ones included, and travel never fires. Zoom back in to travel.

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
logs `Pathing Plus v0.10.0 initialized`.

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
