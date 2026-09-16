using FloppyHub.App.Skin;
using Godot;

namespace FloppyHub.App.Art;

/// <summary>
/// Platzhalter-Icons, per Code als Pixel-Art gezeichnet (damit garantiert lizenzfrei).
/// Mael ersetzt sie spaeter durch eigene Grafiken: einfach eine PNG mit demselben
/// Namen nach res://assets/icons/ legen - die hat dann Vorrang.
/// </summary>
public static class IconForge
{
    /// <param name="Size">Grundgroesse in Pixeln (logisch).</param>
    /// <param name="Glyph">true = haengt vom Farbschema ab, wird nie als PNG exportiert.</param>
    public sealed record IconDef(int Size, bool Glyph, Action<PixelCanvas, Palette> Draw);

    private static Color C(string hex) => new(hex);
    private static Color A(string hex, float alpha) => new Color(hex) with { A = alpha };

    private static readonly Color Ink = C("#1b2130");
    private static readonly Color Shade = new(0, 0, 0, 0.26f);

    public static IReadOnlyDictionary<string, IconDef> All { get; } = new Dictionary<string, IconDef>
    {
        // ---- Werkzeugleiste (32) ----
        ["floppy"] = new(32, false, (c, _) => Floppy(c, star: false)),
        ["app"] = new(32, false, (c, _) => Floppy(c, star: true)),
        ["library"] = new(32, false, (c, _) => Library(c)),
        ["write"] = new(32, false, (c, _) => Write(c)),
        ["drives"] = new(32, false, (c, _) => Drives(c)),
        ["chat"] = new(32, false, (c, _) => Chat(c)),
        ["game"] = new(32, false, (c, _) => Joystick(c)),
        ["log"] = new(32, false, (c, _) => LogPage(c)),
        ["settings"] = new(32, false, (c, _) => Gear(c)),
        ["help"] = new(32, false, (c, _) => Help(c)),

        // ---- Datentraeger (32, gross dargestellt) ----
        ["media_floppy"] = new(32, false, (c, _) => Floppy(c, star: false)),
        ["media_usb"] = new(32, false, (c, _) => Usb(c)),
        ["media_cd"] = new(32, false, (c, _) => Cd(c)),
        ["media_empty"] = new(32, false, (c, _) => EmptyDrive(c)),

        // ---- Listen (16) ----
        ["kind_steam"] = new(16, false, (c, _) => Play(c)),
        ["kind_pcrun"] = new(16, false, (c, _) => Monitor(c)),
        ["kind_run"] = new(16, false, (c, _) => MiniFloppy(c)),
        ["floppy_small"] = new(16, false, (c, _) => MiniFloppy(c)),
        ["library_small"] = new(16, false, (c, _) => Books16(c)),
        ["kind_hub"] = new(16, false, (c, _) => Star(c)),
        ["exe"] = new(16, false, (c, _) => Window16(c)),
        ["file"] = new(16, false, (c, _) => File16(c)),
        ["folder"] = new(16, false, (c, _) => Folder16(c)),
        ["trust"] = new(16, false, (c, _) => Shield(c)),
        ["warn"] = new(16, false, (c, _) => Warn(c)),
        ["error"] = new(16, false, (c, _) => ErrorIcon(c)),
        ["info"] = new(16, false, (c, _) => Info(c)),
        ["ok"] = new(16, false, (c, _) => OkIcon(c)),
        ["led_on"] = new(16, false, (c, _) => Led(c, C("#3bd14f"))),
        ["led_off"] = new(16, false, (c, _) => Led(c, C("#8d939b"))),
        ["led_warn"] = new(16, false, (c, _) => Led(c, C("#f0b429"))),
        ["refresh"] = new(16, false, (c, _) => Refresh(c)),
        ["start"] = new(16, false, (c, _) => Start(c)),
        ["add"] = new(16, false, (c, _) => Plus(c)),
        ["remove"] = new(16, false, (c, _) => Minus(c)),

        // ---- Bedienelemente (haengen vom Farbschema ab) ----
        ["arrow_down"] = new(10, true, (c, p) => c.Polygon(p.Text, new(1, 3), new(9, 3), new(5, 8))),
        ["arrow_right"] = new(10, true, (c, p) => c.Polygon(p.Text, new(3, 1), new(8, 5), new(3, 9))),
        ["check_off"] = new(13, true, (c, p) => CheckBox(c, p, false)),
        ["check_on"] = new(13, true, (c, p) => CheckBox(c, p, true)),
        ["radio_off"] = new(13, true, (c, p) => Radio(c, p, false)),
        ["radio_on"] = new(13, true, (c, p) => Radio(c, p, true)),
    };

