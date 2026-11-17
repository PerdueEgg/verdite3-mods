using System;
using System.Collections.Generic;
using RecompOne.Runtime.Context;
using RecompOne.Runtime.Memory;
using KingsField3 = Recompiled.KingsField3_game;

namespace Kf3.Mods.Text;

/// <summary>
/// The label font: a 4-bit page at VRAM (832, 256), CLUT (80, 500), 8x15 cells,
/// cell g drawn from u = (g &amp; 15) * 8, v = (g &gt;&gt; 4) * 15 and advanced 7
/// (func_800261DC); 0x7F is a space and 0xFF the end of a string. The disc fills
///   0x00-0x19 A-Z, 0x20-0x29 0-9, 0x30-0x38 . , ' - = / * # !, 0x39 a box,
///   0x3A ?, 0x3B a middle dot
/// and keeps other art in 0x1A-0x1F, 0x2A-0x2F, 0x3C-0x3F and row 0x40 (rings),
/// so, unlike KF2's page, row 4 is not free. This adds, in rows 5-7, which the
/// disc leaves empty:
///   0x50 (   0x51-0x6A a-z   0x6B )   0x6C :   0x6D ;   0x6E &amp;   0x6F "
///   0x70 +   0x71 %   0x72 left arrow   0x73 right arrow
/// and writes the disc's own cells again from the menu font's character map
/// (Fonts.Menu), so an edit to a capital shows too. Cells it has no letter for
/// are left as the disc has them.
///
/// Two ways in, so the cells are there whenever the font is: a pre on LoadImage
/// (func_80079DC8, unbound libgpu, recompiled) writes them into any upload that
/// covers them, and once, from the player's tick, a direct upload of the area
/// from scratch memory far below the stack pointer. Until then, text is encoded
/// in capitals only (Ready).
/// </summary>
static class MenuFont
{
    public const uint LoadImage = 0x80079DC8;
    const int PageX = 832, PageY = 256, Rows = 8, CellW = 8, CellH = 15;
    const int AreaW = 128, AreaH = Rows * CellH;           // u 0..127, v 0..119
    const uint FakeRa = 0x8001FFE0;

    /// <summary>The disc's own cells.</summary>
    public static readonly Dictionary<char, int> DiscCells = MakeDisc();
    static readonly Dictionary<char, int> _cells = MakeAll();
    static readonly Dictionary<int, char> _chars = Reverse();

    static byte[,]? _px;
    static bool _uploaded;
    public static bool Ready { get; private set; }

    static Dictionary<char, int> MakeDisc()
    {
        var d = new Dictionary<char, int>();
        for (char c = 'A'; c <= 'Z'; c++) d[c] = c - 'A';
        for (char c = '0'; c <= '9'; c++) d[c] = 0x20 + (c - '0');
        string p = ".,'-=/*#!";
        for (int i = 0; i < p.Length; i++) d[p[i]] = 0x30 + i;
        d['■'] = 0x39;
        d['?'] = 0x3A;
        return d;
    }

    static Dictionary<char, int> MakeAll()
    {
        var d = new Dictionary<char, int>(MakeDisc());
        d['('] = 0x50;
        for (char c = 'a'; c <= 'z'; c++) d[c] = 0x51 + (c - 'a');
        d[')'] = 0x6B; d[':'] = 0x6C; d[';'] = 0x6D; d['&'] = 0x6E; d['"'] = 0x6F;
        d['+'] = 0x70; d['%'] = 0x71; d['←'] = 0x72; d['→'] = 0x73;
        return d;
    }

    static Dictionary<int, char> Reverse()
    {
        var r = new Dictionary<int, char> { [0x7F] = ' ', [0x3B] = '·' };
        foreach (var (c, i) in MakeAll()) r[i] = c;
        return r;
    }

    /// <summary>The cell for a character, or -1. Until the added cells are in
    /// VRAM, lower case falls back to capitals and the added punctuation to none.</summary>
    public static int Index(char c)
    {
        if (c == ' ') return 0x7F;
        if (c == '’') c = '\'';
        if (c == '·') return 0x3B;
        if (!_cells.TryGetValue(c, out int i)) return -1;
        if (i < 0x50 || Ready) return i;
        return c >= 'a' && c <= 'z' ? c - 'a' : -1;
    }

    /// <summary>The character a cell shows, or '\0'.</summary>
    public static char Char(int index) => _chars.TryGetValue(index, out char c) ? c : '\0';

