using System;
using System.Collections.Generic;
using System.Text;

namespace Kf3.Mods.Text;

/// <summary>
/// The unmodded game's own text in this mod's fonts (UsText.cs, written by
/// tools/build_us_text.py from the workbook's Current US column), as the layer
/// underneath whatever other mods set. Two parts, each with its own switch:
///
///  - Pictures: every sign, gravestone, label, dialogue line, Truth Glass card,
///    description and place card is redrawn from its US text (Pictures.SetBase),
///    and the short messages in play from theirs (Captions.SetBase). One another
///    mod gives text to takes that mod's text instead; when the mod takes it
///    back, this shows again.
///  - Normal case: menu-font text the game draws in capitals is drawn in its normal
///    capitalisation (Use Item, Long Sword, Checking memory card) for as long as
///    the draw lasts (MenuText.Draw); RAM keeps the game's own bytes. Only a label
///    that is still the disc's capitals is changed, so a translation that has
///    rewritten one keeps its own. The port's own settings page, all in capitals,
///    is drawn in title case too (Title).
/// </summary>
static class GameText
{
    public static bool ShowPictures = true;
    public static bool NormalCase = true;

    const byte SpaceCell = 0x7F;
    const int AddedCells = 0x50;      // the first cell this mod adds to the label page

    /// <summary>Put the US pictures and messages in play in place, or take them away.</summary>
    public static void SetPictures(bool on)
    {
        ShowPictures = on;
        Pictures.ClearBase();
        Captions.ClearBase();
        if (!on) return;
        foreach (var (key, text) in UsText.Pictures)
        {
            if (!int.TryParse(key.AsSpan(1), out int entry)) continue;
            if (key[0] == 'T')
            {
                Pictures.SetBase(Disc.TalkArchive, entry, "dialogue", text);
                Pictures.SetBase(Disc.LogArchive, entry, "log", text);     // the Conversation log's copy
            }
            else if (key[0] == 'I') Pictures.SetBase(Disc.ItemArchive, entry, "auto", text);
        }
        foreach (var (slot, text) in UsText.Captions) Captions.SetBase(slot, text);
        Captions.SetBase(Captions.Digits, "0123456789");                    // the gold counter's digits
    }

    // Each label's cells in normal case, by its capitals; null where it has none.
    static readonly Dictionary<string, byte[]?> _cased = new();

    /// <summary>A label's cells with its letters in normal case, the same number of
    /// cells and its padding as it was; or null to draw it as it is: switched off,
    /// lower case not in the menu font yet, a cell that is not the disc's (another
    /// mod's text), or text this does not know.</summary>
    public static List<byte>? Cased(List<byte> cells)
    {
        if (!NormalCase || !MenuFont.Ready || cells.Count == 0) return null;
        int lead = 0, end = cells.Count;
        while (lead < end && cells[lead] == SpaceCell) lead++;
        while (end > lead && cells[end - 1] == SpaceCell) end--;
        if (end == lead) return null;

        var sb = new StringBuilder(end - lead);
        for (int i = lead; i < end; i++)
        {
            byte b = cells[i];
            if (b == SpaceCell) { sb.Append(' '); continue; }
            if (b >= AddedCells) return null;           // an added cell: already someone's own text
            char ch = MenuFont.Char(b);
            if (ch == '\0') return null;
            sb.Append(ch);
        }
        string core = sb.ToString();

        if (!_cased.TryGetValue(core, out var enc))
        {
            enc = null;
            string? want = UsText.Menu.TryGetValue(core, out var w) ? w : Title(core);
            if (want != null && want != core && want.Length == core.Length)
            {
                var e = MenuFont.Encode(want, core.Length);
                if (e.Length == core.Length + 1) enc = e[..^1];   // every letter has a cell
            }
            _cased[core] = enc;
        }
        if (enc == null) return null;
        var outCells = new List<byte>(cells);
        for (int k = 0; k < enc.Length; k++) outCells[lead + k] = enc[k];
        return outCells;
    }

    // The words of the port's settings page (patches/PortSettings.cs, SettingsPage.cs)
    // and the map mod's page. A word that is all digits and signs, or holds a digit
    // (1440P, 16/9, X1.25, 4X), is kept, with its X and P in lower case.
    static readonly HashSet<string> Words = new(StringComparer.Ordinal)
    {
        "PORT", "SETTINGS", "DISPLAY", "GRAPHICS", "WORLD", "GAMEPLAY", "CONTROLS", "SOUND", "WINDOWED", "FULLSCREEN", "BORDERLESS", "RESOLUTION", "ASPECT", "HUD", "AT", "EDGES", "FRAME", "RATE", "UNCAPPED", "ORIGINAL", "NO", "SMOOTH", "TEXTURE", "FILTER", "MIPMAPS", "PER-PIXEL", "LIGHT", "AMB.", "OCCLUSION", "LOW", "MEDIUM", "HIGH", "SHADING", "DITHER", "NONE", "RENDER", "DISTANCE", "ENHANCED", "FOG", "CORNERS", "DEPTH", "WATER", "FULL", "CUSTOM", "RELOAD", "ON", "OFF", "DEATH", "COMPARE", "GEAR", "ENEMY", "TURN", "ACCEL", "INVERTED", "CAMERA", "STICK", "MOUSE", "SENS", "GYRO", "RUMBLE", "HD", "RUN", "SETUP", "SAVE", "DISCARD", "CHANGES", "CARRIED", "EVERY", "EVERYWHERE", "EYE", "HELD", "LAST", "USED", "SLOT", "VIEW", "X",
    };
    static readonly HashSet<string> Kept = new(StringComparer.Ordinal) { "HUD", "HD", "L1", "L2", "L3", "R1", "R2", "R3", "2D", "3D" };
    static readonly HashSet<string> Small = new(StringComparer.Ordinal) { "AT", "ON", "AS", "OF" };

    /// <summary>A settings-page label in title case, or null when it holds a word
    /// that is not the page's.</summary>
    public static string? Title(string label)
    {
        var words = label.Split(' ');
        for (int i = 0; i < words.Length; i++)
        {
            string w = words[i];
            if (w.Length == 0) continue;
            bool digit = false;
            foreach (char ch in w) if (ch >= '0' && ch <= '9') digit = true;
            if (digit) { words[i] = w.Replace('X', 'x').Replace('P', 'p'); continue; }
            if (!Words.Contains(w)) return null;
            if (Kept.Contains(w)) continue;
            if (i > 0 && Small.Contains(w)) { words[i] = w.ToLowerInvariant(); continue; }
            var parts = w.Split('-');
            for (int k = 0; k < parts.Length; k++)
                if (parts[k].Length > 0) parts[k] = parts[k].Substring(0, 1) + parts[k].Substring(1).ToLowerInvariant();
            words[i] = string.Join("-", parts);
        }
        return string.Join(" ", words);
    }

    /// <summary>For other mods: a label in the game's capitals as it will be drawn
    /// (LONG SWORD as Long Sword), so a mod that lays text out can measure it;
    /// anything else, or with the normal case off, as it is.</summary>
    public static string Shown(string text)
    {
        if (!NormalCase || !MenuFont.Ready) return text;
        string core = text.Trim(' ');
        if (core.Length == 0) return text;
        string? want = UsText.Menu.TryGetValue(core, out var w) ? w : Title(core);
        if (want == null || want.Length != core.Length) return text;
        int lead = text.Length - text.TrimStart(' ').Length;
        return text.Substring(0, lead) + want + text.Substring(lead + core.Length);
    }
}
