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

Building copies `PathingPlus.json`, `PathingPlus.dll`, and `PathingPlus.pdb` into
`<game>/mods/PathingPlus/`. Pass `-p:SkipModInstall=true` to build without
installing. Close the game first: the DLL is locked while it runs, and the install
copy deliberately writes the DLL before the manifest so a locked DLL aborts the whole
deploy rather than leaving a new manifest over an old binary.

Runtime diagnostics are in `%APPDATA%\SlayTheSpire2\logs\godot.log`. A successful
start logs `Pathing Plus v<version> initialized`.

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
