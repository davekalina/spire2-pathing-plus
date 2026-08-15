# Authoring Slay the Spire 2 mods

Platform reference. These are facts about the game and its mod loader, verified against
the decompiled `sts2.dll` for game version 0.111.0 and against Mega Crit's official
uploader. They are not project preferences — see `AGENTS.md` for those.

Steam app id: **2868840**.

## What a mod is

Slay the Spire 2 is built with Godot 4.5 and .NET 9 and has a built-in mod loader plus
official Steam Workshop distribution. A code mod is:

- a **C# project** compiled against the game's `sts2.dll` and `0Harmony.dll`;
- a **JSON manifest** describing id, name, version, dependencies, and whether the mod
  affects gameplay;
- a **compiled DLL**; and
- optionally a Godot **`.pck`** holding scenes, textures, localization, and other
  resources.

At startup the loader calls a static method marked with the game's `ModInitializer`
attribute. From there, three layers are available, in order of preference:

1. **Native game API and Godot nodes** — models, commands, scenes, and UI.
2. **BaseLib** — a community compatibility/helper library with standardized APIs and
   fewer conflicts between mods.
3. **Harmony patches or reflection** — only when neither of the above exposes a suitable
   hook. These intercept existing C# methods at runtime and are the most likely thing to
   break after a game update.

## The manifest

`ModManifest` (`MegaCrit.Sts2.Core.Modding.ModManifest`) accepts exactly these fields:

| Field | Notes |
| --- | --- |
| `id` | **Required.** The mod's permanent identity. See below. |
| `name` | Display name shown in Settings → Mod Settings. |
| `author` | |
| `description` | |
| `version` | Free-form string; the loader compares it when reconciling Steam vs local copies. |
| `min_game_version` | **Enforced.** Compared as a semantic version against the game's own; a mod declaring a higher one is refused with `MOD_ERROR.GAME_VERSION_UNSUPPORTED` and never initialises. A value that cannot be parsed fails the mod too. Omitting it logs a warning and assumes support. |
| `has_dll` / `has_pck` | Booleans. The loader only looks for the file when the flag is true. |
| `dependencies` | `[{"id": "...", "min_version": "..."}]`. The bare-string form is deprecated and logs an error. |
| `affects_gameplay` | Defaults to **`true`** if omitted. Informational overlays must set it to `false`; it also matters for multiplayer compatibility. |

A JSON file with no `id` is skipped. A JSON file that has `name`/`author`/`description`/
`version` but no `id` is logged as an error.

## The `id` is the mod's permanent identity

Changing `id` after publishing breaks the Workshop item and every user's settings. Pick it
once, before the first upload. It determines:

- **Install folder** — `<game>/mods/<id>/`
- **Assembly filename** — the loader loads `<id>.dll`, so the C# assembly name must equal
  the id
- **PCK filename** — `<id>.pck`
- **Mod-list image path** — `res://<id>/mod_image.png`, so the Godot asset folder inside
  the `.pck` must be named exactly `<id>`; this only renders when `has_pck` is true
- **BaseLib settings key** — `ModConfigRegistry.Register(id, ...)`; changing the id resets
  stored settings to defaults
- **Steam conflict key** — a Workshop copy and a local copy sharing an id conflict, and
  the loader disables one of them. Versions are compared as semantic versions, not
  as strings, so `v0.10.0` correctly beats `v0.9.0`, and a leading `v` is ignored.
  **On equal versions the Steam copy loses**, which is deliberate: it lets a local
  development build shadow the published one without renaming anything

## Where the loader looks

Relative to the game executable's directory:

- `mods/` — scanned recursively, tagged `ModSource.ModsDirectory`. This is the local
  development path.
- `mods_STEAMTEST/` — scanned recursively, tagged `ModSource.SteamWorkshop`. Use it to
  exercise the Workshop code path locally without publishing.
- Steam subscriptions — enumerated through `SteamUGC.GetSubscribedItems`.

Because the scan is recursive, a Workshop item may nest the mod folder inside its content
directory.

