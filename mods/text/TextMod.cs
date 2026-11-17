using System;
using System.Collections.Generic;
using System.IO;
using ImGuiNET;
using RecompOne.Runtime.Context;
using RecompOne.Runtime.Memory;
using RecompOne.Runtime.Modding;

namespace Kf3.Mods.Text;

/// <summary>
/// The game's text in new fonts, King's Field II (US) / King's Field III. It owns the
/// game's fonts (Fonts.cs), the label font's added cells (MenuFont.cs) and every
/// redrawn picture (Pictures.cs). On its own it letters the unmodded game: the US
/// text of every picture and message in play, and the menus in their normal case
/// (GameText.cs, UsText.cs). A mod that wants text of its own hands it over instead
/// of carrying its own fonts. Ported from Verdite2's Text mod (kf2.text); the shape and
/// the published functions are the same, the addresses and fonts are this game's.
///
/// A mod is compiled on its own and cannot name another mod's types, so the
/// functions are published in the process's own data slot, "kf3.text", as a
/// dictionary of plain delegates. A mod that uses them lists "kf3.text" under
/// "dependencies" in its mod.json and carries TextApi.cs, which looks them up:
///
///   "Version"      int
///   "Ready"        Func&lt;bool&gt;                       the fonts are loaded
///   "MenuReady"    Func&lt;bool&gt;                       lower case is in the label font
///   "MenuIndex"    Func&lt;char, int&gt;                  label font cell, -1 none
///   "MenuChar"     Func&lt;int, char&gt;                  the character a cell shows, '\0' none
///   "MenuEncode"   Func&lt;string, int, byte[]&gt;        cells (at most n) then 0xFF
///   "SetPicture"   Action&lt;string, int, int, string, string&gt;  owner, archive, entry, style, text
///   "ClearPictures" Action&lt;string&gt;                  owner
///   "NextPicture"  Action&lt;string, string&gt;           style, text: the next picture shown
///   "Lines"        Func&lt;string, string, int&gt;        style, text: lines it wraps to
///   "SetString"    Action&lt;string, string?&gt;          a shared string table: key, value (null removes)
///   "GetString"    Func&lt;string, string?&gt;
///   "MenuPlace"    Func&lt;string, (int[] Cells, int[] X, int Width)&gt;   a string's label
///                  cells, each one's offset from the first, and the width
///   "MenuShown"    Func&lt;string, string&gt;            a label in the game's capitals as drawn
///                  (normal case on: LONG SWORD is drawn Long Sword)
///   "SetCaption"   Action&lt;string, int, string&gt;     owner, slot (0-35), text: a caption shown in
///                  play ("Level Up", "Locked"...), Captions.cs
///   "ClearCaptions" Action&lt;string&gt;                 owner
///   "Coverage"     Func&lt;string, string, int, (byte[] Ink, int W, int H)&gt;   style, text,
///                  width: the text wrapped and drawn as ink coverage, a byte a pixel
///                  (the Japanese Disc mod letters the movies with it)
///
/// Archives: 6 ITEM.T, 7 TALK.T, 8 STALK.T (the order of CD/COM, as GAME.EXE numbers them).
/// </summary>
public sealed class TextMod : IMod
{
    public const string Slot = "kf3.text";
    const uint UploadTim = 0x80043B38;     // func_80043B38(buf): OpenTIM/ReadTIM/LoadImage of a message picture
    const uint PlayerTick = 0x80030FCC;    // stage 4, the player's tick, once a world tick in play

    const string PropPicturesKey = "kf3.text.proportional.pictures";
    const string GamePicturesKey = "kf3.text.game.pictures", GameCaseKey = "kf3.text.game.case";
    const string PropMenusKey = "kf3.text.proportional.menus";
    static readonly Dictionary<string, string> _strings = new();
    static string _note = "";

    public void OnLoad()
    {
        var api = new Dictionary<string, object>
        {
            ["Version"] = 1,
            ["Ready"] = new Func<bool>(Fonts.Ready),
            ["MenuReady"] = new Func<bool>(() => MenuFont.Ready),
            ["MenuIndex"] = new Func<char, int>(MenuFont.Index),
            ["MenuChar"] = new Func<int, char>(MenuFont.Char),
            ["MenuEncode"] = new Func<string, int, byte[]>(MenuFont.Encode),
            ["SetPicture"] = new Action<string, int, int, string, string>(Pictures.Set),
            ["ClearPictures"] = new Action<string>(Pictures.Clear),
            ["NextPicture"] = new Action<string, string>(Pictures.Next),
            ["Lines"] = new Func<string, string, int>(Pictures.Lines),
            ["SetString"] = new Action<string, string?>((k, v) => { if (v == null) _strings.Remove(k); else _strings[k] = v; }),
            ["GetString"] = new Func<string, string?>(k => _strings.TryGetValue(k, out var v) ? v : null),
            ["MenuPlace"] = new Func<string, (int[] Cells, int[] X, int Width)>(MenuText.PlaceText),
            ["MenuShown"] = new Func<string, string>(GameText.Shown),
            ["SetCaption"] = new Action<string, int, string>(Captions.Set),
            ["ClearCaptions"] = new Action<string>(Captions.Clear),
            ["Coverage"] = new Func<string, string, int, (byte[] Ink, int W, int H)>(Pictures.Coverage),
        };
        var view = RecompOne.Runtime.Runtime.View;
        Fonts.PropPictures = view.GetBool(PropPicturesKey, true);
        Fonts.PropMenus = view.GetBool(PropMenusKey, true);
        GameText.NormalCase = view.GetBool(GameCaseKey, true);
        GameText.SetPictures(view.GetBool(GamePicturesKey, true));
        AppDomain.CurrentDomain.SetData(Slot, api);
        Console.WriteLine("[text] loaded");
    }

