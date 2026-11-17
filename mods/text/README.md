# Text

New fonts for all of King's Field III's text. It changes how the text looks, not what it says.

- **On its own** it letters the whole game in its fonts: every sign, gravestone, dialogue page, Truth Glass card, appraisal, place card and item or spell description, the Conversation log, the menus, the item, equipment and magic names, and the short messages in play (Level Up, Locked, the gold you pick up). The menus and names are shown in normal capitalisation (Use Item, Long Sword) rather than the game's capitals. The words are the US release's own. Only the opening and ending movies are left as they are.
- **With other mods** it is also how they put their own words in the game: mods like **Retranslation** hand it their text, and it draws that in the same fonts. A mod's text always comes first; where it has none, the game's own shows.

## Install

Copy the `text` folder into Verdite3's `mods` folder and switch **Text** on in the Mods panel, along with any mods that use it.

## Settings

| Setting | Default | What it does |
|---|---|---|
| The game's own signs, dialogue, descriptions and messages in these fonts | On | The game's own words, redrawn. Off shows the disc's pictures, unless another mod gives them text. |
| Menus and names in normal capitalisation | On | Use Item, Long Sword, and the port's own settings page in title case. Off keeps the game's capitals. A translation's own text is left as it is. |
| Proportional spacing and kerning in signs, dialogue and descriptions | On | Letters placed by their ink, lines wrapped by their real width. Applies to pictures shown from then on. |
| Proportional spacing and kerning in menus | On | The same for menu labels. Numbers the game draws itself stay in fixed columns. |
| Rebuild the character maps from the disc | (button) | Puts the game's original lettering back, cut from your own disc. See below. |

The settings page also shows where the `fonts` folder is and whether the fonts and the menu font's lower case are in place.

## What it provides

- **Three redrawn fonts with full upper and lower case:** the dialogue font (dialogue, the log, Truth Glass cards, appraisals, place cards, descriptions, gravestones, object labels and the messages in play), the sign font (signs), and the menu font. All three have A-Z, a-z, 0-9, `! " # % & ' ( ) * + , - . / : ; = ?` and arrows; the menu font also has the button box.
- **Lower case in the menu font:** the new cells are added to the game's menu font in video memory, in rows the disc leaves empty, and kept there.
- **Proportional spacing and kerning:** letters are placed by their ink rather than in fixed cells, and pairs with room to spare (To, LT, AV) are pulled together a pixel. In pictures, lines are wrapped by their real width. In menus, a label never runs more than 3 pixels wider than the game's own (otherwise it is drawn the game's way), and labels the game right-aligns by padding them with spaces end flush with the numbers in the same column: the status page's Condition (Normal, Poison, ...) lines up with the numbers above it whatever its last letter.
- **Pictures sized to their text:** KF3 crops every message picture to its own text. A redrawn one is sized to its new text and is not held to the size of the disc's: a picture that needs more room than the game set aside for it is put up from a block of the game's own memory instead. What is left is the screen: a full-screen message holds 13 lines of dialogue. Each picture keeps the disc picture's colours, so place cards and signs stay yellow and dialogue white.
- **The game's own text:** the US text of every picture and message in play, read from the translation workbook and built into the mod (`UsText.cs`). Signs keep their capitals where the US wrote them so. The menus' and names' capitals are drawn in normal case only while they are drawn; the game's memory keeps its own text, so a translation that rewrites a name still finds it.
- **The short messages in play** (Level Up, Locked, Tombstone, the gold counter, 36 in all) are one picture on the disc. It is redrawn in the dialogue font, centred like the disc's, and the gold counter's digits with it.
- **Redrawn pictures for other mods:** any picture a mod gives new text is drawn with that text instead, and goes back to the game's own when the mod takes it away.
- **A shared string table:** mods use it to share wording. For example, a translation tells the Encyclopedia what to call things.

## The character maps

The fonts are character maps in the `fonts` folder inside the Text mod's own folder. The mod ships with its own redrawn letters; the Text settings show where the folder is.

