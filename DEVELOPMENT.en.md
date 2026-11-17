# Development — English

> Build instructions only. See `Docs/` for features, compatibility and known issues, and `ARCHIVE.md` for public archive facts.

## Source

```text
Source/
├── TvBiliBiliMod.cs
└── TV_BiliBili.csproj
```

The repository root is the mod folder. `manifest.xml` and `TV_BiliBili.dll` must stay at the root; the game's mod loader does not use a RimWorld-style `Assemblies/` folder.

## Build

```
dotnet build Source/TV_BiliBili.csproj -c Release
```

- Target framework: `netstandard2.1`
- SDK used for the frozen build: .NET 10.0.301
- Output: `Source/bin/Release/netstandard2.1/TV_BiliBili.dll`
- For release, copy the output DLL to the repository root next to `manifest.xml`.

## References

`Source/TV_BiliBili.csproj` keeps the 12 absolute `HintPath` values from the frozen build machine, pointing to:

```text
<game install directory>/Probably Stolen_Data/Managed/
```

When building elsewhere, replace those 12 paths with your own game directory. All references are read-only (`Private=false`); do not copy game DLLs into the release package. There are no NuGet packages.

## Verification

For normal development, build without errors and confirm the mod loads in-game. For archive verification with the same reference assemblies, the DLL md5 should be `b0e773a7597814123b7fa633ad1b6100`. Do not change code just to force a hash match.

## Layout

| Path | Content |
|---|---|
| `Source/` | C# source and project file |
| `Docs/` | User-facing docs |
| Root | `manifest.xml`, `TV_BiliBili.dll`, README, CHANGELOG, ARCHIVE, DEVELOPMENT, LICENSE |
