using System;
using System.Collections.Generic;
using RecompOne.Runtime.Context;
using RecompOne.Runtime.Memory;
using KingsField3 = Recompiled.KingsField3_game;

namespace Kf3.Mods.Text;

/// <summary>
/// Every picture-format text: 4-bit TIMs from ITEM.T (6), TALK.T (7) and STALK.T
/// (8), each just big enough for its own text, uploaded by func_80043B38(buf)
/// (OpenTIM, ReadTIM, then LoadImage of the CLUT and the image). Callers: a sign,
/// a line of dialogue or a Truth Glass card (func_800441D4, from the TIM loaded to
/// the menu buffer *0x80199154 = 0x80116954), the Conversation log (STALK.T), the
/// item and spell descriptions in the menus and the maps. The full-screen ones are
/// drawn as one fixed quad of texture page (0x3C0, 0x100), u 0-255 and v 0-208,
/// after ClearImage of that rect, so a picture of any size up to 256x208 shows,
/// placed by the x and y in its own header.
///
/// Mods hand in text two ways:
///  - Set(owner, archive, entry, style, text): that disc picture, wherever the
///    game shows it from, is redrawn with the text. It is recognised by its
///    fingerprint (header, palette and pixels), so a picture another mod already
///    changed is left alone.
///  - Next(style, text): the very next picture uploaded is redrawn with the text,
///    whatever it was.
///
/// The new picture keeps the old one's palette (so a place card stays yellow, a
/// Truth Glass card white) and its grain (each inked pixel takes one of the shades
/// the old one used, as often as it used them).
///
/// Its size is the new text's, whatever the disc entry's was. Every caller loads
/// an entry into a buffer (the menus malloc exactly the entry's size), calls
/// func_80043B38, which uploads it there and then (OpenTIM, ReadTIM, LoadImage,
/// DrawSync), and frees or forgets the buffer; nothing reads it afterwards. So a
/// picture that needs more bytes than the buffer has is built in a block of the
/// game's own heap (func_800194F0), the upload pointed at that instead, and the
/// block freed (func_80019518) in the post hook. Only if the heap has no room is
/// the text cut to what the buffer holds, with a line in the console.
///
/// What is left is the screen: a full-screen message is one quad of 256x208, and
/// the menus' description box and the log show what they show. The menus'
/// descriptions and the log keep their top left corner and grow down and right;
/// the others keep their centre.
///
/// Under what mods set is the base layer (SetBase): the game's own US text, put
/// there by GameText, so every picture is drawn in these fonts even with no other
/// mod. A mod's text for a picture always comes first.
///
/// Styles:
///   "dialogue"  dialogue font, left, 16-pixel lines, wrapped at 240
///   "log"       the same at 14-pixel lines, for STALK.T
///   "desc"      dialogue font, left, 15-pixel lines, wrapped at 248 (the menus'
///               description box), as KF2 draws its descriptions
///   "card"      sign font, centred, its lines balanced to even lengths, at most 248:
///               the signs, which both discs letter large
///   "label"     dialogue font, centred and balanced the same way, 14-pixel lines:
///               gravestones and object labels, which both discs letter small
///   "auto"      by archive and entry: TALK.T dialogue, STALK.T log, ITEM.T
///               150-239 card, 240-269 label, 390-720 desc, the rest dialogue
/// </summary>
static class Pictures
{
    const int MaxW = 256, MaxH = 208, PageX = 960, PageY = 256, PageH = 240;
    const int HeaderBytes = 8 + 12 + 32 + 12;

    sealed record Entry(string Owner, int Archive, int Number, string Style, string Text);

    static readonly Dictionary<(int, int), Entry> _set = new();
    static readonly Dictionary<(int, int), Entry> _base = new();     // the game's own US text (GameText)
    static readonly Dictionary<ulong, (int, int)> _byPrint = new();
    static bool _indexed;
    static (string Style, string Text)? _next;
    public static int Shown { get; private set; }

    public static void Set(string owner, int archive, int entry, string style, string text)
    {
        _set[(archive, entry)] = new Entry(owner, archive, entry, style, text);
    }

