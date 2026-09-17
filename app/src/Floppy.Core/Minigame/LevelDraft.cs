namespace Floppy.Core.Minigame;

public enum EditorTool { Wall, Floor, Goal, Box, Player, Erase }

/// <summary>
/// Ein Level im Editor: ein Raster, auf dem man Waende, Laufwerke, Disketten und den Spieler
/// setzt. Ergibt ueber <see cref="ToLevel"/> ein normales <see cref="Level"/> mit ID.
/// </summary>
public sealed class LevelDraft
{
    public const int MinSize = 3;

    private char[,] _cells;

    /// <summary>Neues Level: Rand aus Waenden, Spieler in der Mitte.</summary>
    public LevelDraft(int width = 9, int height = 7, string name = "")
    {
        Width = Math.Clamp(width, MinSize, LevelPack.MaxWidth);
        Height = Math.Clamp(height, MinSize, LevelPack.MaxHeight);
        Name = name;
        _cells = new char[Width, Height];
        for (var y = 0; y < Height; y++)
            for (var x = 0; x < Width; x++)
                _cells[x, y] = x == 0 || y == 0 || x == Width - 1 || y == Height - 1 ? '#' : ' ';
        _cells[Width / 2, Height / 2] = '@';
    }

    public int Width { get; private set; }
    public int Height { get; private set; }
    public string Name { get; set; }

    public static LevelDraft FromLevel(Level level)
    {
        var rows = LevelPack.NormalizeRows(level.Rows);
        var draft = new LevelDraft(Math.Max(MinSize, rows.Count == 0 ? 0 : rows.Max(r => r.Length)), Math.Max(MinSize, rows.Count), level.Name);
        for (var y = 0; y < draft.Height; y++)
            for (var x = 0; x < draft.Width; x++)
                draft._cells[x, y] = y < rows.Count && x < rows[y].Length ? rows[y][x] : ' ';
        return draft;
    }

    public char Get(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height ? _cells[x, y] : ' ';

    /// <summary>Werkzeug auf ein Feld anwenden. true = etwas hat sich geaendert.</summary>
    public bool Paint(int x, int y, EditorTool tool)
    {
        if (x < 0 || y < 0 || x >= Width || y >= Height) return false;
        var old = _cells[x, y];
        var goal = old is '.' or '*' or '+';
        var next = tool switch
        {
            EditorTool.Wall => '#',
            EditorTool.Floor => goal ? '.' : ' ',    // Diskette/Spieler/Wand weg, Laufwerk bleibt
            EditorTool.Goal => old switch { '$' or '*' => '*', '@' or '+' => '+', _ => '.' },
            EditorTool.Box => goal ? '*' : '$',
            EditorTool.Player => goal ? '+' : '@',
            _ => ' ',
        };
        if (tool == EditorTool.Player && next != old)
        {
            // es gibt nur einen Spieler: den alten entfernen
            for (var yy = 0; yy < Height; yy++)
                for (var xx = 0; xx < Width; xx++)
                    _cells[xx, yy] = _cells[xx, yy] switch { '@' => ' ', '+' => '.', var c => c };
        }
        if (next == old) return false;
        _cells[x, y] = next;
        return true;
    }

    /// <summary>Groesse aendern; Inhalt bleibt oben links stehen.</summary>
    public void Resize(int width, int height)
    {
        width = Math.Clamp(width, MinSize, LevelPack.MaxWidth);
        height = Math.Clamp(height, MinSize, LevelPack.MaxHeight);
        var cells = new char[width, height];
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
                cells[x, y] = x < Width && y < Height ? _cells[x, y] : ' ';
        _cells = cells;
        Width = width;
        Height = height;
    }

    public IReadOnlyList<string> ToRows()
    {
        var rows = new List<string>();
        for (var y = 0; y < Height; y++)
        {
            var chars = new char[Width];
            for (var x = 0; x < Width; x++) chars[x] = _cells[x, y];
            rows.Add(new string(chars));
        }
        return LevelPack.NormalizeRows(rows);
    }

    public Level ToLevel() => new(Name.Trim().Length > 0 ? Name.Trim() : "Eigenes Level", ToRows());

    /// <summary>Warum das Level (noch) nicht spielbar ist - oder <c>null</c>.</summary>
    public string? Problem() => LevelPack.Validate(ToRows());

    /// <summary>Dasselbe als Kennung fuer die Uebersetzung.</summary>
    public LevelProblem? ProblemCode() => LevelPack.Check(ToRows());
}

/// <summary>Eigene Level aus dem Editor: ein Levelpaket in <c>%LOCALAPPDATA%\FloppyHub\levels\eigene-level.txt</c>.</summary>
public static class CustomLevels
{
    public const string FileName = "eigene-level.txt";
    public const string DefaultTitle = "Eigene Level";

    public static LevelPack Load(string file) =>
        File.Exists(file) ? LevelPack.Load(file) : new LevelPack(DefaultTitle, "", [], "", []);

    /// <summary>Level speichern: gleicher Name ersetzt das alte, sonst hinten anhaengen.</summary>
    /// <returns>Position im Paket (ab 0).</returns>
    public static int Save(string file, Level level, string author, string? replaceName = null)
    {
        if (LevelPack.Validate(level.Rows) is { } problem) throw new ArgumentException($"Level nicht spielbar: {problem}", nameof(level));
        var pack = Load(file);
        var levels = pack.Levels.ToList();
        var index = levels.FindIndex(l => string.Equals(l.Name, replaceName ?? level.Name, StringComparison.CurrentCultureIgnoreCase));
        if (index >= 0)
        {
            levels[index] = level;
        }
        else
        {
            if (levels.Count >= LevelPack.MaxLevels) throw new InvalidOperationException($"Hoechstens {LevelPack.MaxLevels} eigene Level.");
            levels.Add(level);
            index = levels.Count - 1;
        }
        Write(file, pack.Title.Length > 0 ? pack.Title : DefaultTitle, author.Length > 0 ? author : pack.Author, levels);
        return index;
    }

    public static bool Delete(string file, string name)
    {
        var pack = Load(file);
        var levels = pack.Levels.ToList();
        if (levels.RemoveAll(l => string.Equals(l.Name, name, StringComparison.CurrentCultureIgnoreCase)) == 0) return false;
        Write(file, pack.Title, pack.Author, levels);
        return true;
    }

    private static void Write(string file, string title, string author, IEnumerable<Level> levels)
    {
        FloppyPaths.EnsureDirectory(Path.GetDirectoryName(Path.GetFullPath(file))!);
        var temp = file + ".tmp";
        File.WriteAllText(temp, LevelPack.Render(title, author, levels), new System.Text.UTF8Encoding(true));
        File.Move(temp, file, overwrite: true);
    }
}