    public static Image Draw(string name, Palette palette)
    {
        if (!All.TryGetValue(name, out var def))
        {
            var missing = new PixelCanvas(16, 16);
            missing.Rect(0, 0, 16, 16, C("#ff00ff"));   // auffaellig: Icon fehlt
            return missing.Image;
        }
        var canvas = new PixelCanvas(def.Size, def.Size);
        def.Draw(canvas, palette);
        return canvas.Image;
    }

    /// <summary>Alle Nicht-Glyph-Icons als PNG speichern (Vorlage zum Uebermalen).</summary>
    public static int ExportPngs(string directory)
    {
        System.IO.Directory.CreateDirectory(directory);
        var n = 0;
        foreach (var (name, def) in All)
        {
            if (def.Glyph) continue;
            Draw(name, Palette.Classic).SavePng(System.IO.Path.Combine(directory, name + ".png"));
            n++;
        }
        return n;
    }

    // ==================================================================
    // 32 px
    // ==================================================================

    private static void Floppy(PixelCanvas c, bool star)
    {
        var body = C("#3f69b8");
        c.Rect(4, 3, 24, 26, body);
        c.HLine(4, 3, 22, C("#7099de"));
        c.VLine(4, 3, 26, C("#7099de"));
        c.HLine(5, 28, 23, C("#2a4a88"));
        c.VLine(27, 5, 24, C("#2a4a88"));
        c.Clear(27, 3); c.Clear(26, 3); c.Clear(27, 4);   // abgeschraegte Ecke

        // Metall-Schieber
        c.Rect(10, 3, 13, 11, C("#c9ced6"));
        c.Bevel(10, 3, 13, 11, C("#f1f3f6"), C("#878d97"));
        c.Rect(18, 5, 3, 7, C("#343942"));
        c.VLine(18, 5, 7, C("#4f5663"));

        // Etikett
        c.Rect(7, 17, 18, 11, C("#f4f0e3"));
        c.Bevel(7, 17, 18, 11, C("#ffffff"), C("#c9c1aa"));
        if (star)
        {
            StarShape(c, 16, 22.5f, 4.6f, 2.1f, C("#f2c230"));
            c.HLine(9, 19, 3, C("#d9443a"));
            c.HLine(20, 19, 3, C("#d9443a"));
        }
        else
        {
            c.HLine(9, 19, 14, C("#d9443a"));
            c.HLine(9, 22, 14, C("#9aa5ba"));
            c.HLine(9, 24, 10, C("#9aa5ba"));
        }

        c.Rect(5, 25, 2, 2, C("#1d2e57"));   // Schreibschutz
        c.Outline(C("#161e33"));
        c.DropShadow(Shade);
    }

    private static void Library(PixelCanvas c)
    {
        void Book(int x, int y, int w, int h, string color, string band)
        {
            c.Rect(x, y, w, h, C(color));
            c.VLine(x, y, h, C(color).Lightened(0.25f));
            c.VLine(x + w - 1, y, h, C(color).Darkened(0.25f));
            c.HLine(x, y + 3, w, C(band));
            c.HLine(x, y + h - 4, w, C(band));
        }

        Book(4, 8, 5, 18, "#c0392b", "#f0c64a");
        Book(9, 5, 6, 21, "#2e8b57", "#e9e2c6");
        Book(15, 9, 5, 17, "#3f69b8", "#f0c64a");
        c.Polygon(C("#e0a526"), new(21, 11), new(26, 10), new(29, 26), new(24, 27));
        c.Line(24, 11, 27, 26, C("#b47f12"));
        c.Rect(2, 26, 28, 3, C("#8a5a2b"));
        c.HLine(2, 26, 28, C("#b98149"));
        c.Outline(C("#2a1d12"));
        c.DropShadow(Shade);
    }