    public static void Clear(string owner)
    {
        var gone = new List<(int, int)>();
        foreach (var (k, e) in _set) if (e.Owner == owner) gone.Add(k);
        foreach (var k in gone) _set.Remove(k);
    }

    public static int Count => _set.Count;

    /// <summary>The text a picture has when no mod gives it any.</summary>
    public static void SetBase(int archive, int entry, string style, string text)
    {
        _base[(archive, entry)] = new Entry("", archive, entry, style, text);
    }

    public static void ClearBase() => _base.Clear();

    public static int BaseCount => _base.Count;

    public static void Next(string style, string text) => _next = (style, text);

    static ulong Print(Func<int, byte> b, int bytes)
    {
        ulong h = 0xCBF29CE484222325UL;
        for (int k = 8; k < bytes; k++) h = (h ^ b(k)) * 0x100000001B3UL;   // CLUT block, image header, pixels
        return h;
    }

    /// <summary>Every 4-bit TIM in the three archives, by fingerprint, once.</summary>
    static void IndexAll()
    {
        _indexed = true;
        foreach (int archive in new[] { Disc.ItemArchive, Disc.TalkArchive, Disc.LogArchive })
        {
            var arc = Disc.Of(archive);
            if (arc == null) continue;
            for (int e = 0; e < arc.Count; e++)
            {
                int len = arc.Length(e), s = arc.Offset(e);
                if (len < 64) continue;
                var t = Disc.ParseTim(k => arc.Data[s + k], len);
                if (t == null) continue;
                _byPrint[Print(k => arc.Data[s + k], t.Bytes)] = (archive, e);
            }
        }
        Console.WriteLine($"[text] {_byPrint.Count} pictures indexed");
    }

    const uint FakeRa = 0x8001FFE0;
    static uint _block;      // a heap block holding the picture being uploaded, freed after

    /// <summary>A block of the game's heap, or 0 when it has none to give.</summary>
    static uint Alloc(CpuContext c, IMemory m, int bytes)
    {
        var snap = c.Snapshot();
        try
        {
            c.A0 = (uint)bytes; c.RA = FakeRa;
            KingsField3.func_800194F0(c, m);
            return c.V0;
        }
        catch (Exception e) { Console.WriteLine($"[text] picture block: {e.Message}"); return 0; }
        finally { c.Restore(snap); }
    }

    /// <summary>Post on func_80043B38: the upload is done, so the block can go.</summary>
    public static void AfterUpload(CpuContext c, IMemory m)
    {
        if (_block == 0) return;
        uint block = _block;
        _block = 0;
        var snap = c.Snapshot();
        try { c.A0 = block; c.RA = FakeRa; KingsField3.func_80019518(c, m); }
        catch (Exception e) { Console.WriteLine($"[text] picture block: {e.Message}"); }
        finally { c.Restore(snap); }
    }

    /// <summary>Pre on func_80043B38, with the TIM at a0.</summary>
    public static void BeforeUpload(CpuContext c, IMemory m)
    {
        uint buf = c.A0;
        if (_next == null && _set.Count == 0 && _base.Count == 0) return;
        if (!Fonts.Ready()) return;
        var old = Disc.ParseTim(k => m.ReadU8(buf + (uint)k), 0x10000);
        if (old == null) return;
        if (!_indexed) IndexAll();

        string style, text;
        int archive = -1, entry = -1, capacity = old.Bytes;
        if (_next is { } next)
        {
            _next = null;
            (style, text) = next;
        }
        else
        {
            if (!_byPrint.TryGetValue(Print(k => m.ReadU8(buf + (uint)k), old.Bytes), out var key)) return;
            if (!_set.TryGetValue(key, out var e) && !_base.TryGetValue(key, out e)) return;
            (style, text, archive, entry) = (e.Style, e.Text, e.Archive, e.Number);
            capacity = Math.Max(capacity, Disc.Of(archive)?.Length(entry) ?? 0);
        }
        if (style == "auto") style = Auto(archive, entry);

        string name = archive >= 0 ? $"{archive}:{entry}" : "next picture";
        var tim = Draw(style, text, old, int.MaxValue, out string? note);
        if (tim == null) return;
        uint at = buf;
        if (tim.Length > capacity)
        {
            // More than the game's buffer holds: upload it from a heap block instead.
            _block = Alloc(c, m, tim.Length);
            if (_block != 0) at = _block;
            else
            {
                tim = Draw(style, text, old, capacity, out note);
                if (tim == null) return;
                Console.WriteLine($"[text] {name}: no room in the game's heap; cut to the {capacity} bytes of its own buffer");
            }
        }
        if (note != null) Console.WriteLine($"[text] {name}: {note}");
        for (int k = 0; k < tim.Length; k++) m.WriteU8(at + (uint)k, tim[k]);
        c.A0 = at;
        Shown++;
    }

