using System.Security.Cryptography;
using System.Text;

namespace Floppy.Core.Minigame;

/// <summary>Ein Level: Zeilen aus Zeichen (siehe <see cref="LevelPack"/>).</summary>
public sealed record Level(string Name, IReadOnlyList<string> Rows)
{
    public int Width => Rows.Count == 0 ? 0 : Rows.Max(r => r.Length);
    public int Height => Rows.Count;

    /// <summary>
    /// Level-ID aus dem Inhalt (nicht dem Namen), z. B. <c>3F9A0C1B22D7</c>. Dasselbe Level hat
    /// ueberall dieselbe ID - egal in welchem Paket oder wie es heisst. So koennen sich Spieler in
    /// der Rangliste vergleichen, auch bei eigenen Leveln aus dem Editor.
    /// </summary>
    public string Id => LevelPack.LevelIdOf(Rows);   // berechnet, damit "with { Rows = ... }" stimmt

    /// <summary>Anzahl Disketten (fuer die Punkte) - normal, farbig oder mit begrenzter Reichweite.</summary>
    public int Boxes => Rows.Sum(r => r.Count(c => c is '$' or '*' or 'R' or 'B' || c is >= '1' and <= '9'));

    /// <summary>ID lesbar: <c>3F9A-0C1B-22D7</c>.</summary>
    public string DisplayId => Id.Length == 12 ? $"{Id[..4]}-{Id[4..8]}-{Id[8..]}" : Id;
}

/// <summary>Warum ein Level nicht spielbar ist (siehe <see cref="LevelPack.Check"/>).</summary>
public sealed record LevelProblem(string Code, IReadOnlyList<string> Args);

/// <summary>
/// Levelpaket fuer das Minispiel "Diskettenlager" - reine Textdaten, KEIN Programmcode.
/// Eine fremde Diskette kann damit in der App nichts ausfuehren.
///
/// Format (UTF-8):
/// <code>
/// ; Kommentar
/// title  = Diskettenlager
/// author = Mael
///
/// [level] Erste Diskette
/// #######
/// #@ $ .#
/// #######
/// </code>
/// Zeichen: <c>#</c> Wand, <c>@</c> Spieler, <c>$</c> Diskette, <c>.</c> Laufwerk,
/// <c>*</c> Diskette im Laufwerk, <c>+</c> Spieler auf Laufwerk, Leerzeichen/<c>-</c>/<c>_</c> Boden.
/// Farbige Disketten (passen nur ins gleichfarbige Laufwerk): <c>R</c>/<c>r</c> rot, <c>B</c>/<c>b</c> blau.
/// <c>1</c>-<c>9</c>: normale Diskette, aber nur noch so oft schiebbar (dann steht sie fest).
/// </summary>
public sealed record LevelPack(string Title, string Author, IReadOnlyList<Level> Levels, string Id, IReadOnlyList<string> Problems)
{
    public const int MaxFileBytes = 256 * 1024;
    public const int MaxLevels = 200;
    public const int MaxWidth = 40;
    public const int MaxHeight = 30;

    /// <summary>Erlaubte Dateiendungen fuer Levelpakete auf Disketten.</summary>
    public static IReadOnlyList<string> Extensions { get; } = [".txt"];

    private const string TileChars = "#@+$*. -_RrBb123456789";

    public static LevelPack Load(string path)
    {
        var info = new FileInfo(path);
        if (!info.Exists) return Invalid($"Levelpaket nicht gefunden: {path}");
        if (info.Length > MaxFileBytes) return Invalid($"Levelpaket ist zu gross ({info.Length / 1024} KB, erlaubt {MaxFileBytes / 1024} KB).");
        return Parse(File.ReadAllText(path));
    }

