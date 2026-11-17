# Architecture

This file describes the public runtime structure only. Internal test evidence, freezing records and verdict details are not distributed.

## Layout and loading

```text
TV_BiliBili/                  # repository root is the mod folder
├── manifest.xml              # ID, name, author, version, capabilities, assembly
├── TV_BiliBili.dll           # IMod entry and implementation
├── Docs/                     # user-facing docs
└── Source/                   # C# source and project file
```

Probably Stolen's ModLoader reads `manifest.xml` and the assembly from the root of `Mods/<mod folder>/`. This project does not use a RimWorld-style `Assemblies/` subfolder.

## Runtime flow

1. The player pastes a Bilibili URL or bare BV id into the fourth calendar tab.
2. The entry class extracts the BV id and calls the Bilibili `view` API to resolve cid.
3. cid is passed to `playurl` to obtain a merged direct URL; non-http(s) URLs are rejected.
4. `VideoPlayer` streams the URL into a `RenderTexture`.
5. The same texture is shown in the tab's `RawImage` and fitted to the in-world calendar display.
6. Lifecycle events maintain pause / resume / release; Harmony patches notify the mod when ESC / options menus open and close.

## Capabilities and safety

- Only capability declared in the manifest: `network`.
- No `files`, `process` or `native` capability; no prerequisite mods.
- No video download, cache or player save data; only a plain-text runtime log under the mod's ModData folder.
- Twelve runtime references are provided by the game and are not redistributed; no NuGet packages.
