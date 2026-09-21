namespace Floppy.Core.Minigame;

public enum Direction { Up, Down, Left, Right }

public enum MoveResult { Blocked, Moved, Pushed }

/// <summary>Diskettenart: normal (passt in jedes Laufwerk), oder eine Farbe (nur ins gleichfarbige Laufwerk).</summary>
public enum DiskKind { Universal, Red, Blue }

/// <summary>Eine Diskette auf dem Feld: Art plus verbleibende Schub-Reichweite (null = unbegrenzt).</summary>
public readonly record struct BoxState(DiskKind Kind, int? RangeLeft);

/// <summary>Spiellogik "Diskettenlager": Disketten in die passenden Laufwerke schieben.</summary>
public sealed class SokobanGame
{
    private readonly bool[,] _walls;
    private readonly DiskKind?[,] _goals;
    private readonly bool[,] _inside;
    private readonly Dictionary<(int X, int Y), BoxState> _boxes = [];
    private readonly Stack<(int PX, int PY, (int X, int Y)? BoxFrom, (int X, int Y)? BoxTo, Direction Facing, BoxState PushedBox)> _history = new();
    private readonly Level _level;

    public SokobanGame(Level level)
    {
        _level = level;
        Width = level.Width;
        Height = level.Height;
        _walls = new bool[Width, Height];
        _goals = new DiskKind?[Width, Height];
        _inside = new bool[Width, Height];
        Reset();

        // Boden "innen" = vom Spieler erreichbar (ohne Waende). Aussen wird nicht gezeichnet.
        var queue = new Queue<(int X, int Y)>();
        queue.Enqueue(Player);
        _inside[Player.X, Player.Y] = true;
        while (queue.Count > 0)
        {
            var (x, y) = queue.Dequeue();
            foreach (var (dx, dy) in new[] { (0, -1), (0, 1), (-1, 0), (1, 0) })
            {
                int nx = x + dx, ny = y + dy;
                if (nx < 0 || ny < 0 || nx >= Width || ny >= Height || _inside[nx, ny] || _walls[nx, ny]) continue;
                _inside[nx, ny] = true;
                queue.Enqueue((nx, ny));
            }
        }
    }

    public int Width { get; }
    public int Height { get; }
    public (int X, int Y) Player { get; private set; }
    public Direction Facing { get; private set; } = Direction.Down;
    public int Moves { get; private set; }
    public int Pushes { get; private set; }
    public IReadOnlyCollection<(int X, int Y)> Boxes => _boxes.Keys;
    public bool CanUndo => _history.Count > 0;

    public bool IsSolved => _boxes.All(kv => _goals[kv.Key.X, kv.Key.Y] == kv.Value.Kind);
    public int BoxesOnGoal => _boxes.Count(kv => _goals[kv.Key.X, kv.Key.Y] == kv.Value.Kind);

    /// <summary>Ausserhalb des Spielfelds zaehlt als Wand.</summary>
    public bool IsWall(int x, int y) => x < 0 || y < 0 || x >= Width || y >= Height || _walls[x, y];
    public bool IsGoal(int x, int y) => GoalKind(x, y) is not null;
    public DiskKind? GoalKind(int x, int y) => IsWall(x, y) ? null : _goals[x, y];
    public bool IsInside(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height && _inside[x, y];
    public bool HasBox(int x, int y) => _boxes.ContainsKey((x, y));
    public BoxState? Box(int x, int y) => _boxes.TryGetValue((x, y), out var b) ? b : null;

    public void Reset()
    {
        _boxes.Clear();
        _history.Clear();
        Moves = Pushes = 0;
        Facing = Direction.Down;
        for (var y = 0; y < Height; y++)
        {
            var row = _level.Rows[y];
            for (var x = 0; x < Width; x++)
            {
                var c = x < row.Length ? row[x] : ' ';
                _walls[x, y] = c == '#';
                _goals[x, y] = c switch { '.' or '*' or '+' => DiskKind.Universal, 'r' => DiskKind.Red, 'b' => DiskKind.Blue, _ => null };
                if (c is '$' or '*') _boxes[(x, y)] = new BoxState(DiskKind.Universal, null);
                else if (c is >= '1' and <= '9') _boxes[(x, y)] = new BoxState(DiskKind.Universal, c - '0');
                else if (c == 'R') _boxes[(x, y)] = new BoxState(DiskKind.Red, null);
                else if (c == 'B') _boxes[(x, y)] = new BoxState(DiskKind.Blue, null);
                if (c is '@' or '+') Player = (x, y);
            }
        }
    }

    public MoveResult Move(Direction direction)
    {
        Facing = direction;
        if (IsSolved) return MoveResult.Blocked;

        var (dx, dy) = Delta(direction);
        var target = (X: Player.X + dx, Y: Player.Y + dy);
        if (IsWall(target.X, target.Y)) return MoveResult.Blocked;

        if (_boxes.TryGetValue(target, out var box))
        {
            var beyond = (X: target.X + dx, Y: target.Y + dy);
            if (IsWall(beyond.X, beyond.Y) || _boxes.ContainsKey(beyond)) return MoveResult.Blocked;
            if (box.RangeLeft == 0) return MoveResult.Blocked;   // Reichweite aufgebraucht: steht fest wie eine Wand

            _history.Push((Player.X, Player.Y, target, beyond, direction, box));
            _boxes.Remove(target);
            _boxes[beyond] = box.RangeLeft is { } left ? box with { RangeLeft = left - 1 } : box;
            Player = target;
            Moves++;
            Pushes++;
            return MoveResult.Pushed;
        }

        _history.Push((Player.X, Player.Y, null, null, direction, default));
        Player = target;
        Moves++;
        return MoveResult.Moved;
    }

    public bool Undo()
    {
        if (_history.Count == 0) return false;
        var step = _history.Pop();
        if (step is { BoxFrom: { } from, BoxTo: { } to })
        {
            _boxes.Remove(to);
            _boxes[from] = step.PushedBox;   // stellt auch die Reichweite von vor dem Schub wieder her
            Pushes--;
        }
        Player = (step.PX, step.PY);
        Facing = step.Facing;
        Moves--;
        return true;
    }

    public static (int Dx, int Dy) Delta(Direction d) => d switch
    {
        Direction.Up => (0, -1),
        Direction.Down => (0, 1),
        Direction.Left => (-1, 0),
        _ => (1, 0),
    };
}