    public static LevelPack Parse(string text)
    {
        var problems = new List<string>();
        if (text.Length > MaxFileBytes) return Invalid($"Levelpaket ist zu gross (erlaubt {MaxFileBytes / 1024} KB).");

        string title = "", author = "";
        var levels = new List<Level>();
        string? name = null;
        var rows = new List<string>();
        var closed = false;

        void Finish()
        {
            if (name is null) return;
            var number = levels.Count + problems.Count(p => p.StartsWith("Level ", StringComparison.Ordinal)) + 1;
            var trimmed = TrimRows(rows);
            var problem = Validate(trimmed);
            if (problem is null && levels.Count < MaxLevels) levels.Add(new Level(name, trimmed));
            else if (problem is not null) problems.Add($"Level {number} \"{name}\": {problem}");
            name = null;
            rows = [];
            closed = false;
        }

        foreach (var raw in text.Replace("\r\n", "\n").Split('\n'))
        {
            var line = raw.TrimEnd('\r').TrimEnd();
            var trimmed = line.TrimStart();

            if (trimmed.StartsWith("[level]", StringComparison.OrdinalIgnoreCase))
            {
                Finish();
                name = trimmed[7..].Trim();
                if (name.Length == 0) name = $"Level {levels.Count + 1}";
                if (name.Length > 40) name = name[..40];
                continue;
            }
            if (trimmed.StartsWith(';')) continue;

            if (name is null)
            {
                var eq = line.IndexOf('=');
                if (eq <= 0) continue;
                var key = line[..eq].Trim().ToLowerInvariant();
                var value = Clean(line[(eq + 1)..], 40);
                if (key == "title") title = value;
                else if (key == "author") author = value;
                continue;
            }

            if (line.Length == 0)
            {
                if (rows.Count > 0) closed = true;   // Leerzeile beendet das Level
                continue;
            }
            if (!closed) rows.Add(line);
        }
        Finish();

        if (levels.Count == 0 && problems.Count == 0) problems.Add("Keine Level gefunden.");
        return new LevelPack(title.Length > 0 ? title : "Diskettenlager", author, levels, ComputeId(levels), problems);
    }

    /// <summary>Warum das Level nicht spielbar ist (deutscher Text) - oder <c>null</c>.</summary>
    public static string? Validate(IReadOnlyList<string> rows) => Check(rows) switch
    {
        null => null,
        { Code: "EMPTY" } => "leer",
        { Code: "TOO_BIG" } => $"zu gross (hoechstens {MaxWidth}x{MaxHeight})",
        { Code: "UNKNOWN_CHAR" } p => $"unbekanntes Zeichen '{p.Args[0]}'",
        { Code: "NO_PLAYER" } => "kein Spieler (@)",
        { Code: "MANY_PLAYERS" } => "mehr als ein Spieler (@)",
        { Code: "NO_BOX" } => "keine Diskette ($)",
        { Code: "COUNT_MISMATCH" } p => $"{p.Args[0]} Disketten, aber {p.Args[1]} Laufwerke",
        { Code: "RED_MISMATCH" } p => $"{p.Args[0]} rote Disketten, aber {p.Args[1]} rote Laufwerke",
        { Code: "BLUE_MISMATCH" } p => $"{p.Args[0]} blaue Disketten, aber {p.Args[1]} blaue Laufwerke",
        _ => "ist schon geloest",
    };

    /// <summary>
    /// Warum das Level nicht spielbar ist, als Kennung fuer die Uebersetzung (<c>LEVEL_&lt;Code&gt;</c>):
    /// EMPTY, TOO_BIG (Breite, Hoehe), UNKNOWN_CHAR (Zeichen), NO_PLAYER, MANY_PLAYERS, NO_BOX,
    /// COUNT_MISMATCH/RED_MISMATCH/BLUE_MISMATCH (Disketten, Laufwerke je Art), SOLVED - oder <c>null</c>.
    /// </summary>
    public static LevelProblem? Check(IReadOnlyList<string> rows)
    {
        if (rows.Count == 0) return new("EMPTY", []);
        if (rows.Count > MaxHeight || rows.Any(r => r.Length > MaxWidth)) return new("TOO_BIG", [MaxWidth.ToString(), MaxHeight.ToString()]);

        int players = 0, boxes = 0, goals = 0, boxesOnGoal = 0;
        int redBoxes = 0, redGoals = 0, blueBoxes = 0, blueGoals = 0;
        foreach (var row in rows)
        {
            foreach (var c in row)
            {
                if (!TileChars.Contains(c)) return new("UNKNOWN_CHAR", [c.ToString()]);
                switch (c)
                {
                    case '@': players++; break;
                    case '+': players++; goals++; break;
                    case '$': boxes++; break;
                    case '.': goals++; break;
                    case '*': boxes++; goals++; boxesOnGoal++; break;
                    case >= '1' and <= '9': boxes++; break;
                    case 'R': redBoxes++; break;
                    case 'r': redGoals++; break;
                    case 'B': blueBoxes++; break;
                    case 'b': blueGoals++; break;
                }
            }
        }
        if (players != 1) return new(players == 0 ? "NO_PLAYER" : "MANY_PLAYERS", []);
        if (boxes + redBoxes + blueBoxes == 0) return new("NO_BOX", []);
        if (boxes != goals) return new("COUNT_MISMATCH", [boxes.ToString(), goals.ToString()]);
        if (redBoxes != redGoals) return new("RED_MISMATCH", [redBoxes.ToString(), redGoals.ToString()]);
        if (blueBoxes != blueGoals) return new("BLUE_MISMATCH", [blueBoxes.ToString(), blueGoals.ToString()]);
        if (boxesOnGoal == boxes && redBoxes == 0 && blueBoxes == 0) return new("SOLVED", []);
        return null;
    }

