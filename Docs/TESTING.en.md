# Testing

This file publishes only the verifiable conclusions. Internal reports, screenshots, logs and verdict tables are not distributed.

## Summary

- Development stage T: 38 cases passed. 34 were verified in automated/manual capture; 4 were accepted by the project owner through hands-on play.
- Development stage H: all 13 design criteria passed.
- Release stage B: deterministic rebuild produced DLL md5 `b0e773a7597814123b7fa633ad1b6100`, byte-identical to the frozen anchor; 9 static/offline checks passed.
- Release stage E: independent review passed; one dangling index pointer was corrected and re-verified.

## Coverage

| Area | Coverage |
|---|---|
| Functional | Input parsing, playback controls, lifecycle, layout compatibility, identity/compliance |
| Performance | Cold start, warm start, seek, pause/resume, CPU increase, memory over 10 switches, log rate, event-method count |
| Compatibility | 1920×1080 baseline, 1600×900, 30-mod coexistence, game version 1.0.0 |

## Not covered

- Non-baseline resolutions were not exhaustively verified.
- Exact game revision is not pinned; `GameVersions=1.0.0` follows the official convention covering all revisions of 1.0.0.
- Stress/concurrent parsing, soak tests, GPU profiling, multi-resolution performance matrix, network injection and direct FPS assertions are out of scope.

Detailed thresholds and evidence chains remain in the local technical archive and are not expanded in this public repository.
