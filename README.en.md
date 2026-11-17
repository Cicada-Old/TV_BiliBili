# TV_BiliBili (店铺小电视：也许是B站？)

> A Bilibili player tab for the shop calendar in *Probably Stolen*, with the video projected onto the in-world display.

[简体中文](README.md) · Probably Stolen 1.0.0 · Requires `network` approval · MIT

## What it does

The vanilla shop calendar has three tabs. This mod adds a fourth tab named **Player**: paste a full Bilibili video URL or a bare BV id, and the video plays inside the tab while also being fitted to the in-world calendar display. Clicking the display still opens the original calendar.

## Features

- **Input**: full Bilibili video URLs (query strings allowed) and bare BV ids; multi-part videos always use the first part.
- **Projection**: the video is shown in the tab and fitted to the in-world calendar display; source black bars are not cropped.
- **Controls**: play / pause, seek bar, time display and volume slider.
- **Status**: ready / resolving / loading / playing / paused / playback failed / invalid BV / empty URL. Failure details are written to the `PLAY_FAIL` log line.
- **Lifecycle**: switching tabs or closing the calendar keeps playback; day-end and leaving the shop pause and later resume; leaving the save stops playback and releases resources.
- **Menu pause**: opening ESC or options pauses playback; closing resumes after a short re-check to avoid a same-frame audio glitch.
- **Local behaviour**: no video download or cache, no player save data; only a plain-text runtime log under the mod's ModData folder.

## Requirements

| Item | Value |
|---|---|
| Game version | Probably Stolen 1.0.0 (all revisions) |
| Mod version | 1.0.0 (first public release) |
| Runtime | Unity 2021.3.45f2 / Mono, target framework `netstandard2.1` |
| Required | Approve the `network` capability in-game; access to Bilibili web/stream endpoints |
| Prerequisites | None |
| Bundled dependencies | None. All 12 runtime references are provided by the game and are not copied into the mod folder. |

## Installation

**Steam Workshop**: The current version has been released to the Steam Workshop; subscribe to this mod, confirm that it is enabled in-game, and restart the game.

**Manual**:

1. Close the game.
2. Download `TV_BiliBili_1.0.0.zip` from the `v1.0.0` Release and extract it to get the `TV_BiliBili` folder.
3. Put the **whole `TV_BiliBili` folder** into the game root `Mods\` (next to `Probably Stolen.exe`).
4. Start the game and enable the mod in the Mods list.
5. On first enable, approve `network`; without it the mod cannot fetch video streams.

You can also download this repository as a ZIP: the repository root is the mod folder, so copy the extracted folder to `Mods\TV_BiliBili\`. The only required runtime files are `TV_BiliBili.dll` and `manifest.xml`.

## Quick start

1. Open the shop calendar and switch to the fourth tab, **Player**.
2. Paste a full Bilibili video URL (for example `https://www.bilibili.com/video/BV1GJ411x7h7`) or a bare BV id.
3. Click play; use the seek bar to jump and the volume slider to adjust sound.
4. Switching tabs or closing the calendar keeps playback; day-end and leaving the shop pause it and returning resumes.
5. To uninstall, close the game and delete the whole `Mods\TV_BiliBili\` folder. Saves are unaffected.

## Screenshots

No public screenshots are bundled with this repository yet. Please refer to in-game display; the Release page will carry version-specific notes when needed.

## Known issues

Full list: [Known issues](Docs/KNOWN_ISSUES.md). Three key points:

- Only full Bilibili URLs and bare BV ids are supported. av ids, short links, anime / paid / membership-only content, livestreams and non-Bilibili URLs are not supported.
- Pillarboxes on portrait videos are expected; source black bars are not cropped. Non-baseline resolutions were not exhaustively verified.
- Day-end resume is an accepted observation; CPU increase measured +13.22%, close to but within the 15% limit.

## More docs

| Looking for | Go to |
|---|---|
| Feature details and boundaries | [Features](Docs/FEATURES.md) |
| Install, use, uninstall | [Install and use](Docs/安装与使用.md) |
| Compatibility, dependencies, capabilities | [Compatibility](Docs/兼容性与依赖.md) |
| Testing summary | [Testing](Docs/TESTING.md) |
| Architecture and runtime flow | [Architecture](Docs/ARCHITECTURE.md) |
| Known issues and workarounds | [Known issues](Docs/KNOWN_ISSUES.md) / [已知问题与限制](Docs/已知问题与限制.md) |
| Contributing / report an issue | [Contributing](Docs/CONTRIBUTING.md) |
| Build locally | [Development](DEVELOPMENT.md) |
| Version changes | [CHANGELOG](CHANGELOG.md) / [更新说明](Docs/更新说明.md) |
| Archive summary | [ARCHIVE](ARCHIVE.md) |

## License and feedback

- License: [MIT](LICENSE), `Copyright (c) 2026 Cicada`
- Source: <https://github.com/Cicada-Old/TV_BiliBili>
- Feedback: open an issue with game version, mod version, log excerpt and reproduction steps. Runtime log: `ModData/TV_BiliBili/`; game log: `%USERPROFILE%\AppData\LocalLow\Questing Goose Studio\Probably Stolen\Player.log`. Do not include saves or private information.