    private static void Write(PixelCanvas c)
    {
        // kleine Diskette
        c.Rect(2, 11, 19, 19, C("#3f69b8"));
        c.HLine(2, 11, 18, C("#7099de"));
        c.VLine(2, 11, 19, C("#7099de"));
        c.Rect(7, 11, 9, 7, C("#c9ced6"));
        c.Rect(12, 12, 2, 5, C("#343942"));
        c.Rect(5, 21, 13, 8, C("#f4f0e3"));
        c.HLine(7, 23, 9, C("#d9443a"));
        c.HLine(7, 25, 9, C("#9aa5ba"));

        // Stift
        c.Polygon(C("#f2c230"), new(25, 2), new(30, 7), new(18, 19), new(13, 14));
        c.Line(26, 4, 17, 13, C("#fbe08a"));
        c.Polygon(C("#b9bec6"), new(23, 4), new(28, 9), new(26, 11), new(21, 6));
        c.Polygon(C("#e98aa3"), new(25, 2), new(30, 7), new(28, 9), new(23, 4));
        c.Polygon(C("#e8c89a"), new(13, 14), new(18, 19), new(11, 22));
        c.Px(11, 21, C("#2b2b2b")); c.Px(12, 21, C("#2b2b2b")); c.Px(11, 20, C("#2b2b2b"));
        c.Outline(Ink);
        c.DropShadow(Shade);
    }

    private static void Drives(PixelCanvas c)
    {
        c.VGradient(2, 9, 28, 16, C("#eceef1"), C("#a9aeb6"));
        c.Bevel(2, 9, 28, 16, C("#ffffff"), C("#7c828b"));
        c.Rect(6, 13, 16, 3, C("#2c2f35"));
        c.HLine(6, 16, 16, C("#f8f9fb"));
        c.Rect(24, 12, 4, 3, C("#c7cbd1"));
        c.Bevel(24, 12, 4, 3, C("#ffffff"), C("#8a9099"));
        c.Rect(24, 20, 3, 2, C("#3bd14f"));
        c.Px(24, 20, C("#b8ffc2"));
        c.HLine(6, 20, 12, C("#c4c8ce"));
        c.Outline(C("#2b2e33"));
        c.DropShadow(Shade);
    }

    private static void Chat(PixelCanvas c)
    {
        // hintere Sprechblase
        c.Rect(12, 3, 17, 13, C("#ffe07a"));
        c.HLine(12, 3, 17, C("#fff1b8"));
        c.Polygon(C("#ffe07a"), new(22, 16), new(27, 16), new(28, 21));
        c.Clear(12, 3); c.Clear(28, 3);
        c.Outline(C("#6b5410"));

        // vordere Sprechblase mit eigenem Rand
        c.Rect(2, 9, 21, 16, C("#24324d"));
        c.Rect(3, 10, 19, 14, C("#ffffff"));
        c.HLine(3, 23, 19, C("#d6deeb"));
        c.Polygon(C("#24324d"), new(5, 24), new(11, 24), new(4, 30));
        c.Polygon(C("#ffffff"), new(6, 23), new(10, 23), new(5, 28));
        c.Clear(2, 9); c.Clear(22, 9); c.Clear(2, 24); c.Clear(22, 24);
        c.Rect(7, 16, 2, 2, C("#3f69b8"));
        c.Rect(11, 16, 2, 2, C("#3f69b8"));
        c.Rect(15, 16, 2, 2, C("#3f69b8"));
        c.DropShadow(Shade);
    }

    private static void Joystick(PixelCanvas c)
    {
        c.Polygon(C("#353840"), new(5, 21), new(27, 21), new(30, 28), new(2, 28));
        c.HLine(5, 21, 22, C("#5a5f69"));
        c.Rect(21, 18, 5, 3, C("#d63a2f"));
        c.HLine(21, 18, 5, C("#ff7b6e"));
        c.Rect(15, 9, 3, 13, C("#9aa1ab"));
        c.VLine(15, 9, 13, C("#d3d8df"));
        c.Rect(13, 20, 7, 2, C("#22252b"));
        c.Disc(16.5f, 8f, 5.2f, C("#d63a2f"));
        c.Disc(15f, 6.5f, 1.8f, C("#ff9b8f"));
        c.Outline(C("#1a1b20"));
        c.DropShadow(Shade);
    }

