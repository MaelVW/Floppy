using System.Globalization;
using System.Text;
using Floppy.Core.Chat;

namespace Floppy.Core.Minigame;

/// <param name="Level">Levelnummer ab 1.</param>
/// <param name="Millis">Spielzeit (0 = alter Eintrag ohne Zeit).</param>
/// <param name="Points">Punkte laut <see cref="GameScoring"/> (0 = alter Eintrag).</param>
/// <param name="LevelId">ID des Levels (fuer die Rangliste im Chat).</param>
/// <param name="LevelName">Name des Levels (fuer die Rangliste im Chat).</param>
public sealed record Score(int Level, int Moves, int Pushes, string Name, string Date,
    long Millis = 0, int Points = 0, string LevelId = "", string LevelName = "");

/// <summary>
/// Bestenliste eines Levelpakets. Liegt als Textdatei neben dem Paket auf der Diskette
/// (wandert also mit) und zusaetzlich lokal. Pro Level zaehlt nur der beste Versuch:
/// mehr Punkte, bei Gleichstand weniger Zuege, dann weniger Schuebe, dann weniger Zeit.
/// </summary>
public sealed class ScoreBoard
{
    public const string DiscFileName = "scores.txt";
    public const int MaxNameLength = 16;

    private readonly Dictionary<int, Score> _best = new();

    public ScoreBoard(string packId) => PackId = packId;

    public string PackId { get; }
    public IReadOnlyCollection<Score> All => _best.Values;

    /// <summary>Bester Versuch fuer Level Nummer <paramref name="level"/>.</summary>
    /// <param name="levelId">Wenn angegeben: Eintraege fuer ein anderes Level (Paket wurde geaendert) zaehlen nicht.</param>
    public Score? Best(int level, string? levelId = null)
    {
        var best = _best.GetValueOrDefault(level);
        if (best is null || string.IsNullOrEmpty(levelId) || best.LevelId.Length == 0) return best;
        return string.Equals(best.LevelId, levelId, StringComparison.OrdinalIgnoreCase) ? best : null;
    }

    public bool IsRecord(Score candidate) => Best(candidate.Level, candidate.LevelId) is not { } best || IsBetter(candidate, best);

    public static bool IsBetter(Score a, Score b) =>
        a.Points != b.Points ? a.Points > b.Points :
        a.Moves != b.Moves ? a.Moves < b.Moves :
        a.Pushes != b.Pushes ? a.Pushes < b.Pushes :
        a.Millis > 0 && (b.Millis == 0 || a.Millis < b.Millis);

    /// <summary>true = neuer Rekord (und uebernommen).</summary>
    public bool Submit(Score score)
    {
        if (score.Level < 1 || score.Moves < 1 || score.Pushes < 0 || score.Millis < 0 || score.Points < 0) return false;
        if (!IsRecord(score)) return false;
        _best[score.Level] = score with
        {
            Name = CleanName(score.Name),
            LevelId = score.LevelId.All(char.IsAsciiHexDigit) && score.LevelId.Length <= 24 ? score.LevelId : "",
            LevelName = CleanField(score.LevelName, 40),
        };
        return true;
    }

    /// <summary>Andere Bestenliste einmischen (Diskette + lokal).</summary>
    public void Merge(ScoreBoard other)
    {
        if (other.PackId != PackId) return;
        foreach (var s in other.All) Submit(s);
    }

    public static string CleanName(string? name)
    {
        var cleaned = CleanField(name, MaxNameLength);
        return cleaned.Length == 0 ? "Spieler" : cleaned;
    }

    private static string CleanField(string? value, int max)
    {
        var cleaned = new string((value ?? "").Where(c => !char.IsControl(c) && c != ';').ToArray()).Trim();
        return cleaned.Length > max ? cleaned[..max].Trim() : cleaned;
    }

