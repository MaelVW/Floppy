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
        ["media_dvd"] = new(32, false, (c, _) => Dvd(c)),
        ["media_bluray"] = new(32, false, (c, _) => BluRay(c)),
        ["media_audio"] = new(32, false, (c, _) => AudioCd(c)),
        ["media_virtual"] = new(32, false, (c, _) => VirtualDisc(c)),
        ["media_sd"] = new(32, false, (c, _) => SdCard(c)),
        ["media_reader"] = new(32, false, (c, _) => CardReader(c)),
        ["media_hdd"] = new(32, false, (c, _) => ExternalDisk(c)),

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

        // ---- Chat (16) ----
        ["key"] = new(16, false, (c, _) => Key16(c)),
        ["lan"] = new(16, false, (c, _) => Lan16(c)),
        ["trophy"] = new(16, false, (c, _) => Trophy16(c)),
        ["contact"] = new(16, false, (c, _) => Contact16(c)),
        ["send"] = new(16, false, (c, _) => Send16(c)),
        ["pencil"] = new(16, false, (c, _) => Pencil16(c)),
        ["cloud"] = new(16, false, (c, _) => Cloud16(c)),
        ["door"] = new(16, false, (c, _) => Door16(c)),
        ["chess"] = new(32, true, (c, p) => ChessTool(c, p)),

        // ---- Schachfiguren (32, eine Silhouette pro Figur, zwei Farben) ----
        ["chess_wk"] = new(32, false, (c, _) => ChessKing(c, ChessWhite)),
        ["chess_wq"] = new(32, false, (c, _) => ChessQueen(c, ChessWhite)),
        ["chess_wr"] = new(32, false, (c, _) => ChessRook(c, ChessWhite)),
        ["chess_wb"] = new(32, false, (c, _) => ChessBishop(c, ChessWhite)),
        ["chess_wn"] = new(32, false, (c, _) => ChessKnight(c, ChessWhite)),
        ["chess_wp"] = new(32, false, (c, _) => ChessPawn(c, ChessWhite)),
        ["chess_bk"] = new(32, false, (c, _) => ChessKing(c, ChessBlack)),
        ["chess_bq"] = new(32, false, (c, _) => ChessQueen(c, ChessBlack)),
        ["chess_br"] = new(32, false, (c, _) => ChessRook(c, ChessBlack)),
        ["chess_bb"] = new(32, false, (c, _) => ChessBishop(c, ChessBlack)),
        ["chess_bn"] = new(32, false, (c, _) => ChessKnight(c, ChessBlack)),
        ["chess_bp"] = new(32, false, (c, _) => ChessPawn(c, ChessBlack)),

        // ---- Minispiel-Kacheln (16) ----
        ["tile_floor"] = new(16, false, (c, _) => TileFloor(c)),
        ["tile_wall"] = new(16, false, (c, _) => TileWall(c)),
        ["tile_goal"] = new(16, false, (c, _) => TileGoal(c)),
        ["tile_box"] = new(16, false, (c, _) => TileBox(c, done: false)),
        ["tile_box_goal"] = new(16, false, (c, _) => TileBox(c, done: true)),
        ["tile_player"] = new(16, false, (c, _) => TilePlayer(c)),

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

    /// <summary>
    /// Fehlende Nicht-Glyph-Icons als PNG speichern (Vorlage zum Uebermalen).
    /// Vorhandene Dateien werden NIE ueberschrieben - das koennten Maels eigene Grafiken sein.
    /// </summary>
    public static int ExportPngs(string directory)
    {
        System.IO.Directory.CreateDirectory(directory);
        var n = 0;
        foreach (var (name, def) in All)
        {
            var file = System.IO.Path.Combine(directory, name + ".png");
            if (def.Glyph || System.IO.File.Exists(file)) continue;
            Draw(name, Palette.Classic).SavePng(file);
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

    private static void Cd(PixelCanvas c) =>
        Disc(c, "#cdd3dc", A("#8fd3ff", 0.6f), A("#ffd27a", 0.55f), A("#ff9ad5", 0.5f));

    private static void Dvd(PixelCanvas c) =>
        Disc(c, "#d6d0e4", A("#b98cff", 0.6f), A("#ffc86b", 0.5f), A("#e7a0ff", 0.45f));

    private static void BluRay(PixelCanvas c) =>
        Disc(c, "#c9d8ee", A("#3f8cff", 0.65f), A("#9fd8ff", 0.55f), A("#5fa8ff", 0.45f));

    private static void Disc(PixelCanvas c, string baseColor, Color outer, Color middle, Color inner, bool finish = true)
    {
        c.Disc(16, 16, 13, C(baseColor));
        c.Ring(16, 16, 12, 2, outer);
        c.Ring(16, 16, 9.5f, 2, middle);
        c.Ring(16, 16, 7, 1.5f, inner);
        c.Line(8, 9, 12, 13, A("#ffffff", 0.9f));
        c.Line(9, 8, 13, 12, A("#ffffff", 0.6f));
        c.Disc(16, 16, 4.5f, C("#a0a8b5"));
        c.Punch(16, 16, 2.2f);
        if (!finish) return;
        c.Outline(C("#3a3f4a"));
        c.DropShadow(Shade);
    }

    private static void AudioCd(PixelCanvas c)
    {
        Disc(c, "#cdd3dc", A("#8fd3ff", 0.6f), A("#ffd27a", 0.55f), A("#ff9ad5", 0.5f), finish: false);
        var ink = C("#1b1b22");
        c.Disc(20.5f, 25.5f, 2.6f, ink);
        c.Disc(27.5f, 23.5f, 2.6f, ink);
        c.Rect(22, 14, 1, 12, ink);
        c.Rect(29, 12, 1, 12, ink);
        c.Line(22, 14, 29, 12, ink);
        c.Line(22, 15, 29, 13, ink);
        c.Outline(C("#3a3f4a"));
        c.DropShadow(Shade);
    }

    private static void VirtualDisc(PixelCanvas c)
    {
        Disc(c, "#cdd3dc", A("#8fd3ff", 0.6f), A("#ffd27a", 0.55f), A("#ff9ad5", 0.5f), finish: false);
        c.Rect(20, 20, 11, 10, C("#2f5fb3"));
        var w = C("#ffffff");
        c.Line(22, 22, 25, 28, w);
        c.Line(28, 22, 25, 28, w);
        c.Outline(C("#3a3f4a"));
        c.DropShadow(Shade);
    }

    private static void SdCard(PixelCanvas c)
    {
        c.Polygon(C("#30384a"), new(8, 2), new(21, 2), new(26, 7), new(26, 30), new(8, 30));
        c.VLine(8, 2, 28, C("#4a556d"));
        for (var x = 11; x <= 21; x += 2) c.VLine(x, 3, 4, C("#d9b54a"));
        c.Rect(10, 12, 14, 15, C("#e9eef6"));
        c.HLine(10, 12, 14, C("#ffffff"));
        c.Rect(10, 15, 14, 3, C("#d9443a"));
        c.HLine(12, 21, 9, C("#9aa5ba"));
        c.HLine(12, 23, 6, C("#9aa5ba"));
        c.Rect(6, 16, 2, 5, C("#f1f3f6"));   // Schreibschutz-Schieber
        c.Outline(C("#141820"));
        c.DropShadow(Shade);
    }

    private static void ExternalDisk(PixelCanvas c)
    {
        c.VGradient(4, 5, 24, 20, C("#5a606b"), C("#2c3038"));
        c.Bevel(4, 5, 24, 20, C("#7c838f"), C("#1f2228"));
        c.Clear(4, 5); c.Clear(27, 5); c.Clear(4, 24); c.Clear(27, 24);
        c.HLine(8, 9, 16, C("#737a86"));
        c.HLine(8, 11, 10, C("#4a505a"));
        c.Rect(22, 20, 3, 2, C("#4fa8ff"));
        c.Px(22, 20, C("#c9e6ff"));
        // Kabel
        c.Rect(15, 25, 2, 3, C("#2a2d33"));
        c.Line(16, 28, 20, 30, C("#2a2d33"));
        c.Line(20, 30, 26, 30, C("#2a2d33"));
        c.Outline(C("#15171b"));
        c.DropShadow(Shade);
    }

    private static void CardReader(PixelCanvas c)
    {
        c.VGradient(4, 11, 24, 12, C("#e3e5e8"), C("#b3b8bf"));
        c.Bevel(4, 11, 24, 12, C("#ffffff"), C("#7c828b"));
        c.Rect(8, 15, 12, 2, C("#2c2f35"));
        c.Rect(8, 19, 7, 1, C("#2c2f35"));
        c.Rect(23, 18, 2, 2, C("#8d939b"));
        c.Outline(C("#2b2e33"));
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
    // Chat
    // ==================================================================

    private static void Key16(PixelCanvas c)
    {
        var gold = C("#f2c230");
        c.Ring(5, 8, 4.5f, 2.2f, gold);
        c.Rect(8, 7, 7, 2, gold);
        c.Rect(12, 9, 2, 3, gold);
        c.Rect(9, 9, 2, 2, gold);
        c.Px(3, 6, C("#fff0a8"));
        c.Outline(C("#7a5a10"));
    }

    private static void Lan16(PixelCanvas c)
    {
        // zwei Bildschirme, verbunden
        void Screen(int x, int y)
        {
            c.Rect(x, y, 7, 6, C("#c9ced6"));
            c.Rect(x + 1, y + 1, 5, 4, C("#3f69b8"));
            c.Px(x + 1, y + 1, C("#7099de"));
            c.Rect(x + 2, y + 6, 3, 1, C("#8b919b"));
        }
        Screen(0, 1);
        Screen(9, 8);
        c.HLine(3, 11, 6, C("#2e9e3e"));
        c.VLine(3, 8, 4, C("#2e9e3e"));
        c.Outline(C("#1b2130"));
    }

    private static void Trophy16(PixelCanvas c)
    {
        var gold = C("#f2c230");
        c.Rect(4, 1, 8, 6, gold);
        c.Rect(5, 7, 6, 2, gold);
        c.Rect(7, 9, 2, 3, gold);
        c.Rect(4, 12, 8, 3, C("#8a5a2b"));
        c.Ring(3, 4, 2.6f, 1.2f, gold);
        c.Ring(13, 4, 2.6f, 1.2f, gold);
        c.VLine(5, 2, 4, C("#fff0a8"));
        c.Outline(C("#7a5a10"));
    }

    private static void Contact16(PixelCanvas c)
    {
        c.Rect(1, 2, 14, 12, C("#f4f0e3"));
        c.Bevel(1, 2, 14, 12, C("#ffffff"), C("#b8b0a0"));
        c.Disc(5.5f, 6.5f, 2.2f, C("#e0a878"));
        c.Rect(3, 9, 6, 3, C("#3f69b8"));
        c.HLine(10, 5, 4, C("#6b6560"));
        c.HLine(10, 8, 4, C("#9a948c"));
        c.HLine(10, 10, 3, C("#9a948c"));
        c.Outline(C("#4a4438"));
    }

    private static void Send16(PixelCanvas c)
    {
        c.Polygon(C("#5b8bd9"), new(1, 2), new(15, 8), new(1, 14), new(4, 8));
        c.Polygon(C("#2f5fb3"), new(4, 8), new(15, 8), new(1, 14));
        c.Outline(C("#15305f"));
    }

    private static void Pencil16(PixelCanvas c)
    {
        for (var i = 0; i < 9; i++)
        {
            c.Rect(3 + i, 10 - i, 2, 2, C("#f2c230"));
            c.Px(4 + i, 11 - i, C("#c98208"));
        }
        c.Rect(12, 1, 2, 2, C("#e07a8a"));
        c.Rect(2, 12, 2, 2, C("#e8d2a8"));
        c.Px(1, 14, C("#2b2b2b"));
        c.Outline(C("#5a4010"));
    }

    private static void Cloud16(PixelCanvas c)
    {
        var white = C("#f6f8fc");
        c.Disc(5, 9, 3.5f, white);
        c.Disc(9, 7, 4.2f, white);
        c.Disc(12, 10, 3, white);
        c.Rect(4, 9, 9, 4, white);
        c.HLine(4, 12, 9, C("#c9d4e6"));
        c.Outline(C("#3f69b8"));
    }

    private static void Door16(PixelCanvas c)
    {
        c.Rect(3, 1, 9, 14, C("#8a5a2b"));
        c.Rect(4, 2, 7, 12, C("#b07a42"));
        c.Rect(5, 3, 5, 4, C("#9a6634"));
        c.Rect(5, 8, 5, 5, C("#9a6634"));
        c.Px(9, 8, C("#f2c230"));
        c.Polygon(C("#d64533"), new(12, 6), new(15, 8), new(12, 10));
        c.Outline(C("#3c2410"));
    }

    // ==================================================================
    // Schachfiguren (32) - gemeinsamer Sockel, pro Figur eigene Silhouette.
    // Beide Farben werden IDENTISCH gezeichnet (nur body wechselt) und bekommen
    // denselben Umriss/Schatten - damit wirkt Weiss genauso solide wie Schwarz
    // (anders als bei den Unicode-Glyphen davor, die nur bei Schwarz gefuellt waren).
    // ==================================================================

    private static readonly Color ChessWhite = C("#f4f0e3");
    private static readonly Color ChessBlack = C("#33343c");

    private static void ChessBase(PixelCanvas c, Color body)
    {
        c.Polygon(body, new(4, 30), new(28, 30), new(25, 26), new(7, 26));
        c.Rect(11, 21, 10, 6, body);
    }

    private static void ChessKing(PixelCanvas c, Color body)
    {
        ChessBase(c, body);
        c.Rect(9, 15, 14, 7, body);
        c.Rect(14, 4, 4, 11, body);
        c.Rect(11, 7, 10, 3, body);
        c.Outline(Ink);
        c.DropShadow(Shade);
    }

    private static void ChessQueen(PixelCanvas c, Color body)
    {
        ChessBase(c, body);
        c.Rect(9, 15, 14, 7, body);
        for (var i = 0; i < 5; i++)
        {
            var x = 9 + i * 3;   // Breite 2, Abstand 3 -> 1 Pixel Luecke zwischen den Zacken
            c.Rect(x, 9, 2, 7, body);
            c.Disc(x + 1f, 8, 1.4f, body);
        }
        c.Outline(Ink);
        c.DropShadow(Shade);
    }

    private static void ChessRook(PixelCanvas c, Color body)
    {
        ChessBase(c, body);
        c.Rect(9, 10, 14, 12, body);
        c.Rect(9, 6, 4, 5, body);
        c.Rect(14, 6, 4, 5, body);
        c.Rect(19, 6, 4, 5, body);
        c.Outline(Ink);
        c.DropShadow(Shade);
    }

    private static void ChessBishop(PixelCanvas c, Color body)
    {
        ChessBase(c, body);
        c.Polygon(body, new(16, 5), new(22, 13), new(20, 21), new(12, 21), new(10, 13));
        c.Disc(16, 4, 2.1f, body);
        c.Outline(Ink);
        c.DropShadow(Shade);
    }

    private static void ChessKnight(PixelCanvas c, Color body)
    {
        ChessBase(c, body);
        c.Polygon(body,
            new(10, 21), new(10, 14), new(9, 12), new(12, 8), new(11, 5), new(15, 5), new(15, 8),
            new(21, 6), new(24, 9), new(22, 12), new(18, 11), new(18, 21));
        c.Outline(Ink);
        c.DropShadow(Shade);
    }

    private static void ChessPawn(PixelCanvas c, Color body)
    {
        ChessBase(c, body);
        c.Rect(12, 15, 8, 7, body);
        c.Disc(16, 11, 5.5f, body);
        c.Outline(Ink);
        c.DropShadow(Shade);
    }

    /// <summary>
    /// Werkzeugleisten-Symbol fuer die Schach-Ansicht - 32 px wie die anderen Werkzeugknoepfe
    /// (ToolButton zeigt Icons immer in ihrer Grundgroesse, ein 16-px-Icon wirkte daneben zu
    /// klein und "schwebte" wegen der oben ausgerichteten Icons hoeher als die anderen).
    /// Haengt vom Farbschema ab (wie arrow_down/check_off), damit die Figur im Dunkelmodus
    /// hell und im Hellmodus dunkel bleibt, statt immer schwarz auf dunklem Grund zu verschwinden.
    /// </summary>
    private static void ChessTool(PixelCanvas c, Palette p)
    {
        var piece = p.Text;
        c.Rect(6, 26, 20, 4, piece);    // Sockel
        c.Rect(10, 22, 12, 4, piece);   // Fuss
        c.Rect(12, 16, 8, 6, piece);    // Stamm
        c.Rect(10, 12, 12, 4, piece);   // Kragen
        c.Disc(16, 8, 6f, piece);       // Kopf
    }

    // ==================================================================
    // Minispiel "Diskettenlager"
    // ==================================================================

    private static void TileFloor(PixelCanvas c)
    {
        c.Rect(0, 0, 16, 16, C("#d6cfbd"));
        for (var y = 0; y < 15; y++)
            for (var x = 0; x < 15; x++)
                if ((x + y * 3) % 7 == 0) c.Px(x, y, C("#cbc3af"));
        c.HLine(0, 15, 16, C("#b9b09a"));
        c.VLine(15, 0, 16, C("#b9b09a"));
        c.HLine(0, 0, 15, C("#e4decf"));
    }

    private static void TileWall(PixelCanvas c)
    {
        var mortar = C("#cfc2ab");
        c.Rect(0, 0, 16, 16, mortar);
        for (var row = 0; row < 4; row++)
        {
            var y = row * 4;
            var offset = row % 2 == 0 ? 0 : 4;
            for (var x = -offset; x < 16; x += 8)
            {
                var bx = Math.Max(0, x);
                var bw = Math.Min(x + 7, 16) - bx;
                if (bw <= 0) continue;
                c.Rect(bx, y, bw, 3, C("#a5492f"));
                c.HLine(bx, y, bw, C("#c4674a"));
                c.HLine(bx, y + 2, bw, C("#833722"));
            }
        }
    }

    private static void TileGoal(PixelCanvas c)
    {
        c.Rect(2, 4, 12, 8, C("#c9ced6"));
        c.Bevel(2, 4, 12, 8, C("#f1f3f6"), C("#7d838c"));
        c.Rect(4, 6, 8, 2, C("#20242c"));
        c.HLine(4, 8, 8, C("#f8f9fb"));
        c.Rect(11, 10, 2, 1, C("#8d939b"));
        c.Outline(C("#3a3f47"));
    }

    private static void TileBox(PixelCanvas c, bool done)
    {
        var body = C(done ? "#2e9e57" : "#3f69b8");
        c.Rect(2, 2, 12, 12, body);
        c.VLine(2, 2, 12, body.Lightened(0.3f));
        c.HLine(2, 2, 11, body.Lightened(0.3f));
        c.HLine(3, 13, 11, body.Darkened(0.3f));
        c.Rect(5, 2, 6, 4, C("#c9ced6"));
        c.Rect(8, 3, 2, 2, C("#343942"));
        c.Rect(4, 8, 8, 5, C("#f4f0e3"));
        c.HLine(5, 9, 6, C(done ? "#2e9e57" : "#d9443a"));
        c.HLine(5, 11, 4, C("#9aa5ba"));
        c.Clear(13, 2);
        if (done) c.Px(12, 12, C("#b8ffc2"));
        c.Outline(C("#141a28"));
    }

    private static void TilePlayer(PixelCanvas c)
    {
        // "Compu": kleiner Kerl mit Roehrenmonitor-Kopf
        c.Rect(3, 0, 10, 8, C("#d9d2bd"));
        c.HLine(3, 0, 10, C("#efe9d8"));
        c.Rect(4, 1, 8, 5, C("#173524"));
        c.Px(6, 3, C("#6dff8a"));
        c.Px(9, 3, C("#6dff8a"));
        c.HLine(6, 5, 4, C("#3fbf5a"));
        c.Rect(7, 8, 2, 1, C("#8d939b"));
        c.Rect(4, 9, 8, 4, C("#3f69b8"));
        c.HLine(4, 9, 8, C("#7099de"));
        c.Rect(2, 9, 2, 3, C("#d9d2bd"));
        c.Rect(12, 9, 2, 3, C("#d9d2bd"));
        c.Rect(5, 13, 2, 3, C("#2b2b33"));
        c.Rect(9, 13, 2, 3, C("#2b2b33"));
        c.Outline(C("#15161b"));
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