Mods are enabled in **Settings → Mod Settings**. Modded and unmodded saves are separate.
The loader rebuilds the persisted mod list from whatever it discovers on each launch, so
deleting an install folder is enough to clear a stale entry — no save editing needed.
Launching with `-nomods` skips mod initialization entirely.

## Steam Workshop publishing

Use Mega Crit's uploader: <https://github.com/megacrit/sts2-mod-uploader> (releases ship
`ModUploader-win-x64.zip` and friends). It operates on a *workspace* directory:

```text
workshop/
  workshop.json   # metadata, see below
  image.png       # required, must be under 1 MB
  previews/       # optional extra images, each under 1 MB, keyed by filename;
                  # omit the directory entirely to leave existing previews unchanged
  content/        # exactly what gets uploaded: <id>.json, <id>.dll, optional <id>.pck
  mod_id.txt      # written by the uploader after the first publish, read on every update
```

```powershell
ModUploader.exe upload -w <workspace-folder>
```

`workshop.json` fields — most may be `null` or omitted to leave the current value
unchanged after the initial upload:

| Field | Notes |
| --- | --- |
| `title` | |
| `description` | |
| `visibility` | `private`, `public`, `unlisted`, or `friends_only` |
| `changeNote` | Shown to subscribers as the changelog for this update |
| `tags` | `Tools & APIs` is reserved for mods that are genuinely tools or APIs |
| `dependencies` | Workshop mod ids, taken from the Workshop URL — not manifest ids |
| `contentDescriptors` | `nudity`, `frequent_violence`, `adult_only`, `gratuitous_nudity`, `general_mature` |
| `minBranch` / `maxBranch` | e.g. `public-beta`, `public`. Mega Crit notes these behave oddly through the API and recommends setting them on the Steam web page instead. Omitting them means all versions are supported. |

Supported-branch enforcement happens Steam-side, not in the manifest: at load time the
game queries `SteamUGC.GetSupportedGameVersions` for each subscribed item and refuses to
load a mod whose branch range excludes the player's current branch.

`mod_id.txt` is the only link between the repository and the published Workshop item.
**Commit it. Never gitignore it.** Losing it orphans the item and the next upload creates a
duplicate.

A run of the uploader writes `mod-uploader.log` next to the executable; that is the file
to send Mega Crit when reporting an upload problem.

## Development loop

1. Reference the installed `sts2.dll` and `0Harmony.dll` from
   `<game>/data_sts2_windows_x86_64/`. `Sts2PathDiscovery.props` resolves that path from
   the Steam registry keys, with per-machine overrides in `Directory.Build.props`.
2. Inspect the exact classes and signatures in the *installed* build with ILSpy before
   writing a patch. The game is in Early Access; internals move between releases.
3. Build. The csproj copies `<id>.json`, `<id>.dll`, and `<id>.pdb` into
   `<game>/mods/<id>/` automatically.
4. Launch with mods enabled and check the log for the initializer line.
5. Package with `scripts/package-workshop.ps1` and publish with the uploader.

## Reference links

- [Mega Crit v0.107.1 notes](https://store.steampowered.com/news/app/2868840/view/532105985720570241) — built-in loader and official Workshop support
- [Slay the Spire 2 Workshop](https://steamcommunity.com/app/2868840/workshop/)
- [Mega Crit mod uploader](https://github.com/megacrit/sts2-mod-uploader)
- [Alchyr's mod template](https://github.com/Alchyr/ModTemplate-StS2) and its
  [modding basics wiki](https://github.com/Alchyr/ModTemplate-StS2/wiki/Modding-Basics)
- [BaseLib wiki](https://alchyr.github.io/BaseLib-Wiki/)
- [Community modding guide](https://tutorials.sts2modding.com/en/)
- [Minimal example mod](https://github.com/jiegec/STS2FirstMod)
- [Harmony](https://harmony.pardeike.net/articles/intro.html) and
  [Godot 4](https://docs.godotengine.org/en/4.x/)