    static string Auto(int archive, int entry)
    {
        if (archive == Disc.TalkArchive) return "dialogue";
        if (archive == Disc.LogArchive) return "log";
        if (entry >= 150 && entry < 240) return "card";
        if (entry >= 240 && entry < 270) return "label";
        if (entry >= 390 && entry <= 720) return "desc";
        return "dialogue";
    }

    // ---- layout ----

    static Font FontOf(string style) => style == "card" ? Fonts.Bold : Fonts.Thin;
    static int WrapWidth(string style) => style switch { "card" => 248, "label" => 248, "desc" => 248, "log" => 232, _ => 240 };
    static bool Centred(string style) => style == "card" || style == "label";
    static int Pitch(string style, Font f) =>
        (style switch { "card" => 18, "label" => 14, "desc" => 15, "log" => 14, _ => 16 }) + Math.Max(0, Fonts.Tallest(f) - f.CellH) + f.Sp.LineGap;

    static bool Wide(Font f, char c) => !f.Menu && (c == '→' || c == '←');

    /// <summary>A line's inked width in pixels: proportional (Spacing.cs), or in
    /// whole cells when proportional spacing is off.</summary>
    public static int Measure(Font f, string s)
    {
        if (Fonts.PropPictures) { f.Sp.Place(s, out int w); return w; }
        int n = 0;
        foreach (var c in s) n += Wide(f, c) ? 2 * f.CellW : f.CellW;
        return n;
    }

    /// <summary>Two or more spaces after a sentence: the disc's monospaced lettering
    /// (and the OCR'd US text) has them, proportional text wants one. The gaps that
    /// line up a description's header columns follow a letter or digit, so they stay.</summary>
    static readonly System.Text.RegularExpressions.Regex SentenceGap = new(@"([.!?,;:""')])  +");

    /// <summary>Greedy word wrap to a width in pixels; a line break in the text
    /// always starts a new line.</summary>
    public static List<string> Wrap(string text, Font f, int px)
    {
        text = SentenceGap.Replace(text, "$1 ");
        var lines = new List<string>();
        foreach (var para in text.Split('\n'))
        {
            // A line that fits is kept as written, so the spaces that line up a
            // description's header ("Special Weapon   94 cm  0.9kg") stay.
            if (Measure(f, para) <= px) { lines.Add(para.TrimEnd(' ')); continue; }
            var line = "";
            foreach (var raw in para.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                var word = raw;
                while (word.Length > 1 && Measure(f, word) > px)
                {
                    int k = word.Length - 1;
                    while (k > 1 && Measure(f, word.Substring(0, k)) > px) k--;
                    if (line.Length > 0) { lines.Add(line); line = ""; }
                    lines.Add(word.Substring(0, k));
                    word = word.Substring(k);
                }
                if (line.Length == 0) line = word;
                else if (Measure(f, line + " " + word) <= px) line += " " + word;
                else { lines.Add(line); line = word; }
            }
            lines.Add(line);
        }
        return lines;
    }

