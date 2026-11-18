# Developing Pathing Plus

Maintainer notes. What the mod does for a player is in [README.md](README.md); how I
want mod work done is in [AGENTS.md](AGENTS.md), and the platform's own rules are in
[docs/sts2-modding.md](docs/sts2-modding.md).

## Build

```powershell
dotnet build .\PathingPlus.csproj
dotnet test .\PathingPlus.Tests\PathingPlus.Tests.csproj
```

`Sts2PathDiscovery.props` finds the game through the Steam registry keys. If it
cannot, copy `Directory.Build.props.example` to `Directory.Build.props` and set
`Sts2Path`.

Player-facing text is not in the code. Edit these as prose and rebuild; no C# and no
JSON escaping involved, and never hand-edit the JSON fields they feed.

| File | Shows up as |
| --- | --- |
| [`text/eng/help.txt`](text/eng/help.txt) | The in-game help panel, behind the **?** badge. Embedded into the assembly at build. |
| [`text/eng/strings.txt`](text/eng/strings.txt) | Every short label: buttons, settings rows, legend names, tooltips. `key = value`, one per line. |
| `text/zhs/`, `text/zht/` | The same two files in Simplified and Traditional Chinese. |
| [`text/description.txt`](text/description.txt) | The game's own mod list. A build target copies it into `PathingPlus.json`. |
| [`workshop/workshop-description.txt`](workshop/workshop-description.txt) | The Workshop page. `package-workshop.ps1` copies it into `workshop.json`. |

### Translations

Each language is a folder under `text/` named with the game's own language code
(`LocManager.Language`: `eng`, `zhs`, `zht`, `jpn`, ...). It holds a `strings.txt` and a
`help.txt`, and is embedded as `<lang>/<file>`. A new language is a new folder; nothing
in the project file or the C# changes. Lookups fall back to English **key by key**, so a
partial translation is safe. Text in `{braces}` is filled in by the mod and must survive
translation unchanged; the tests in `TextFilesTests` fail the build when a translation
drops or adds a key or a placeholder.

The Chinese terms shared with the game (精英/菁英, 休息处/休息處, 宝箱/財寶, ...) are
copied from the game's own `localization/zhs` and `localization/zht` tables, so they read
the same as the map they sit on. zht is a Taiwan-style translation in its own right, not
converted zhs. Follow the game's punctuation: full-width （） and ： in both.

To check a language in game, switch it in Settings → General from the **main menu**
(the game only allows it there), continue a run, and open the map.

Building copies `PathingPlus.json`, `PathingPlus.dll`, and `PathingPlus.pdb` into
`<game>/mods/PathingPlus/`. Pass `-p:SkipModInstall=true` to build without
installing. Close the game first: the DLL is locked while it runs, and the install
copy deliberately writes the DLL before the manifest so a locked DLL aborts the whole
deploy rather than leaving a new manifest over an old binary.

Runtime diagnostics are in `%APPDATA%\SlayTheSpire2\logs\godot.log`. A successful
start logs `Pathing Plus v<version> initialized`.

## Route selection checks

`RouteDisplay` pages complete routes and matches every pinned route against the
full ranked set before applying the five-column limit. Up to four pins remain on
every page; with five or more, pins and alternatives are paged with pins first.
Pins outside the current page remain highlighted on the map. An explicit map-line
selection reveals its page and remains beside the pins when the table folds.
Mouse hover alone remains a temporary preview. Re-entering the legend expands it
for comparison; page controls provide the same route access with a controller.

`RoutePlan.FromRoutes` supplies both Auto-Path and the legend's clear action, which
appears only with at least one pinned route. Each column independently toggles its
pin. Clearing unpinned routes keeps the combined steps of all pins and does not
touch native quill drawings. The exact retained routes are also saved, preventing
their shared junctions from reassembling unwanted route combinations. Drawing or
erasing resumes normal assembly; travel preserves the remaining retained tails.
`SavedPlan` reads the old `LockedRoute` field and writes the plural `LockedRoutes`.

For in-game validation:

- Generate suggestions and verify that the clear action is absent until a route is
  pinned. Pin several, unpin one without affecting the others, then clear unpinned
  routes. Check that all remaining pins stay and ordinary draw/erase edits work.
- With more than five completed routes, click a line that only has a hover preview.
  Move to the legend and pin its persistent column, then try the clear action.
- Browse every page with the controller and pin a route outside the first page.
  Check the clear action, disabled page boundaries, focus after columns rebuild,
  and paging while no pin exists and the clear action is hidden.
- Add higher-ranked routes, advance along the pin, and reopen the map. The pin
  should survive while its remaining route exists, and clear when it is removed.
  Check several pins across a restart, and five or more pins across legend pages.
- Check that map dragging, node travel clicks, native drawings, and incomplete path
  previews still behave normally in each zoom/rotation view.

The action label is `legend.clear_unpinned` in `text/<language>/strings.txt`. The existing help
and Workshop copy do not yet describe these controls; leave that prose for David.

Player settings live in `PathingPlus.settings.json`, and pins in
`PathingPlus.pins.json`, both in the game's user data directory. Delete either to
test first-run behaviour.

## Publish to the Steam Workshop

```powershell
.\scripts\package-workshop.ps1
```

That stages `workshop/content/` and prints the `ModUploader.exe upload -w …` command
to run next. Get the uploader from
<https://github.com/megacrit/sts2-mod-uploader/releases>. Steam must be running.

Edit `workshop/workshop-description.txt` when the page copy changes, and keep
`workshop.json`'s `description` in sync with it.

`workshop/mod_id.txt` is the only link between this repository and the published
Workshop item. **Commit it**; losing it orphans the item and the next upload creates
a duplicate.

See `docs/sts2-modding.md` for the full pipeline and `workshop/README.md` for the
`workshop.json` field reference.
