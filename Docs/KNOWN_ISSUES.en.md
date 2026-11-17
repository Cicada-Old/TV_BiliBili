# Known Issues

This is the BetterBuild-style entry point. The full Chinese list is also in [已知问题与限制](已知问题与限制.md). The content below matches that file.

## Accepted observations

| Item | Status |
|---|---|
| Day-end resume regression | In-game time gate was unreachable; accepted by the project owner as an observation, not a defect |
| Leaving/returning the shop, manual pause not overridden, pause/resume latency | Passed by the project owner's hands-on acceptance, not by automated capture |
| CPU increase during playback | +13.22% against a 15% limit; close to but within the limit |
| Working set after 10 video switches | +29.88 MB and non-monotonic; within the 150 MB limit; +127 handles recorded only |

## Design limits

- Only full Bilibili video URLs and bare BV ids are supported. av ids, short links, anime / paid / membership-only content, livestreams and non-Bilibili URLs are not supported.
- Multi-part videos always use the first part.
- No manual 1080P / DASH selection; no login-only content.
- Source black bars are not cropped; pillarboxes on portrait videos are expected.
- 1920×1080 is the baseline; 1600×900 was verified; other resolutions were not exhaustively tested.
- The player is a calendar tab, not a standalone window. Clicking the display still opens the original calendar.

## Improvement items

Ambiguous `PLAY_FAIL` prefix, `lastError` generation ownership, a source comment/text mismatch, and bootstrap not printing the DLL fingerprint are deferred to the next substantive change. They do not block 1.0.0.
