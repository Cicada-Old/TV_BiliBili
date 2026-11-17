# TV_BiliBili

> A Bilibili player tab for the shop calendar in *Probably Stolen*.  
> [简体中文](README.md) · Probably Stolen 1.0.0 · Author: Cicada · MIT

## What it does

Adds a fourth tab named **Player** to the shop calendar. Paste a Bilibili video URL or a bare BV id, and the video plays inside the tab while also being projected onto the in-world calendar display. Clicking the display still opens the original calendar.

## Installation

1. Close the game.
2. Download `TV_BiliBili_1.0.0.zip` from the `v1.0.0` Release and extract it to get the `TV_BiliBili` folder.
3. Put the **whole `TV_BiliBili` folder** into the game root `Mods/` (next to `Probably Stolen.exe`).
4. Start the game and enable the mod in the Mods list.
5. On first enable, approve the `network` capability in-game; without it the mod cannot fetch video streams.

You can also download this repository as a ZIP: the repository root is the mod folder, so copy the extracted folder to `Mods/TV_BiliBili/`.

The mod folder only needs `TV_BiliBili.dll` and `manifest.xml` at runtime. The Release archive also includes user docs, which are not loaded by the game.

## Docs

| Topic | Link |
|---|---|
| Install and use | [安装与使用](Docs/安装与使用.md) |
| Compatibility and dependencies | [兼容性与依赖](Docs/兼容性与依赖.md) |
| Features | [FEATURES](Docs/FEATURES.md) / [English](Docs/FEATURES.en.md) |
| Known issues | [KNOWN_ISSUES](Docs/KNOWN_ISSUES.md) / [English](Docs/KNOWN_ISSUES.en.md) |
| Testing summary | [TESTING](Docs/TESTING.md) / [English](Docs/TESTING.en.md) |
| Architecture | [ARCHITECTURE](Docs/ARCHITECTURE.md) / [English](Docs/ARCHITECTURE.en.md) |
| Contributing | [CONTRIBUTING](Docs/CONTRIBUTING.md) / [English](Docs/CONTRIBUTING.en.md) |
| Release notes / changelog | [更新说明](Docs/更新说明.md) / [CHANGELOG](CHANGELOG.md) / [English](CHANGELOG.en.md) |
| Development | [DEVELOPMENT](DEVELOPMENT.md) / [English](DEVELOPMENT.en.md) |
| Archive summary | [ARCHIVE](ARCHIVE.md) |

## Repository layout

The repository root is the mod folder. `manifest.xml` and `TV_BiliBili.dll` must stay at the root because of the game's mod loader.

```text
TV_BiliBili/
├── manifest.xml
├── TV_BiliBili.dll
├── Docs/
├── Source/
├── README.md / README.en.md
├── CHANGELOG.md / CHANGELOG.en.md
├── DEVELOPMENT.md / DEVELOPMENT.en.md
├── ARCHIVE.md
├── LICENSE
├── .gitignore
└── .gitattributes
```