    private static void LogPage(PixelCanvas c)
    {
        c.Polygon(C("#fbfbf7"), new(6, 3), new(21, 3), new(27, 9), new(27, 30), new(6, 30));
        c.Polygon(C("#d6d3c8"), new(21, 3), new(27, 9), new(21, 9));
        c.VLine(21, 3, 6, C("#a9a597"));
        c.HLine(21, 9, 6, C("#a9a597"));
        c.HLine(9, 12, 14, C("#3a9d45"));
        c.HLine(9, 15, 11, C("#3f69b8"));
        c.HLine(9, 18, 15, C("#c98208"));
        c.HLine(9, 21, 9, C("#3f69b8"));
        c.HLine(9, 24, 13, C("#c0392b"));
        c.HLine(9, 27, 8, C("#8c8c8c"));
        c.Outline(C("#3b3a36"));
        c.DropShadow(Shade);
    }

    private static void Gear(PixelCanvas c)
    {
        var metal = C("#9ea5af");
        for (var i = 0; i < 8; i++)
        {
            var a = i * MathF.PI / 4f;
            var x = (int)MathF.Round(16 + 11.5f * MathF.Cos(a));
            var y = (int)MathF.Round(16 + 11.5f * MathF.Sin(a));
            c.Rect(x - 2, y - 2, 5, 5, metal);
        }
        c.Disc(16, 16, 10.5f, metal);
        c.Disc(14.5f, 14.5f, 8f, C("#bcc2cb"));
        c.Disc(16, 16, 6.5f, C("#aab1bb"));
        c.Ring(16, 16, 6.5f, 1.5f, C("#6f7682"));
        c.Punch(16, 16, 4f);
        c.Outline(C("#2f3238"));
        c.DropShadow(Shade);
    }

    private static void Help(PixelCanvas c)
    {
        c.Disc(16, 16, 13, C("#3f69b8"));
        c.Disc(12.5f, 11.5f, 6, A("#ffffff", 0.18f));
        var w = C("#ffffff");
        c.Rect(12, 8, 8, 2, w);
        c.Rect(11, 9, 2, 3, w);
        c.Rect(19, 9, 2, 5, w);
        c.Rect(16, 14, 3, 2, w);
        c.Rect(15, 15, 2, 4, w);
        c.Rect(15, 21, 2, 2, w);
        c.Outline(C("#15254a"));
        c.DropShadow(Shade);
    }

    private static void Usb(PixelCanvas c)
    {
        c.Rect(3, 11, 19, 11, C("#2d2f36"));
        c.HLine(3, 11, 19, C("#50545e"));
        c.Rect(6, 13, 11, 7, C("#3f69b8"));
        c.HLine(6, 13, 11, C("#7099de"));
        c.Px(19, 14, C("#3bd14f"));
        c.Rect(22, 12, 8, 9, C("#c9ced6"));
        c.Bevel(22, 12, 8, 9, C("#f1f3f6"), C("#878d97"));
        c.Rect(25, 14, 2, 2, C("#2d2f36"));
        c.Rect(25, 17, 2, 2, C("#2d2f36"));
        c.Rect(1, 15, 2, 3, C("#2d2f36"));
        c.Outline(C("#17181c"));
        c.DropShadow(Shade);
    }

    private static void Cd(PixelCanvas c)
    {
        c.Disc(16, 16, 13, C("#cdd3dc"));
        c.Ring(16, 16, 12, 2, A("#8fd3ff", 0.6f));
        c.Ring(16, 16, 9.5f, 2, A("#ffd27a", 0.55f));
        c.Ring(16, 16, 7, 1.5f, A("#ff9ad5", 0.5f));
        c.Line(8, 9, 12, 13, A("#ffffff", 0.9f));
        c.Line(9, 8, 13, 12, A("#ffffff", 0.6f));
        c.Disc(16, 16, 4.5f, C("#a0a8b5"));
        c.Punch(16, 16, 2.2f);
        c.Outline(C("#3a3f4a"));
        c.DropShadow(Shade);
    }

    private static void EmptyDrive(PixelCanvas c)
    {
        c.VGradient(3, 10, 26, 14, C("#e3e5e8"), C("#b3b8bf"));
        c.Bevel(3, 10, 26, 14, C("#ffffff"), C("#7c828b"));
        c.Rect(7, 15, 18, 3, C("#2c2f35"));
        c.Rect(23, 20, 3, 2, C("#8d939b"));
        c.Outline(C("#2b2e33"));
        c.DropShadow(Shade);
    }

    // ==================================================================
    // 16 px
    // ==================================================================

    private static void Play(PixelCanvas c)
    {
        c.Disc(8, 8, 7.5f, C("#2f5fb3"));
        c.Disc(6.5f, 6f, 3.5f, A("#ffffff", 0.22f));
        c.Polygon(C("#ffffff"), new(6, 4), new(12, 8), new(6, 12));
    }

