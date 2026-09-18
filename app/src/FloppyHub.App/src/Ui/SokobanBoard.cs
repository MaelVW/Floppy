using Floppy.Core.Minigame;
using FloppyHub.App.Art;
using FloppyHub.App.Core;
using FloppyHub.App.Skin;
using Godot;

namespace FloppyHub.App.Ui;

/// <summary>Spielfeld "Diskettenlager": zeichnet die Kacheln und nimmt Tasten an.</summary>
public partial class SokobanBoard : Control
{
    private static readonly Color Background = new("#23262d");

    public SokobanBoard()
    {
        FocusMode = FocusModeEnum.All;
        MouseFilter = MouseFilterEnum.Stop;
        CustomMinimumSize = new Vector2(240, 200);
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        SizeFlagsVertical = SizeFlags.ExpandFill;
    }

    public SokobanGame? Game { get; private set; }

    /// <summary>Nach jedem Zug, Rueckgaengig und Neustart.</summary>
    public event Action? Changed;

    /// <summary>Level geloest.</summary>
    public event Action? Solved;

    /// <summary>Neues Level geladen oder Neustart (Stoppuhr zuruecksetzen).</summary>
    public event Action? Restarted;

    public void Load(Level level)
    {
        Game = new SokobanGame(level);
        Restarted?.Invoke();
        Changed?.Invoke();
        QueueRedraw();
        CallDeferred(Control.MethodName.GrabFocus);
    }

    public void Step(Direction direction)
    {
        if (Game is null || Game.IsSolved) return;
        var result = Game.Move(direction);
        QueueRedraw();
        if (result == MoveResult.Blocked) return;
        Changed?.Invoke();
        if (Game.IsSolved) Solved?.Invoke();
    }

    public void UndoStep()
    {
        if (Game is null || Game.IsSolved || !Game.Undo()) return;
        Changed?.Invoke();
        QueueRedraw();
    }

    public void Restart()
    {
        if (Game is null) return;
        Game.Reset();
        Restarted?.Invoke();
        Changed?.Invoke();
        QueueRedraw();
        GrabFocus();
    }

    public override void _GuiInput(InputEvent e)
    {
        if (e is InputEventMouseButton { Pressed: true })
        {
            GrabFocus();
            return;
        }
        if (e is not InputEventKey { Pressed: true } key) return;

        Direction? dir = key.Keycode switch
        {
            Key.Up or Key.W => Direction.Up,
            Key.Down or Key.S => Direction.Down,
            Key.Left or Key.A => Direction.Left,
            Key.Right or Key.D => Direction.Right,
            _ => null,
        };
        if (dir is { } d)
        {
            Step(d);
            AcceptEvent();
        }
        else if (!key.Echo && key.Keycode is Key.Z or Key.Backspace)
        {
            UndoStep();
            AcceptEvent();
        }
        else if (!key.Echo && key.Keycode == Key.R)
        {
            Restart();
            AcceptEvent();
        }
    }

    public override void _Notification(int what)
    {
        if (what is (int)NotificationResized or (int)NotificationFocusEnter or (int)NotificationFocusExit) QueueRedraw();
    }