    public void OnUnload() => AppDomain.CurrentDomain.SetData(Slot, null);

    [PreHook("game", Address = UploadTim)]
    static bool BeforeUpload(CpuContext c, IMemory m)
    {
        try { Pictures.BeforeUpload(c, m); }
        catch (Exception e) { Console.WriteLine($"[text] picture: {e.Message}"); }
        return true;
    }

    [PostHook("game", Address = UploadTim)]
    static void AfterUpload(CpuContext c, IMemory m) => Pictures.AfterUpload(c, m);

    [PreHook("game", Address = MenuFont.LoadImage)]
    static bool BeforeLoadImage(CpuContext c, IMemory m)
    {
        try { MenuFont.BeforeLoadImage(c, m); }
        catch (Exception e) { Console.WriteLine($"[text] font: {e.Message}"); }
        try { Captions.BeforeLoadImage(c, m); }
        catch (Exception e) { Console.WriteLine($"[text] captions: {e.Message}"); }
        return true;
    }

    [Replace("game", Address = MenuText.DrawLabel)]
    static void DrawLabel(Action<CpuContext, IMemory> orig, CpuContext c, IMemory m) => MenuText.Draw(orig, c, m);

    [PostHook("game", Address = PlayerTick)]
    static void AfterPlayerTick(CpuContext c, IMemory m)
    {
        try { MenuFont.UploadOnce(c, m); }
        catch (Exception e) { Console.WriteLine($"[text] font: {e.Message}"); }
        try { Captions.Tick(m); Captions.UploadOnce(c, m); }
        catch (Exception e) { Console.WriteLine($"[text] captions: {e.Message}"); }
    }

    static void Persist(string key, bool value)
    {
        RecompOne.Runtime.Runtime.View.SetBool(key, value);
        RecompOne.Runtime.Runtime.SaveView();
    }

    public void DrawSettings()
    {
        ImGui.TextWrapped("New fonts for all of the game's text, with full upper and lower case and proportional spacing. " +
                          "Other mods (Retranslation, Japanese Disc) draw their own text with it.");
        ImGui.TextDisabled($"Fonts: {Fonts.Status}");
        ImGui.TextDisabled(MenuFont.Ready ? "Menu font: lower case in place" : "Menu font: lower case not up yet");
        ImGui.TextDisabled($"Pictures: {Pictures.BaseCount} of the game's own, {Pictures.Count} set by mods, {Pictures.Shown} shown this session");
        ImGui.TextDisabled($"Captions: {Captions.Count} set by mods ");
        ImGui.Separator();
        bool gamePictures = GameText.ShowPictures;
        if (ImGui.Checkbox("The game's own signs, dialogue, descriptions and messages in these fonts", ref gamePictures))
        {
            GameText.SetPictures(gamePictures);
            Persist(GamePicturesKey, gamePictures);
        }
        ImGui.TextDisabled("  the US text, where no other mod gives its own; for pictures shown from now on");
        if (ImGui.Checkbox("Menus and names in normal capitalisation (Use Item, Long Sword)", ref GameText.NormalCase))
            Persist(GameCaseKey, GameText.NormalCase);
        ImGui.Separator();
        if (ImGui.Checkbox("Proportional spacing and kerning in signs, dialogue and descriptions", ref Fonts.PropPictures))
            Persist(PropPicturesKey, Fonts.PropPictures);
        ImGui.TextDisabled("  for pictures shown from now on");
        if (ImGui.Checkbox("Proportional spacing and kerning in menus", ref Fonts.PropMenus))
            Persist(PropMenusKey, Fonts.PropMenus);
        ImGui.TextDisabled("  the numbers the game draws itself stay in fixed columns");
        ImGui.Separator();
        ImGui.TextWrapped($"Character maps: {Path.GetFullPath(Fonts.Folder)}");
        ImGui.TextWrapped("dialogue.png, sign.png and menu.png hold every letter; edit one and restart the game to use it. " +
                          "The *_template.png files are the same grids, empty, for drawing a new font. " +
                          "The *_spacing.json files hold each font's spacing and kerning.");
        if (ImGui.Button("Rebuild the character maps from the disc"))
        {
            try { Fonts.Rebuild(); _note = "Rebuilt from the disc. The mod's own fonts and any edits were replaced; reinstall the mod to get its fonts back."; }
            catch (Exception e) { _note = "Could not rebuild: " + e.Message; }
        }
        ImGui.TextDisabled("Replaces the mod's fonts with letters cut from your disc.");
        if (_note.Length > 0) ImGui.TextDisabled(_note);
    }
}
