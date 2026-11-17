using System;
using System.Collections.Generic;
using RecompOne.Runtime.Context;
using RecompOne.Runtime.Memory;

namespace Kf3.Mods.Text;

/// <summary>
/// The short captions the game shows in play ("Level Up", "Locked", "Can't Used
/// Here", the gold counter's "Gold"...): one 4-bit page at VRAM (640, 256), 256x256,
/// uploaded from FDAT.T entry 96's VRAM blocks. It holds two columns of 18 slots,
/// each 128 wide and 14 high, the text centred in its slot:
///   slot k: u = (k / 18) * 128, v = (k % 18) * 14
/// Slot 17 is the gold counter's digits, ten 8-pixel cells from u 0; no mod may
/// set it, but the base layer redraws the digits in the dialogue font. Slot 25 (gold
/// obtained) is left-aligned on both discs, the counter drawn to its left, and
/// stays so.
///
/// Mods hand in text with SetCaption(owner, slot, text). The new text goes into
/// the page on the way up: a pre on LoadImage (the same hook as the label font)
/// rewrites each set slot inside any upload that covers it. The dialogue font is
/// used, its ink shades taken from the slot's own pixels as the pictures do.
/// How the game cuts each caption out of the page has not been traced; both
/// discs fill whole slots, so the text is kept inside its slot (at most 126 wide).
/// If the page went up before the mod was ready (it is loaded once, with the
/// common data), UploadOnce puts the set slots up directly from the player's
/// tick, from the disc's own copy of the page.
/// </summary>
static class Captions
{
    public const int PageX = 640, PageY = 256, SlotW = 128, SlotH = 14, Rows = 18, Slots = 36;
    const int AreaHw = 64, AreaH = 256;
    public const int Digits = 17, LeftAligned = 25;

    sealed record Entry(string Owner, string Text);
    static readonly Dictionary<int, Entry> _set = new();
    static readonly Dictionary<int, byte[]?> _drawn = new();
    static int _top = int.MinValue, _bottom;
    public static int Count => _set.Count;

    public static void Set(string owner, int slot, string text)
    {
        if (slot < 0 || slot >= Slots || slot == Digits) return;
        if (_set.TryGetValue(slot, out var e) && e.Owner != owner) return;
        _set[slot] = new Entry(owner, text);
        _drawn.Remove(slot);
    }

    // The game's own US wording (GameText), under whatever mods set.
    static readonly Dictionary<int, string> _base = new();

    public static void SetBase(int slot, string text)
    {
        if (slot < 0 || slot >= Slots) return;
        _base[slot] = text;
        _drawn.Remove(slot);
        _uploaded = false;
    }

    public static void ClearBase() { _base.Clear(); _drawn.Clear(); }

    /// <summary>Every slot with text: a mod's, or else the base layer's.</summary>
    static List<(int Slot, string Text)> All()
    {
        var all = new List<(int, string)>(_set.Count + _base.Count);
        foreach (var (slot, e) in _set) all.Add((slot, e.Text));
        foreach (var (slot, text) in _base) if (!_set.ContainsKey(slot)) all.Add((slot, text));
        return all;
    }

    public static void Clear(string owner)
    {
        var gone = new List<int>();
        foreach (var (k, e) in _set) if (e.Owner == owner) gone.Add(k);
        foreach (var k in gone) { _set.Remove(k); _drawn.Remove(k); }
    }

    static bool _seen, _uploaded;

    public static void Invalidate() { _drawn.Clear(); _top = int.MinValue; _uploaded = false; }

    /// <summary>The font's ink rows, for every letter: so all captions share a baseline.</summary>
    static void Extent(Font f)
    {
        if (_top != int.MinValue) return;
        int top = int.MaxValue, bottom = 0;
        foreach (var (c, g) in f.Glyphs)
        {
            bool cap = c >= 'A' && c <= 'Z';
            for (int r = 0; r < g.H; r++)
                for (int q = 0; q < g.W; q++)
                    if (g.Ink[r * g.W + q] > 0.05f)
                    {
                        if (cap) top = Math.Min(top, r);
                        bottom = Math.Max(bottom, r);
                    }
        }
        if (top == int.MaxValue) top = 0;
        _top = top; _bottom = bottom;
    }