| File | What it is |
|---|---|
| `dialogue.png`, `sign.png`, `menu.png` | Every letter of each font. Edit these and restart the game to use your changes. |
| `dialogue.json`, `sign.json`, `menu.json` | Where each letter sits in its map. |
| `*_template.png` | The same grid, empty, for drawing a whole new font. Save it over the font's map to use it. |
| `dialogue_spacing.json`, `sign_spacing.json`, `menu_spacing.json` | Each font's spacing: the gap between letters, a space's width, extra space between lines (`lineGap`), how far kerning may tighten a pair, per-letter or per-pair overrides such as `"advance": { "i": 3 }` or `"kern": { "Te": -1 }`, and `clear`, the fewest pixels kept between a letter and its neighbours, never kerned closer. The dialogue font ships with `"clear": { "'": 1, ".": 2 }`, so kerning never tucks an apostrophe or a period against the letters beside it ("who're"). |

- **Picture fonts** (dialogue, sign): ink is grey. 232 is full ink, darker greys are fainter, black is clear.
- **Menu font:** uses the menu palette's own colours. Any other colour is read as the nearest one.
- **Guides:** the green frames and blue ticks mark each cell, its cap height and its baseline. They're never drawn in game, but they do say where each letter is: a cell can be made wider or taller by moving its frame, and the letter is read at that size. Keep the letters in the same order. The menu font is the exception: the game's menu cells are fixed at 8x15, so a menu letter can't be bigger than that (the console says so if one is).
- **Empty cells:** a cell left empty is a letter the font doesn't have.

**Rebuild the character maps from the disc** in the Text settings is for anyone who wants the game's original lettering back. It replaces the maps with letters cut from your own disc, plus a few hand-drawn letters in the same style for characters the game's lettering doesn't have, and a sign font made by thickening the dialogue letters. It replaces the included fonts and any edits; reinstall the mod to get the included fonts back. A font whose map is missing is rebuilt this way on launch.

## Where the game's text comes from

`UsText.cs` is written from the translation workbook's **Current US** column by `tools/build_us_text.py` (in this repository's `tools` folder; it needs Python 3 only). The workbook ships with the Retranslation mod:

```
python3 tools/build_us_text.py mods/retranslation/KF3_retranslation.ods mods/text/UsText.cs
```

Pictures keep the US text as it is, line breaks included, with one space after a sentence where the disc's lettering had two. Menu text and names are put in normal case by the tool's rules (title case for menus and names, sentence case for the memory-card messages, abbreviations kept); a string they get wrong goes in its `OVERRIDES`.

## For mod authors

- **Dependency:** if your mod needs the Text mod, list `"kf3.text"` under `"dependencies"` in your `mod.json`. It then loads first, and the loader skips your mod with a message if it's missing.
- **Client file:** copy `TextApi.cs` from the Retranslation mod into your mod, changing only its namespace.
- **Archives:** 6 = ITEM.T, 7 = TALK.T, 8 = STALK.T (the Conversation log).
- **Styles:** `"dialogue"`, `"log"` (the log's 14-pixel lines), `"desc"` (the menus' description box: 15-pixel lines, top left kept), `"card"` (sign font, centred, lines balanced to even lengths), `"label"` (dialogue font, centred and balanced, 14-pixel lines, for gravestones and object labels) or `"auto"` (chosen by archive and entry).

| Call | What it does |
|---|---|
| `TextApi.MenuEncode(text, max)` | Menu font cells for a game string, then `0xFF`. Lower case once `TextApi.MenuReady`. |
| `TextApi.MenuIndex(c)` / `TextApi.MenuChar(i)` | Character to cell and back. |
| `TextApi.MenuShown(text)` | A label in the game's capitals as the Text mod will draw it (`LONG SWORD` as `Long Sword` with normal case on), for measuring text you lay out yourself. |
| `TextApi.MenuPlace(text)` | A string's menu cells, each one's x offset and the total width, spaced as the menus are. |
| `TextApi.SetPicture(owner, archive, entry, style, text)` | Redraws a disc picture wherever it's shown. |
| `TextApi.ClearPictures(owner)` | Removes everything that owner set. |
| `TextApi.NextPicture(style, text)` | Redraws the very next picture shown. |
| `TextApi.SetCaption(owner, slot, text)` / `TextApi.ClearCaptions(owner)` | Words a message in play, slot 0-35 in the page's order (slot 17 is the gold counter's digits and can't be set). |
| `TextApi.SetString(key, value)` / `TextApi.GetString(key)` | The shared string table. |
| `TextApi.Coverage(style, text, width)` | The text wrapped to a width in pixels and drawn as ink coverage, a byte a pixel, for a mod that letters something itself (the Japanese Disc mod's movie text). |
