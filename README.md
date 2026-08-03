# __MOD_NAME__

<!-- TEMPLATE-ONLY:START -->
> **This is the template.** Create a repository from it on GitHub ("Use this template"),
> or copy the folder, then run:
>
> ```powershell
> .\scripts\scaffold.ps1 -ModId MyMod -DisplayName "My Mod"
> ```
>
> That rewrites every `__MOD_ID__` / `__MOD_NAME__` token, renames the files and folders
> that carry them, trims this block, and deletes itself.
>
> Pick `-ModId` carefully. It becomes the install folder, the DLL and manifest filenames,
> the C# namespace, and the Steam Workshop conflict key, and it cannot be changed after
> publishing. Letters and digits only.
>
> ### What you get
>
> ```text
> AGENTS.md                  house rules; canonical, read by both Codex and Claude Code
> CLAUDE.md                  three-line @-import shim so there is one source of truth
> docs/sts2-modding.md       platform reference: loader, manifest, Workshop pipeline
> <ModId>.csproj             builds and installs into <game>/mods/<ModId>/
> <ModId>.json               loader manifest
> <ModId>Code/MainFile.cs    [ModInitializer], Harmony, logger
> <ModId>Code/Example.cs     placeholder for game-independent logic; delete it
> <ModId>.Tests/             xunit project that links pure logic files
> Sts2PathDiscovery.props    finds the game via the Steam registry, cross-platform
> scripts/package-workshop.ps1   stages workshop/content/ for the uploader
> workshop/                  Mega Crit uploader workspace
> ```
>
> ### After scaffolding
>
> 1. Fill in the **This mod** table and the **Surfaces to audit** list in `AGENTS.md`.
>    Those two sections are what make the house rules enforceable for your mod.
> 2. Replace `workshop/image.png`. It is Mega Crit's placeholder.
> 3. Delete `<ModId>Code/Example.cs` and its test once real logic exists, and update the
>    `Compile Include` in the test csproj.
<!-- TEMPLATE-ONLY:END -->

An informational Slay the Spire 2 mod.

## Build

```powershell
dotnet build .\__MOD_ID__.csproj
dotnet test .\__MOD_ID__.Tests\__MOD_ID__.Tests.csproj
```

`Sts2PathDiscovery.props` finds the game through the Steam registry keys. If it cannot,
copy `Directory.Build.props.example` to `Directory.Build.props` and set `Sts2Path`.

Building copies `__MOD_ID__.json`, `__MOD_ID__.dll`, and `__MOD_ID__.pdb` into
`<game>/mods/__MOD_ID__/`. Pass `-p:SkipModInstall=true` to build without installing.
Close the game first, or the DLL will be locked.

Runtime diagnostics are in `%APPDATA%\SlayTheSpire2\logs\godot.log`. A successful start
logs `__MOD_NAME__ v0.1.0 initialized`.

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