    /// <summary>Text as cells, at most <paramref name="max"/> of them, then 0xFF.
    /// A character the font lacks is left out.</summary>
    public static byte[] Encode(string text, int max)
    {
        var b = new List<byte>(text.Length + 1);
        foreach (char c in text)
        {
            if (b.Count >= max) break;
            int i = Index(c);
            if (i >= 0) b.Add((byte)i);
        }
        b.Add(0xFF);
        return b.ToArray();
    }

    public static void Invalidate() { _px = null; _uploaded = false; }

    /// <summary>The area's pixels: the disc's page, with every cell the map has a
    /// letter for drawn over it. Null until the fonts are loaded.</summary>
    static byte[,]? Pixels()
    {
        if (_px != null) return _px;
        if (!Fonts.Ready() || !Disc.MenuPage(out var page, out _)) return null;
        var px = new byte[AreaH, AreaW];
        for (int v = 0; v < AreaH; v++)
            for (int u = 0; u < AreaW; u++) px[v, u] = page[v * 256 + u];
        foreach (var (c, index) in _cells)
        {
            if (!Fonts.Menu.Glyphs.TryGetValue(c, out var g)) continue;
            int row = index >> 4, col = index & 15;
            if (row >= Rows) continue;
            for (int r = 0; r < CellH; r++)
                for (int q = 0; q < CellW; q++)
                    px[row * CellH + r, col * CellW + q] = r < g.H && q < g.W ? g.Index[r * g.W + q] : (byte)0;
        }
        _px = px;
        return px;
    }

    /// <summary>Is this cell one we write (rather than the disc's other art)?</summary>
    static bool Ours(int u, int v)
    {
        int index = (v / CellH) * 16 + u / CellW;
        return _chars.ContainsKey(index) && index != 0x7F && index != 0x3B;
    }

    /// <summary>Pre on LoadImage(rect, data): the cells into any upload over them.</summary>
    public static void BeforeLoadImage(CpuContext c, IMemory m)
    {
        uint rect = c.A0, data = c.A1;
        int rx = (short)m.ReadU16(rect), ry = (short)m.ReadU16(rect + 2);
        int rw = (short)m.ReadU16(rect + 4), rh = (short)m.ReadU16(rect + 6);
        const int ax = PageX, ahw = AreaW / 4;
        if (rw <= 0 || rh <= 0) return;
        if (rx >= ax + ahw || rx + rw <= ax || ry >= PageY + AreaH || ry + rh <= PageY) return;
        var px = Pixels();
        if (px == null) return;
        for (int v = 0; v < AreaH; v++)
        {
            int vy = PageY + v;
            if (vy < ry || vy >= ry + rh) continue;
            for (int u = 0; u < AreaW; u++)
            {
                if (!Ours(u, v)) continue;
                int vx = ax + u / 4;
                if (vx < rx || vx >= rx + rw) continue;
                uint at = data + (uint)(((vy - ry) * rw + (vx - rx)) * 2 + (u & 3) / 2);
                byte b = m.ReadU8(at);
                int value = px[v, u];
                b = (u & 1) == 0 ? (byte)((b & 0xF0) | value) : (byte)((b & 0x0F) | (value << 4));
                m.WriteU8(at, b);
            }
        }
        if (rx <= ax && rx + rw >= ax + ahw && ry <= PageY && ry + rh >= PageY + AreaH) Ready = true;
    }

    /// <summary>From the player's tick: put the area up directly, once (and after a rebuild).</summary>
    public static void UploadOnce(CpuContext c, IMemory m)
    {
        if (_uploaded) return;
        var px = Pixels();
        if (px == null) return;
        _uploaded = true;
        uint scratch = (c.SP - 0x3000) & ~0xFu, data = scratch + 8;
        m.WriteU16(scratch, PageX);
        m.WriteU16(scratch + 2, PageY);
        m.WriteU16(scratch + 4, AreaW / 4);
        m.WriteU16(scratch + 6, AreaH);
        for (int v = 0; v < AreaH; v++)
            for (int u = 0; u < AreaW; u += 2)
                m.WriteU8(data + (uint)((v * AreaW + u) / 2), (byte)(px[v, u] | (px[v, u + 1] << 4)));
        var snap = c.Snapshot();
        try
        {
            c.A0 = scratch; c.A1 = data; c.A2 = 0; c.A3 = 0;
            c.RA = FakeRa;
            KingsField3.func_80079DC8(c, m);
            Ready = true;
            Console.WriteLine("[text] menu font: letters uploaded");
        }
        catch (Exception e) { Console.WriteLine($"[text] menu font upload: {e.Message}"); }
        finally { c.Restore(snap); }
    }
}
