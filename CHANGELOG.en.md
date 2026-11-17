# CHANGELOG

Public release changes only. Internal development records are not included here.

## 1.0.0 — First public release

Release date: see the GitHub Release page.

### Added

- Fourth shop-calendar tab: **Player**.
- B站 webpage URLs and bare BV ids; multi-part videos always use the first part.
- In-tab video display and four-corner projection onto the in-world calendar display.
- Play / pause, seek bar, time display and volume slider.
- Lifecycle handling: keep playback on tab / calendar close; pause on day-end and leaving the shop, resume on return; stop and release on leaving the save.
- Pause while ESC / options menu is open, resume after it closes.
- Unified `播放失败` status with details in the `PLAY_FAIL` log line.
- `network` capability only; no video download, no cache, no player save data.

### Compatibility

- Probably Stolen 1.0.0 (all revisions).
- Target framework `netstandard2.1`; 12 runtime references provided by the game; no NuGet packages.
- Approve `network` on first enable.

### Known issues

See [Known issues](Docs/KNOWN_ISSUES.md).