    /// <summary>A slot's new pixels (SlotW x SlotH, palette indices), drawn over
    /// the old slot so its background and shades are kept. Null if nothing to do.</summary>
    static byte[]? Draw(int slot, string text, byte[] old)
    {
        var f = Fonts.Thin;
        Extent(f);
        int bg = Disc.Background(old);
        var count = new int[16];
        foreach (var p in old) count[p]++;
        var bag = new List<byte>();
        int total = 0;
        for (int i = 0; i < 16; i++) if (i != bg) total += count[i];
        for (int i = 0; i < 16; i++)
        {
            if (i == bg || count[i] == 0) continue;
            int n = Math.Max(1, count[i] * 64 / Math.Max(1, total));
            for (int k = 0; k < n; k++) bag.Add((byte)i);
        }
        if (bag.Count == 0) return null;
        var rng = new Random(text.GetHashCode());

        if (slot == Digits) return DrawDigits(f, text, bg, bag, rng);

        string line = text.Replace('\n', ' ');
        while (line.Length > 1 && Pictures.Measure(f, line) > SlotW - 2) line = line.Substring(0, line.Length - 1);
        if (line.Length < text.Length) Console.WriteLine($"[text] caption {slot} cut to fit: \"{line}\"");
        int width = Pictures.Measure(f, line);
        int x0 = slot == LeftAligned ? 0 : (SlotW - width) / 2;
        int y0 = (SlotH - (_bottom - _top + 1)) / 2 - _top;
        bool prop = Fonts.PropPictures;
        int[] at = prop ? f.Sp.Place(line, out _) : new int[line.Length];
        if (!prop) for (int i = 0; i < line.Length; i++) at[i] = i * f.CellW;

        var px = new byte[SlotW * SlotH];
        Array.Fill(px, (byte)bg);
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (c == ' ' || !f.Glyphs.TryGetValue(c, out var g)) continue;
            int x = x0 + at[i] - (prop ? f.Sp.Left(c) : 0);
            for (int r = 0; r < g.H; r++)
                for (int q = 0; q < g.W; q++)
                {
                    int yy = y0 + r, xx = x + q;
                    if (g.Ink[r * g.W + q] > 0.05f && yy >= 0 && xx >= 0 && yy < SlotH && xx < SlotW)
                        px[yy * SlotW + xx] = bag[rng.Next(bag.Count)];
                }
        }
        return px;
    }

    /// <summary>Slot 17, the gold counter's digits: the game cuts digit d from an
    /// 8-pixel cell at u = 8 * d (the disc's are 7 wide, 9 high, from the slot's
    /// top). Each is drawn in the middle of its cell's first 7 pixels, on the same
    /// rows as the captions, so the amount sits level with "Gold".</summary>
    static byte[] DrawDigits(Font f, string digits, int bg, List<byte> bag, Random rng)
    {
        const int Cell = 8, Wide = 7;
        var px = new byte[SlotW * SlotH];
        Array.Fill(px, (byte)bg);
        int y0 = (SlotH - (_bottom - _top + 1)) / 2 - _top;
        for (int i = 0; i < digits.Length && (i + 1) * Cell <= SlotW; i++)
        {
            if (!f.Glyphs.TryGetValue(digits[i], out var g)) continue;
            int left = f.Sp.Left(digits[i]), width = Math.Min(f.Sp.Width(digits[i]), Wide);
            int x0 = i * Cell + (Wide - width) / 2 - left;
            for (int r = 0; r < g.H; r++)
                for (int q = left; q < left + width && q < g.W; q++)
                {
                    int yy = y0 + r, xx = x0 + q;
                    if (g.Ink[r * g.W + q] > 0.05f && yy >= 0 && yy < SlotH && xx >= i * Cell && xx < (i + 1) * Cell)
                        px[yy * SlotW + xx] = bag[rng.Next(bag.Count)];
                }
        }
        return px;
    }

    // The gold counter's four digit sprites: records 2-5 of the table at 0x80081928
    // (20 bytes each: +3 v, +5 texture height, +8 y, +12 height). The game cuts 8x9
    // from the slot's top and draws it a row below the caption; the new digits are
    // taller, so each sprite is made the whole slot, 14 rows, level with the caption.
    const uint DigitSprites = 0x80081950;
    static bool _digitsPatched;

    /// <summary>From the player's tick: the digit sprites as tall as the slot while
    /// the digits are redrawn, and the game's own when they are not.</summary>
    public static void Tick(IMemory m)
    {
        bool want = _base.ContainsKey(Digits);
        if (want == _digitsPatched) return;
        byte th = (byte)(want ? SlotH : 9), y = (byte)(want ? 0xCB : 0xCC);
        byte wasTh = (byte)(want ? 9 : SlotH), wasY = (byte)(want ? 0xCC : 0xCB);
        for (uint k = 0; k < 4; k++)
        {
            uint r = DigitSprites + 20 * k;
            if (m.ReadU8(r + 3) != 0xEE || m.ReadU8(r + 5) != wasTh || m.ReadU16(r + 8) != wasY || m.ReadU16(r + 12) != wasTh)
            {
                Console.WriteLine("[text] captions: the digit sprites are not as expected; left alone");
                _digitsPatched = want;
                return;
            }
        }
        for (uint k = 0; k < 4; k++)
        {
            uint r = DigitSprites + 20 * k;
            m.WriteU8(r + 5, th); m.WriteU16(r + 8, y); m.WriteU16(r + 12, th);
        }
        _digitsPatched = want;
    }

    /// <summary>Pre on LoadImage(rect, data): each set slot into any upload over it.</summary>
    public static void BeforeLoadImage(CpuContext c, IMemory m)
    {
        if (_set.Count == 0 && _base.Count == 0) return;
        uint rect = c.A0, data = c.A1;
        int rx = (short)m.ReadU16(rect), ry = (short)m.ReadU16(rect + 2);
        int rw = (short)m.ReadU16(rect + 4), rh = (short)m.ReadU16(rect + 6);
        if (rw <= 0 || rh <= 0) return;
        if (rx >= PageX + AreaHw || rx + rw <= PageX || ry >= PageY + AreaH || ry + rh <= PageY) return;
        if (!Fonts.Ready()) return;
        if (rx <= PageX && rx + rw >= PageX + AreaHw && ry <= PageY && ry + rh >= PageY + AreaH) _seen = true;

        foreach (var (slot, text) in All())
        {
            int u0 = (slot / Rows) * SlotW, v0 = (slot % Rows) * SlotH;
            // Only an upload holding the whole slot: the old pixels come from it.
            int sx = PageX + u0 / 4, sy = PageY + v0;
            if (sx < rx || sx + SlotW / 4 > rx + rw || sy < ry || sy + SlotH > ry + rh) continue;
            uint At(int u, int v) => data + (uint)(((sy + v - ry) * rw + (sx + u / 4 - rx)) * 2 + (u & 3) / 2);

            if (!_drawn.TryGetValue(slot, out var px))
            {
                var old = new byte[SlotW * SlotH];
                for (int v = 0; v < SlotH; v++)
                    for (int u = 0; u < SlotW; u++)
                    {
                        byte b = m.ReadU8(At(u, v));
                        old[v * SlotW + u] = (byte)((u & 1) == 0 ? b & 15 : b >> 4);
                    }
                px = Draw(slot, text, old);
                _drawn[slot] = px;
            }
            if (px == null) continue;
            for (int v = 0; v < SlotH; v++)
                for (int u = 0; u < SlotW; u += 2)
                    m.WriteU8(At(u, v), (byte)(px[v * SlotW + u] | (px[v * SlotW + u + 1] << 4)));
        }
    }

    /// <summary>From the player's tick, once: if the page has not gone through
    /// LoadImage since the captions were set, put each set slot up directly.</summary>
    public static void UploadOnce(CpuContext c, IMemory m)
    {
        if (_uploaded || _seen || (_set.Count == 0 && _base.Count == 0)) return;
        _uploaded = true;
        if (!Fonts.Ready() || !Disc.Load()) return;
        int src = Disc.FdatBlock(PageX, PageY, AreaHw, AreaH);
        if (src < 0) { Console.WriteLine("[text] captions: the page was not found on the disc"); return; }
        var d = Disc.Fdat!.Data;
        uint scratch = (c.SP - 0x3000) & ~0xFu, data = scratch + 8;
        foreach (var (slot, text) in All())
        {
            int u0 = (slot / Rows) * SlotW, v0 = (slot % Rows) * SlotH;
            if (!_drawn.TryGetValue(slot, out var px))
            {
                var old = new byte[SlotW * SlotH];
                for (int v = 0; v < SlotH; v++)
                    for (int u = 0; u < SlotW; u++)
                    {
                        byte b = d[src + (v0 + v) * 128 + (u0 + u) / 2];
                        old[v * SlotW + u] = (byte)(((u0 + u) & 1) == 0 ? b & 15 : b >> 4);
                    }
                px = Draw(slot, text, old);
                _drawn[slot] = px;
            }
            if (px == null) continue;
            m.WriteU16(scratch, (ushort)(PageX + u0 / 4));
            m.WriteU16(scratch + 2, (ushort)(PageY + v0));
            m.WriteU16(scratch + 4, SlotW / 4);
            m.WriteU16(scratch + 6, SlotH);
            for (int k = 0; k < SlotW * SlotH; k += 2) m.WriteU8(data + (uint)(k / 2), (byte)(px[k] | (px[k + 1] << 4)));
            var snap = c.Snapshot();
            try
            {
                c.A0 = scratch; c.A1 = data; c.A2 = 0; c.A3 = 0;
                c.RA = 0x8001FFE0;
                Recompiled.KingsField3_game.func_80079DC8(c, m);
            }
            catch (Exception ex) { Console.WriteLine($"[text] caption upload: {ex.Message}"); return; }
            finally { c.Restore(snap); }
        }
        Console.WriteLine("[text] captions: put up directly");
    }
}