    private static void Monitor(PixelCanvas c)
    {
        c.Rect(1, 1, 14, 11, C("#c9ced6"));
        c.Bevel(1, 1, 14, 11, C("#f1f3f6"), C("#7d838c"));
        c.VGradient(3, 3, 10, 7, C("#5b8bd9"), C("#2f5fb3"));
        c.Rect(6, 12, 4, 1, C("#8b919b"));
        c.Rect(4, 13, 8, 2, C("#b0b6bf"));

    }

    private static void MiniFloppy(PixelCanvas c)
    {
        c.Rect(2, 1, 12, 14, C("#3f69b8"));
        c.VLine(2, 1, 14, C("#7099de"));
        c.Rect(5, 1, 6, 5, C("#c9ced6"));
        c.Rect(8, 2, 2, 3, C("#343942"));
        c.Rect(4, 8, 8, 6, C("#f4f0e3"));
        c.HLine(5, 10, 6, C("#d9443a"));
        c.Clear(13, 1);
        c.Outline(C("#161e33"));
    }

    private static void Books16(PixelCanvas c)
    {
        c.Rect(2, 3, 3, 11, C("#c0392b"));
        c.Rect(5, 1, 4, 13, C("#2e8b57"));
        c.Rect(9, 4, 3, 10, C("#3f69b8"));
        c.HLine(5, 3, 4, C("#e9e2c6"));
        c.Polygon(C("#e0a526"), new(12, 5), new(14, 5), new(15, 14), new(13, 14));
        c.Rect(1, 14, 14, 1, C("#8a5a2b"));
        c.Outline(C("#2a1d12"));
    }

    private static void Star(PixelCanvas c)
    {
        StarShape(c, 8, 8.5f, 7.2f, 3.1f, C("#f2c230"));
        c.Px(6, 6, C("#fff0a8"));
        c.Outline(C("#7a5a10"));
    }

    private static void StarShape(PixelCanvas c, float cx, float cy, float outer, float inner, Color color)
    {
        var pts = new Vector2I[10];
        for (var i = 0; i < 10; i++)
        {
            var r = i % 2 == 0 ? outer : inner;
            var a = -MathF.PI / 2 + i * MathF.PI / 5;
            pts[i] = new Vector2I((int)MathF.Round(cx + r * MathF.Cos(a)), (int)MathF.Round(cy + r * MathF.Sin(a)));
        }
        c.Polygon(color, pts);
    }

    private static void Window16(PixelCanvas c)
    {
        c.Rect(1, 2, 14, 12, C("#ffffff"));
        c.Rect(1, 2, 14, 3, C("#2f5fb3"));
        c.Px(12, 3, C("#ffffff"));
        c.HLine(3, 7, 7, C("#9aa5ba"));
        c.HLine(3, 9, 9, C("#9aa5ba"));
        c.Outline(C("#2f3238"));
    }

    private static void File16(PixelCanvas c)
    {
        c.Polygon(C("#fbfbf7"), new(3, 1), new(10, 1), new(13, 4), new(13, 15), new(3, 15));
        c.Polygon(C("#d6d3c8"), new(10, 1), new(13, 4), new(10, 4));
        c.HLine(5, 7, 6, C("#9aa5ba"));
        c.HLine(5, 9, 6, C("#9aa5ba"));
        c.HLine(5, 11, 4, C("#9aa5ba"));
        c.Outline(C("#5a5850"));
    }

    private static void Folder16(PixelCanvas c)
    {
        c.Rect(1, 3, 6, 2, C("#e2ad3f"));
        c.VGradient(1, 5, 14, 9, C("#f8d77a"), C("#e7b447"));
        c.HLine(1, 5, 14, C("#fff0b3"));
        c.Outline(C("#7a5a10"));
    }

    private static void Shield(PixelCanvas c)
    {
        c.Polygon(C("#3aa14a"), new(8, 1), new(14, 3), new(13, 10), new(8, 15), new(3, 10), new(2, 3));
        c.Polygon(A("#ffffff", 0.2f), new(8, 1), new(8, 15), new(3, 10), new(2, 3));
        var w = C("#ffffff");
        c.Line(5, 8, 7, 10, w);
        c.Line(7, 10, 11, 5, w);
        c.Outline(C("#1d5a27"));
    }

