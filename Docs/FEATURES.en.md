# Features

This file describes v1.0.0 features and boundaries. See [Install and use](安装与使用.md) for installation and [Compatibility](兼容性与依赖.md) for dependencies.

## Player entry

- A fourth tab named **Player** is added to the shop calendar.
- Clicking the in-world calendar display still opens the original calendar. There is no separate launcher button or window.

## Input and parsing

- Accepts full Bilibili video URLs (query strings allowed) and bare BV ids.
- Multi-part videos always use the first part.
- Not supported: av ids, `b23.tv` short links, anime / paid / membership-only content, livestreams, non-Bilibili URLs, 4K / login-only content.
- Empty input shows `请输入 B 站视频网址`; unrecognised input shows `无法识别 BV 号`.

## Playback and display

- Video plays inside the tab and is projected with four-corner fitting onto the in-world calendar display.
- Source black bars are not cropped; pillarboxes on portrait videos are expected.
- Controls: play / pause, seek bar, time display, volume slider.
- Status text: ready / resolving / loading / playing / paused / playback failed / invalid BV / empty URL.
- Failure details go to the `PLAY_FAIL` log line; the UI always shows the unified `播放失败` status.

## Lifecycle

- Switching tabs or closing the calendar keeps the URL, progress and playback state.
- Day-end and leaving the shop pause playback; returning resumes automatically.
- Opening ESC / options pauses; closing resumes after a short re-check to avoid a same-frame audio glitch.
- Leaving the save to the main menu stops playback and releases resources; re-entering the save can start playback again.

## Local behaviour

- No video download, cache or playlist is created.
- Player save data is not written; only a plain-text runtime log is written under the mod's ModData folder.
- The only capability is `network`, which the player must approve on first enable.