    private static List<string> TrimRows(List<string> rows) => NormalizeRows(rows).ToList();

    /// <summary>
    /// Einheitliche Form: <c>-</c>/<c>_</c> werden Leerzeichen, Leerzeichen am Zeilenende und leere
    /// Zeilen oben/unten fallen weg, gemeinsame Einrueckung wird entfernt. Grundlage der Level-ID.
    /// </summary>
    public static IReadOnlyList<string> NormalizeRows(IEnumerable<string> rows)
    {
        var list = rows.Select(r => r.Replace('-', ' ').Replace('_', ' ').TrimEnd()).ToList();
        while (list.Count > 0 && list[0].Length == 0) list.RemoveAt(0);
        while (list.Count > 0 && list[^1].Length == 0) list.RemoveAt(list.Count - 1);
        var indent = list.Where(r => r.Length > 0).Select(r => r.Length - r.TrimStart().Length).DefaultIfEmpty(0).Min();
        return list.Select(r => r.Length >= indent ? r[indent..] : "").ToList();
    }

    /// <summary>12 Hex-Zeichen aus SHA-256 der einheitlichen Zeilen.</summary>
    public static string LevelIdOf(IEnumerable<string> rows)
    {
        var text = "diskettenlager-v1\n" + string.Join("\n", NormalizeRows(rows));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..12];
    }

    /// <summary>Levelpaket als Text (zum Speichern eigener Level).</summary>
    public static string Render(string title, string author, IEnumerable<Level> levels)
    {
        var sb = new StringBuilder()
            .Append("; Floppy Hub - Diskettenlager Levelpaket\r\n")
            .Append("; # Wand  @ Spieler  $ Diskette  . Laufwerk  * Diskette im Laufwerk  + Spieler auf Laufwerk\r\n")
            .Append("title = ").Append(Clean(title, 40)).Append("\r\n");
        if (author.Trim().Length > 0) sb.Append("author = ").Append(Clean(author, 40)).Append("\r\n");

        foreach (var level in levels)
        {
            sb.Append("\r\n[level] ").Append(Clean(level.Name, 40)).Append("\r\n");
            var width = level.Width;
            foreach (var row in NormalizeRows(level.Rows))
            {
                // Eine Zeile nur aus Boden wuerde beim Lesen das Level beenden: dann mit "-" schreiben.
                sb.Append(row.Trim().Length == 0 ? new string('-', Math.Max(1, width)) : row).Append("\r\n");
            }
        }
        return sb.ToString();
    }

    private static string ComputeId(IReadOnlyList<Level> levels)
    {
        var sb = new StringBuilder();
        foreach (var l in levels) sb.Append(string.Join("\n", l.Rows)).Append("\n\n");
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString())))[..16];
    }

    private static string Clean(string value, int max)
    {
        var v = new string(value.Trim().Where(c => !char.IsControl(c)).ToArray());
        return v.Length > max ? v[..max] : v;
    }

    private static LevelPack Invalid(string problem) => new("", "", [], "", [problem]);
}
