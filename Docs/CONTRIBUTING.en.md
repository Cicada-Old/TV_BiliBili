# Contributing

Bug reports and improvements are welcome. Please include the following information.

## When opening an issue

- Game version and OS; mod version (currently 1.0.0).
- Reproduction steps: what URL was pasted, which buttons were used, and in which scene it happened.
- Expected behaviour and actual behaviour.
- Relevant log excerpt: the plain-text log under `ModData/TV_BiliBili/` or the game's `Player.log`. Do not upload saves, account information or screenshots containing private data.
- Whether more than 30 mods are enabled; if so, list mods that may conflict with the calendar UI.

## Before submitting code

- Read [Architecture](ARCHITECTURE.md) and [Development](../DEVELOPMENT.md).
- Do not commit game DLLs, `bin/`, `obj/`, saves or internal evidence.
- Do not change the frozen 1.0.0 product fingerprint. Behaviour changes belong in a new version with CHANGELOG / ARCHIVE updates.
- Build with: `dotnet build Source/TV_BiliBili.csproj -c Release`.
- If you update public docs, keep Chinese and English versions aligned where practical; at minimum keep the Chinese version complete.

## Scope boundary

Danmaku, local video files, a standalone player, av ids / short links / anime / paid / membership-only content / livestreams, and projector prerequisites are out of scope and are not treated as defects.
