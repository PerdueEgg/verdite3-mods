using System;
using System.IO;
using RecompOne.Runtime.Cdrom;

namespace Kf3.Mods.Text;

/// <summary>
/// The archives the text comes from, read once off the player's disc (SLUS-00255).
/// GAME.EXE numbers them as the order of CD/COM: 6 ITEM.T (signs, labels, Truth
/// Glass, item and spell descriptions, maps), 7 TALK.T (dialogue), 8 STALK.T (the
/// same dialogue in the smaller lettering of STATUS/RECORDS ▸ CONVERSATION). Each
/// archive is a u16 count, then u16 start sectors (count + 1 of them); an entry is
/// a 4-bit TIM with a 16-colour CLUT at (0x60, 0x1E0). FDAT.T entry 96 carries the
/// label font: a chain of VRAM blocks ((x, y, w, h) twice, then the pixels), among
/// them the page at (832, 256) and its palette at (80, 500).
/// </summary>
static class Disc
{
    public const int ItemArchive = 6, TalkArchive = 7, LogArchive = 8;

    public sealed class Archive
    {
        public readonly byte[] Data;
        readonly int[] _starts;
        public Archive(byte[] data)
        {
            Data = data;
            int n = BitConverter.ToUInt16(data, 0);
            _starts = new int[n + 1];
            for (int i = 0; i <= n; i++) _starts[i] = BitConverter.ToUInt16(data, 2 + 2 * i) * 2048;
        }
        public int Count => _starts.Length - 1;
        public int Offset(int i) => _starts[i];
        public int Length(int i) => i < 0 || i >= Count ? 0 : Math.Max(Math.Min(_starts[i + 1], Data.Length) - _starts[i], 0);
    }

    /// <summary>A 4-bit TIM with a CLUT: 16 colours, one byte a pixel.</summary>
    public sealed class Tim
    {
        public ushort[] Clut = new ushort[16];
        public byte[] Px = [];
        public int W, H;                   // pixels
        public int X, Y;                   // VRAM, X in halfwords
        public int PixelOffset;            // where its packed pixels start in the archive
        public int Bytes;                  // the whole TIM, header to last pixel
    }

    public static Archive? Talk, Log, Item, Fdat;
    public static string Error { get; private set; } = "";
    static bool _tried;

    public static bool Load()
    {
        if (_tried) return Talk != null;
        _tried = true;
        try
        {
            string? path = DiscPath();
            if (path == null) { Error = "the disc image was not found"; return false; }
            using (var disc = DiscFs.Open(path))
            {
                Item = new Archive(disc.ReadFile("CD/COM/ITEM.T"));
                Talk = new Archive(disc.ReadFile("CD/COM/TALK.T"));
                Log = new Archive(disc.ReadFile("CD/COM/STALK.T"));
                Fdat = new Archive(disc.ReadFile("CD/COM/FDAT.T"));
            }
            return true;
        }
        catch (Exception e)
        {
            Talk = null;
            Error = "could not read the disc: " + e.Message;
            Console.WriteLine($"[text] reading the disc: {e}");
            return false;
        }
    }

    static string? DiscPath()
    {
        string p = RecompOne.Runtime.Runtime.CdPath;
        if (!string.IsNullOrWhiteSpace(p) && File.Exists(p)) return p;
        foreach (var a in Environment.GetCommandLineArgs())
        {
            string ext = Path.GetExtension(a).ToLowerInvariant();
            if ((ext is ".cue" or ".bin" or ".chd" or ".iso") && File.Exists(a)) return a;
        }
        return null;
    }

    public static Archive? Of(int archive) => archive switch
    {
        ItemArchive => Item,
        TalkArchive => Talk,
        LogArchive => Log,
        _ => null,
    };

    /// <summary>Parse a 4-bit TIM with its CLUT; null if the bytes are anything else.</summary>
    public static Tim? ParseTim(Func<int, byte> b, int length)
    {
        uint U32(int at) => (uint)(b(at) | b(at + 1) << 8 | b(at + 2) << 16 | b(at + 3) << 24);
        int U16(int at) => b(at) | b(at + 1) << 8;
        if (length < 64 || U32(0) != 0x10 || U32(4) != 8) return null;
        int clutLen = (int)U32(8);
        if (clutLen != 12 + 32) return null;
        var t = new Tim();
        for (int i = 0; i < 16; i++) t.Clut[i] = (ushort)U16(20 + 2 * i);
        int img = 8 + clutLen;
        t.X = U16(img + 4); t.Y = U16(img + 6);
        t.W = U16(img + 8) * 4; t.H = U16(img + 10);
        t.PixelOffset = img + 12;
        t.Bytes = t.PixelOffset + t.W * t.H / 2;
        if (t.W <= 0 || t.H <= 0 || t.W > 1024 || t.H > 512 || t.Bytes > length) return null;
        t.Px = new byte[t.W * t.H];
        for (int k = 0; k < t.Px.Length; k += 2)
        {
            byte v = b(t.PixelOffset + (k >> 1));
            t.Px[k] = (byte)(v & 15);
            t.Px[k + 1] = (byte)(v >> 4);
        }
        return t;
    }