    private static void Warn(PixelCanvas c)
    {
        c.Polygon(C("#f2c230"), new(8, 0), new(16, 14), new(0, 14));
        c.VLine(7, 5, 5, C("#2b2b2b"));
        c.VLine(8, 5, 5, C("#2b2b2b"));
        c.Rect(7, 11, 2, 2, C("#2b2b2b"));
        c.Outline(C("#7a5a10"));
    }

    private static void ErrorIcon(PixelCanvas c)
    {
        c.Disc(8, 8, 7.5f, C("#d64533"));
        var w = C("#ffffff");
        c.Line(5, 5, 10, 10, w); c.Line(6, 5, 11, 10, w);
        c.Line(10, 5, 5, 10, w); c.Line(11, 5, 6, 10, w);
    }

    private static void Info(PixelCanvas c)
    {
        c.Disc(8, 8, 7.5f, C("#2f5fb3"));
        var w = C("#ffffff");
        c.Rect(7, 3, 2, 2, w);
        c.Rect(7, 6, 2, 6, w);
        c.HLine(6, 12, 4, w);
    }

    private static void OkIcon(PixelCanvas c)
    {
        c.Disc(8, 8, 7.5f, C("#3aa14a"));
        var w = C("#ffffff");
        c.Line(4, 8, 7, 11, w); c.Line(4, 7, 7, 10, w);
        c.Line(7, 11, 12, 5, w); c.Line(7, 10, 12, 4, w);
    }

    private static void Led(PixelCanvas c, Color color)
    {
        c.Disc(8, 8, 5.5f, color.Darkened(0.35f));
        c.Disc(8, 8, 4.5f, color);
        c.Disc(6.5f, 6.5f, 1.5f, color.Lightened(0.6f));
        c.Ring(8, 8, 6.5f, 1f, A("#000000", 0.35f));
    }

    private static void Refresh(PixelCanvas c)
    {
        var g = C("#2e9e3e");
        c.Ring(8, 8.5f, 6.5f, 2.2f, g);
        for (var y = 0; y < 9; y++)
        for (var x = 8; x < 16; x++)
            if (x - 8 > y) c.Clear(x, y);
        c.Polygon(g, new(9, 0), new(15, 4), new(9, 8));
        c.Outline(C("#15521f"));
    }

    private static void Start(PixelCanvas c)
    {
        c.Polygon(C("#3bb44a"), new(3, 1), new(14, 8), new(3, 15));
        c.Polygon(A("#ffffff", 0.3f), new(3, 1), new(14, 8), new(3, 8));
        c.Outline(C("#1d5a27"));
    }

    private static void Plus(PixelCanvas c)
    {
        c.Rect(6, 2, 4, 12, C("#3bb44a"));
        c.Rect(2, 6, 12, 4, C("#3bb44a"));
        c.Outline(C("#1d5a27"));
    }

    private static void Minus(PixelCanvas c)
    {
        c.Rect(2, 6, 12, 4, C("#d64533"));
        c.Outline(C("#6e1c12"));
    }

    // ==================================================================
    // Glyphen (Farbschema)
    // ==================================================================

    private static void CheckBox(PixelCanvas c, Palette p, bool on)
    {
        c.Rect(0, 0, 13, 13, p.Field);
        c.HLine(0, 0, 12, p.Shadow); c.VLine(0, 0, 12, p.Shadow);
        c.HLine(0, 12, 13, p.Highlight); c.VLine(12, 0, 13, p.Highlight);
        c.HLine(1, 1, 10, p.DarkShadow); c.VLine(1, 1, 10, p.DarkShadow);
        c.HLine(1, 11, 11, p.Light); c.VLine(11, 1, 11, p.Light);
        if (!on) return;
        for (var i = 0; i < 3; i++)
        {
            c.Line(3, 5 + i, 5, 7 + i, p.Text);
            c.Line(5, 7 + i, 9, 3 + i, p.Text);
        }
    }

    private static void Radio(PixelCanvas c, Palette p, bool on)
    {
        c.Disc(6.5f, 6.5f, 6.5f, p.Highlight);
        c.Disc(6.5f, 6.5f, 5.5f, p.Shadow);
        c.Disc(7f, 7f, 5f, p.Light);
        c.Disc(6.5f, 6.5f, 4.5f, p.Field);
        if (on) c.Disc(6.5f, 6.5f, 2.2f, p.Text);
    }
}