    public override void _Draw()
    {
        var p = Palette.Current;
        var line = UiScale.Line;
        DrawRect(new Rect2(Vector2.Zero, Size), Background);
        // eingelassener Rahmen
        DrawRect(new Rect2(0, 0, Size.X, line), p.Shadow);
        DrawRect(new Rect2(0, 0, line, Size.Y), p.Shadow);
        DrawRect(new Rect2(0, Size.Y - line, Size.X, line), p.Highlight);
        DrawRect(new Rect2(Size.X - line, 0, line, Size.Y), p.Highlight);

        var g = Game;
        if (g is null) return;

        // Kachelgroesse: so gross wie moeglich, in 4er-Schritten, 16..64
        var fit = MathF.Min((Size.X - 16) / g.Width, (Size.Y - 16) / g.Height);
        var tile = Math.Clamp(MathF.Floor(fit / 4) * 4, 12, 64);
        var origin = UiScale.Snap((Size.X - tile * g.Width) / 2) * Vector2.Right + UiScale.Snap((Size.Y - tile * g.Height) / 2) * Vector2.Down;
        var scale = tile / 16f;

        var floor = Icons.Get("tile_floor", scale);
        var wall = Icons.Get("tile_wall", scale);
        var goal = Icons.Get("tile_goal", scale);
        var box = Icons.Get("tile_box", scale);
        var boxDone = Icons.Get("tile_box_goal", scale);
        var player = Icons.Get("tile_player", scale);

        for (var y = 0; y < g.Height; y++)
        {
            for (var x = 0; x < g.Width; x++)
            {
                var r = new Rect2(origin + new Vector2(x * tile, y * tile), tile, tile);
                if (g.IsInside(x, y))
                {
                    DrawTextureRect(floor, r, false);
                    if (g.GoalKind(x, y) is { } gk)
                    {
                        DrawTextureRect(goal, r, false);
                        if (gk != DiskKind.Universal) DrawKindBadge(r, tile, KindColor(gk, p));
                    }
                }
                else if (g.IsWall(x, y) && TouchesInside(g, x, y))
                {
                    DrawTextureRect(wall, r, false);
                }
            }
        }

        foreach (var (bx, by) in g.Boxes)
        {
            var state = g.Box(bx, by)!.Value;
            var r = new Rect2(origin + new Vector2(bx * tile, by * tile), tile, tile);
            DrawTextureRect(g.GoalKind(bx, by) == state.Kind ? boxDone : box, r, false);
            if (state.Kind != DiskKind.Universal) DrawKindBadge(r, tile, KindColor(state.Kind, p));
            if (state.RangeLeft is { } left) DrawRangeNumber(r, tile, left);
        }

        var pr = new Rect2(origin + new Vector2(g.Player.X * tile, g.Player.Y * tile), tile, tile);
        if (g.Facing == Direction.Left)
        {
            DrawSetTransform(new Vector2(pr.Position.X + tile, pr.Position.Y), 0, new Vector2(-1, 1));
            DrawTextureRect(player, new Rect2(0, 0, tile, tile), false);
            DrawSetTransform(Vector2.Zero, 0, Vector2.One);
        }
        else
        {
            DrawTextureRect(player, pr, false);
        }

        if (g.IsSolved) DrawBanner(Loc.T("GAME_SOLVED_BANNER"));
        else if (!HasFocus()) DrawBanner(Loc.T("GAME_CLICK_TO_PLAY"), small: true);
    }

    private static Color KindColor(DiskKind kind, Palette p) => kind == DiskKind.Red ? p.Error : p.Accent;

    /// <summary>Farbige Ecke: zeigt, dass Diskette/Laufwerk nur zueinander passen (nicht zu normalen).</summary>
    private void DrawKindBadge(Rect2 r, float tile, Color color)
    {
        var size = MathF.Max(6, tile * 0.3f);
        var badge = new Rect2(r.Position.X + tile - size - 2, r.Position.Y + 2, size, size);
        DrawRect(badge, new Color(0, 0, 0, 0.5f));
        DrawRect(badge.Grow(-1.5f), color);
    }

    /// <summary>Verbleibende Schub-Reichweite als Zahl auf der Diskette.</summary>
    private void DrawRangeNumber(Rect2 r, float tile, int left)
    {
        var font = SkinBuilder.BoldFont;
        var size = Math.Max(10, (int)(tile * 0.5f));
        var text = left.ToString();
        var textSize = font.GetStringSize(text, HorizontalAlignment.Center, -1, size);
        var pos = new Vector2(r.Position.X + (tile - textSize.X) / 2, r.Position.Y + (tile + textSize.Y) / 2);
        DrawString(font, pos + Vector2.One, text, HorizontalAlignment.Left, -1, size, Colors.Black);
        DrawString(font, pos, text, HorizontalAlignment.Left, -1, size, Colors.White);
    }

    private static bool TouchesInside(SokobanGame g, int x, int y)
    {
        for (var dy = -1; dy <= 1; dy++)
            for (var dx = -1; dx <= 1; dx++)
                if (g.IsInside(x + dx, y + dy)) return true;
        return false;
    }

    private void DrawBanner(string text, bool small = false)
    {
        var font = SkinBuilder.BoldFont;
        var size = small ? 13 : 26;
        var textSize = font.GetStringSize(text, HorizontalAlignment.Left, -1, size);
        var box = new Rect2((Size.X - textSize.X) / 2 - 16, small ? Size.Y - textSize.Y - 22 : (Size.Y - textSize.Y) / 2 - 10,
            textSize.X + 32, textSize.Y + 20);
        DrawRect(box, new Color(0, 0, 0, small ? 0.55f : 0.7f));
        DrawRect(box, new Color("#3bd14f"), false, UiScale.Line);
        DrawString(font, new Vector2(box.Position.X + 16, box.Position.Y + 10 + font.GetAscent(size)), text,
            HorizontalAlignment.Left, -1, size, small ? Colors.White : new Color("#6dff8a"));
    }
}