    public static Tim? ReadTim(int archive, int entry)
    {
        var arc = Of(archive);
        if (arc == null || arc.Length(entry) < 64) return null;
        int s = arc.Offset(entry);
        var t = ParseTim(k => arc.Data[s + k], arc.Length(entry));
        if (t != null) t.PixelOffset += s;
        return t;
    }

    /// <summary>The label font page (256x256, one byte a pixel; only the rows the
    /// disc fills are non-zero) and its palette, from FDAT.T entry 96's VRAM blocks.</summary>
    public static bool MenuPage(out byte[] page, out ushort[] clut)
    {
        page = new byte[256 * 256];
        clut = new ushort[16];
        if (Fdat == null || Fdat.Count <= 96) return false;
        var d = Fdat.Data;
        int s = Fdat.Offset(96), end = s + Fdat.Length(96);
        bool gotPage = false, gotClut = false;
        // The chain starts part-way into the entry; find it by its first block, the palette.
        for (int start = s; start + 16 <= end && !(gotPage && gotClut); start += 2)
        {
            if (BitConverter.ToUInt16(d, start) != 80 || BitConverter.ToUInt16(d, start + 2) != 500) continue;
            int p = start;
            while (p + 16 <= end)
            {
                int x = BitConverter.ToUInt16(d, p), y = BitConverter.ToUInt16(d, p + 2);
                int w = BitConverter.ToUInt16(d, p + 4), h = BitConverter.ToUInt16(d, p + 6);
                if (w == 0 || h == 0 || w == 0xFFFF || BitConverter.ToUInt32(d, p) != BitConverter.ToUInt32(d, p + 8)
                    || BitConverter.ToUInt32(d, p + 4) != BitConverter.ToUInt32(d, p + 12)) break;
                int data = p + 16, bytes = w * h * 2;
                if (data + bytes > end) break;
                if (x == 80 && y == 500 && w == 16 && h == 1)
                {
                    for (int i = 0; i < 16; i++) clut[i] = BitConverter.ToUInt16(d, data + 2 * i);
                    gotClut = true;
                }
                else if (x == 832 && y == 256 && w == 64)
                {
                    for (int k = 0; k < 256 * h; k += 2)
                    {
                        byte v = d[data + (k >> 1)];
                        page[k] = (byte)(v & 15);
                        page[k + 1] = (byte)(v >> 4);
                    }
                    gotPage = true;
                }
                p = data + bytes;
            }
        }
        return gotPage && gotClut;
    }

    /// <summary>A VRAM block in FDAT.T entry 96 by its rect: the offset of its pixels
    /// in Fdat.Data, or -1. A block's header is its rect twice, so it is found by that.</summary>
    public static int FdatBlock(int x, int y, int w, int h)
    {
        if (Fdat == null || Fdat.Count <= 96) return -1;
        var d = Fdat.Data;
        int s = Fdat.Offset(96), end = s + Fdat.Length(96);
        for (int p = s; p + 16 + w * h * 2 <= end; p += 2)
            if (BitConverter.ToUInt16(d, p) == x && BitConverter.ToUInt16(d, p + 2) == y
                && BitConverter.ToUInt16(d, p + 4) == w && BitConverter.ToUInt16(d, p + 6) == h
                && BitConverter.ToUInt64(d, p) == BitConverter.ToUInt64(d, p + 8))
                return p + 16;
        return -1;
    }

    public static double Lum(ushort c) => ((c & 31) * 2 + ((c >> 5) & 31) * 5 + ((c >> 10) & 31)) / 8.0;

    public static (byte R, byte G, byte B) Rgb(ushort c) =>
        ((byte)((c & 31) * 255 / 31), (byte)(((c >> 5) & 31) * 255 / 31), (byte)(((c >> 10) & 31) * 255 / 31));

    public static int Background(byte[] px)
    {
        var n = new int[16];
        foreach (var p in px) n[p]++;
        int bg = 0;
        for (int i = 1; i < 16; i++) if (n[i] > n[bg]) bg = i;
        return bg;
    }
}