    /// <summary>Datei lesen; fehlt sie, ist sie kaputt oder gehoert sie zu einem anderen Paket: leer.</summary>
    public static ScoreBoard Load(string path, string packId)
    {
        var board = new ScoreBoard(packId);
        foreach (var (filePack, score) in ReadRows(path))
            if (filePack == packId) board.Submit(score);
        return board;
    }

    /// <summary>Alle Zeilen einer Datei (egal welches Paket).</summary>
    private static IEnumerable<(string Pack, Score Score)> ReadRows(string path)
    {
        var rows = new List<(string, Score)>();
        try
        {
            if (!File.Exists(path) || new FileInfo(path).Length > 64 * 1024) return rows;
            string? filePack = null;
            foreach (var raw in File.ReadLines(path))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith('#')) continue;
                if (line.StartsWith("pack", StringComparison.OrdinalIgnoreCase) && line.Contains('='))
                {
                    filePack = line[(line.IndexOf('=') + 1)..].Trim();
                    continue;
                }
                if (filePack is null) continue;

                var parts = line.Split(';');
                if (parts.Length < 5) continue;
                if (!Int(parts[0], out var level) || !Int(parts[1], out var moves) || !Int(parts[2], out var pushes)) continue;
                long millis = 0;
                var points = 0;
                if (parts.Length >= 7 && (!long.TryParse(parts[5], NumberStyles.None, CultureInfo.InvariantCulture, out millis) || !Int(parts[6], out points))) continue;
                rows.Add((filePack, new Score(level, moves, pushes, parts[3], parts[4].Trim(), millis, points,
                    parts.Length >= 8 ? parts[7].Trim() : "", parts.Length >= 9 ? parts[8].Trim() : "")));
            }
        }
        catch
        {
            // kaputte Datei: einfach ohne Rekorde weiter
        }
        return rows;
    }

    private static bool Int(string text, out int value) =>
        int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value);

    public void Save(string path)
    {
        var sb = new StringBuilder()
            .Append("# Floppy Hub - Diskettenlager Bestenliste\r\n")
            .Append("# Level;Zuege;Schuebe;Name;Datum;Millisekunden;Punkte;Level-ID;Levelname\r\n")
            .Append("pack = ").Append(PackId).Append("\r\n");
        foreach (var s in _best.Values.OrderBy(s => s.Level))
            sb.Append(CultureInfo.InvariantCulture, $"{s.Level};{s.Moves};{s.Pushes};{s.Name};{s.Date};{s.Millis};{s.Points};{s.LevelId};{s.LevelName}\r\n");
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
    }

    /// <summary>
    /// Die besten eigenen Ergebnisse aus allen lokalen Bestenlisten (<c>scores\*.txt</c>) - fuer
    /// die Rangliste im Chat. Pro Level-ID nur das beste; alte Eintraege ohne Punkte zaehlen nicht.
    /// </summary>
    public static IReadOnlyList<ChatScore> BestForChat(string scoresDirectory, int max = 120)
    {
        var best = new Dictionary<string, Score>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (!Directory.Exists(scoresDirectory)) return [];
            foreach (var file in Directory.EnumerateFiles(scoresDirectory, "*.txt").Take(500))
            {
                foreach (var (_, s) in ReadRows(file))
                {
                    if (s.Points <= 0 || s.LevelId.Length != 12 || !s.LevelId.All(char.IsAsciiHexDigit)) continue;
                    if (!best.TryGetValue(s.LevelId, out var old) || IsBetter(s, old)) best[s.LevelId] = s;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // was wir haben, reicht
        }

        return best.Values
            .OrderByDescending(s => s.Date, StringComparer.Ordinal)
            .Take(max)
            .Select(s => new ChatScore
            {
                Game = GameScoring.GameId,
                LevelId = s.LevelId.ToUpperInvariant(),
                LevelName = s.LevelName,
                Moves = s.Moves,
                Pushes = s.Pushes,
                Millis = s.Millis,
                Points = s.Points,
            })
            .ToList();
    }
}
