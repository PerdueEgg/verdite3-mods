# Verdite3 mods

Mods for [Verdite3](https://github.com/Voicedrew11/verdite3) (King's Field III, recompiled for PC).

| Mod | What it does |
|---|---|
| [Text](mods/text/README.md) | New fonts for all of the game's text, with full upper and lower case, proportional spacing, and the menus and names in normal capitalisation. Other mods draw their own text with it. |

More are on the way, starting with a new translation from the Japanese release.

## Installing

1. Download the latest release's `.zip` from the **Releases** page, or **Code > Download ZIP**.
2. Copy each mod's folder from `mods` (for example `mods/text`) into Verdite3's own `mods` folder, beside the game.
3. Start Verdite3 and switch the mod on in the **Mods** menu.

Tested with Verdite3 0.2.0 and the US disc (SLUS-00255).

## Tools

`tools/build_us_text.py` rebuilds the Text mod's built-in US text from the translation workbook. Python 3 only; see the [Text README](mods/text/README.md#where-the-games-text-comes-from).