    /// <summary>Each paragraph wrapped to the narrowest width that still takes as
    /// few lines as the full width, so a centred sign's lines come out even.</summary>
    public static List<string> Balanced(string text, Font f, int px)
    {
        var lines = new List<string>();
        foreach (var para in text.Split('\n'))
        {
            int n = Wrap(para, f, px).Count, lo = 1, hi = px;
            while (lo < hi)
            {
                int mid = (lo + hi) / 2;
                if (Wrap(para, f, mid).Count == n) hi = mid; else lo = mid + 1;
            }
            lines.AddRange(Wrap(para, f, hi));
        }
        return lines;
    }

    // Spaces that line up the columns of a description's header ("Special Weapon   94cm")
    // are kept as they are when a line has no wrapping to do.
    static List<string> Layout(string style, string text)
    {
        var f = FontOf(style);
        if (Centred(style)) return Balanced(text, f, WrapWidth(style));
        return Wrap(text, f, WrapWidth(style));
    }

    public static int Lines(string style, string text) => Layout(style, text).Count;

    /// <summary>Text wrapped to a width and drawn as ink coverage (0 to 1, a byte a
    /// pixel scaled to 255), in a picture style's font, spacing and line pitch: for
    /// a mod that draws the lettering itself, as the movies' English is drawn.</summary>
    public static (byte[] Ink, int W, int H) Coverage(string style, string text, int width)
    {
        var font = FontOf(style);
        var lines = Wrap(text, font, width);
        while (lines.Count > 0 && lines[^1].Length == 0) lines.RemoveAt(lines.Count - 1);
        int pitch = Pitch(style, font), w = 1;
        foreach (var l in lines) w = Math.Max(w, Measure(font, l) + 2);
        int h = Math.Max(1, lines.Count * pitch + 2);
        var ink = new byte[w * h];
        bool prop = Fonts.PropPictures;
        for (int li = 0; li < lines.Count; li++)
        {
            string line = lines[li];
            int[] at = prop ? font.Sp.Place(line, out _) : new int[line.Length];
            if (!prop) for (int i = 0, pen = 0; i < line.Length; i++) { at[i] = pen; pen += Wide(font, line[i]) ? 2 * font.CellW : font.CellW; }
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (c == ' ' || !font.Glyphs.TryGetValue(c, out var g)) continue;
                int x = at[i] - (prop ? font.Sp.Left(c) : 0), y = li * pitch;
                for (int r = 0; r < g.H; r++)
                    for (int q = 0; q < g.W; q++)
                    {
                        int yy = y + r, xx = x + q;
                        if (yy < 0 || xx < 0 || yy >= h || xx >= w) continue;
                        byte v = (byte)Math.Clamp((int)(g.Ink[r * g.W + q] * 255 + 0.5f), 0, 255);
                        if (v > ink[yy * w + xx]) ink[yy * w + xx] = v;
                    }
            }
        }
        return (ink, w, h);
    }

    /// <summary>The new TIM: the old one's CLUT, a new image block. Null when the
    /// text is empty.</summary>
    static byte[]? Draw(string style, string text, Disc.Tim old, int capacity, out string? note)
    {
        note = null;
        var font = FontOf(style);
        var lines = Layout(style, text);
        while (lines.Count > 0 && lines[^1].Length == 0) lines.RemoveAt(lines.Count - 1);
        if (lines.Count == 0) return null;
        bool card = Centred(style);
        int pitch = Pitch(style, font);
        int widest = 0;
        foreach (var l in lines) widest = Math.Max(widest, Measure(font, l));
        int w = Math.Min(MaxW, (widest + 2 + 7) & ~7);                 // a multiple of 8, so the centre stays on a halfword
        int maxLines = Math.Min(MaxH / pitch, Math.Max(1, (int)(((long)capacity - HeaderBytes) * 2 / Math.Max(w, 8)) / pitch));
        if (lines.Count > maxLines)
        {
            note = $"{lines.Count} lines do not fit; shown {maxLines}";
            lines.RemoveRange(maxLines, lines.Count - maxLines);
        }
        int h = lines.Count * pitch;
        if (h > MaxH) h = MaxH;

        // The palette's ink: each shade the old picture used, as often as it used it.
        int bg = Disc.Background(old.Px);
        var count = new int[16];
        foreach (var p in old.Px) count[p]++;
        var bag = new List<byte>();
        int total = 0;
        for (int i = 0; i < 16; i++) if (i != bg) total += count[i];
        for (int i = 0; i < 16; i++)
        {
            if (i == bg || count[i] == 0) continue;
            int n = Math.Max(1, count[i] * 64 / Math.Max(1, total));
            for (int k = 0; k < n; k++) bag.Add((byte)i);
        }
        if (bag.Count == 0)
        {
            int best = bg == 0 ? 1 : 0;
            for (int i = 0; i < 16; i++) if (i != bg && Disc.Lum(old.Clut[i]) > Disc.Lum(old.Clut[best])) best = i;
            bag.Add((byte)best);
        }
        var rng = new Random(text.GetHashCode());

        var px = new byte[w * h];
        Array.Fill(px, (byte)bg);
        bool prop = Fonts.PropPictures;
        for (int li = 0; li < lines.Count; li++)
        {
            string line = lines[li];
            int width = Measure(font, line);
            int x0 = card ? (w - width) / 2 : 1;
            int y = li * pitch;
            int[] at = prop ? font.Sp.Place(line, out _) : new int[line.Length];
            if (!prop) for (int i = 0, pen = 0; i < line.Length; i++) { at[i] = pen; pen += Wide(font, line[i]) ? 2 * font.CellW : font.CellW; }
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (c == ' ') continue;
                if (!font.Glyphs.TryGetValue(c, out var g))
                {
                    Console.WriteLine($"[text] no letter '{c}' in the {font.Name} font");
                    continue;
                }
                int x = x0 + at[i] - (prop ? font.Sp.Left(c) : 0);
                for (int r = 0; r < g.H; r++)
                    for (int q = 0; q < g.W; q++)
                    {
                        int yy = y + r, xx = x + q;
                        if (g.Ink[r * g.W + q] > 0.05f && yy >= 0 && xx >= 0 && yy < h && xx < w)
                            px[yy * w + xx] = bag[rng.Next(bag.Count)];
                    }
            }
        }

        // Where it goes: top left kept for a picture at u 0, the centre kept otherwise.
        int vx, vy;
        if (style == "desc" || style == "log" || (old.X == PageX && old.W < MaxW)) {
            vx = Math.Min(old.X, PageX + (MaxW - w) / 4);         // still inside the page when it has grown
            vy = Math.Min(old.Y, PageY + PageH - h);
        }
        else
        {
            int cx = (old.X - PageX) * 4 + old.W / 2, cy = old.Y - PageY + old.H / 2;
            int left = Math.Clamp(cx - w / 2, 0, MaxW - w) & ~3;
            vx = PageX + left / 4;
            vy = PageY + Math.Clamp(cy - h / 2, 0, MaxH - h);
        }

        int bytes = HeaderBytes + w * h / 2;
        var tim = new byte[bytes];
        void U32(int at, uint v) { tim[at] = (byte)v; tim[at + 1] = (byte)(v >> 8); tim[at + 2] = (byte)(v >> 16); tim[at + 3] = (byte)(v >> 24); }
        void U16(int at, int v) { tim[at] = (byte)v; tim[at + 1] = (byte)(v >> 8); }
        U32(0, 0x10); U32(4, 8);
        U32(8, 12 + 32); U16(12, 0x60); U16(14, 0x1E0); U16(16, 16); U16(18, 1);
        for (int i = 0; i < 16; i++) U16(20 + 2 * i, old.Clut[i]);
        int img = 8 + 12 + 32;
        U32(img, (uint)(12 + w * h / 2)); U16(img + 4, vx); U16(img + 6, vy); U16(img + 8, w / 4); U16(img + 10, h);
        int p0 = img + 12;
        for (int k = 0; k < w * h; k += 2) tim[p0 + (k >> 1)] = (byte)(px[k] | (px[k + 1] << 4));
        return tim;
    }
}
