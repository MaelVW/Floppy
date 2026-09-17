using Floppy.Core.Minigame;
using FloppyHub.App.Art;
using FloppyHub.App.Skin;
using Godot;

namespace FloppyHub.App.Ui;

/// <summary>Zeichenflaeche des Level-Editors: Linksklick setzt das Werkzeug, Rechtsklick radiert, Ziehen malt.</summary>
public partial class LevelEditorCanvas : Control
{
    private static readonly Color Background = new("#23262d");
    private static readonly Color Grid = new(1, 1, 1, 0.07f);
    private static readonly Color Outside = new("#2d3038");

    private bool _painting;
    private EditorTool _strokeTool;
    private Vector2I? _hover;

    public LevelEditorCanvas()
    {
        FocusMode = FocusModeEnum.Click;
        MouseFilter = MouseFilterEnum.Stop;
        CustomMinimumSize = new Vector2(260, 220);
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        SizeFlagsVertical = SizeFlags.ExpandFill;
        MouseDefaultCursorShape = CursorShape.Cross;
    }

    public LevelDraft Draft { get; private set; } = new();
    public EditorTool Tool { get; set; } = EditorTool.Wall;

    /// <summary>Etwas wurde gemalt.</summary>
    public event Action? Changed;

    public void Load(LevelDraft draft)
    {
        Draft = draft;
        QueueRedraw();
    }

    public override void _GuiInput(InputEvent e)
    {
        switch (e)
        {
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left or MouseButton.Right } press:
                _painting = true;
                _strokeTool = press.ButtonIndex == MouseButton.Right ? EditorTool.Erase : Tool;
                PaintAt(press.Position);
                AcceptEvent();
                break;
            case InputEventMouseButton { Pressed: false, ButtonIndex: MouseButton.Left or MouseButton.Right }:
                _painting = false;
                break;
            case InputEventMouseMotion motion:
                var cell = CellAt(motion.Position);
                if (cell != _hover)
                {
                    _hover = cell;
                    QueueRedraw();
                }
                if (_painting) PaintAt(motion.Position);
                break;
        }
    }

    public override void _Notification(int what)
    {
        if (what == NotificationMouseExit)
        {
            _hover = null;
            _painting = false;
            QueueRedraw();
        }
        else if (what == (int)NotificationResized)
        {
            QueueRedraw();
        }
    }

    private void PaintAt(Vector2 position)
    {
        if (CellAt(position) is not { } cell) return;
        if (!Draft.Paint(cell.X, cell.Y, _strokeTool)) return;
        QueueRedraw();
        Changed?.Invoke();
    }

    private (float Tile, Vector2 Origin) Layout()
    {
        var fit = MathF.Min((Size.X - 16) / Draft.Width, (Size.Y - 16) / Draft.Height);
        var tile = Math.Clamp(MathF.Floor(fit / 4) * 4, 12, 48);
        var origin = new Vector2(UiScale.Snap((Size.X - tile * Draft.Width) / 2), UiScale.Snap((Size.Y - tile * Draft.Height) / 2));
        return (tile, origin);
    }

    private Vector2I? CellAt(Vector2 position)
    {
        var (tile, origin) = Layout();
        var local = (position - origin) / tile;
        var x = (int)MathF.Floor(local.X);
        var y = (int)MathF.Floor(local.Y);
        return x >= 0 && y >= 0 && x < Draft.Width && y < Draft.Height ? new Vector2I(x, y) : null;
    }

    public override void _Draw()
    {
        var p = Palette.Current;
        var line = UiScale.Line;
        DrawRect(new Rect2(Vector2.Zero, Size), Background);
        DrawRect(new Rect2(0, 0, Size.X, line), p.Shadow);
        DrawRect(new Rect2(0, 0, line, Size.Y), p.Shadow);
        DrawRect(new Rect2(0, Size.Y - line, Size.X, line), p.Highlight);
        DrawRect(new Rect2(Size.X - line, 0, line, Size.Y), p.Highlight);

        var (tile, origin) = Layout();
        var scale = tile / 16f;
        var floor = Icons.Get("tile_floor", scale);
        var wall = Icons.Get("tile_wall", scale);
        var goal = Icons.Get("tile_goal", scale);
        var box = Icons.Get("tile_box", scale);
        var boxDone = Icons.Get("tile_box_goal", scale);
        var player = Icons.Get("tile_player", scale);

        DrawRect(new Rect2(origin, tile * Draft.Width, tile * Draft.Height), Outside);
        for (var y = 0; y < Draft.Height; y++)
        {
            for (var x = 0; x < Draft.Width; x++)
            {
                var r = new Rect2(origin + new Vector2(x * tile, y * tile), tile, tile);
                var c = Draft.Get(x, y);
                DrawTextureRect(c == '#' ? wall : floor, r, false);
                if (c is '.' or '+') DrawTextureRect(goal, r, false);
                if (c == '$') DrawTextureRect(box, r, false);
                if (c == '*') DrawTextureRect(boxDone, r, false);
                if (c is '@' or '+') DrawTextureRect(player, r, false);
            }
        }

        // feines Raster zum Zielen
        for (var x = 0; x <= Draft.Width; x++)
            DrawRect(new Rect2(origin.X + x * tile, origin.Y, line, tile * Draft.Height), Grid);
        for (var y = 0; y <= Draft.Height; y++)
            DrawRect(new Rect2(origin.X, origin.Y + y * tile, tile * Draft.Width, line), Grid);

        if (_hover is { } h)
            DrawRect(new Rect2(origin + new Vector2(h.X * tile, h.Y * tile), tile, tile), new Color("#ffe07a"), false, UiScale.Line * 2);
    }
}
