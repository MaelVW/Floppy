using Godot;

namespace FloppyHub.App.Skin;

/// <summary>Eingeritzte Trennlinie (dunkel + hell), wie in klassischen Menues.</summary>
public partial class SeparatorStyle : StyleBox
{
    public Palette Palette { get; set; } = Palette.Current;
    public bool Vertical { get; set; }

    public override void _Draw(Rid canvas, Rect2 rect)
    {
        var line = UiScale.Line;
        if (Vertical)
        {
            var x = UiScale.Snap(rect.Position.X + rect.Size.X / 2 - line);
            RenderingServer.CanvasItemAddRect(canvas, new Rect2(x, rect.Position.Y, line, rect.Size.Y), Palette.Shadow);
            RenderingServer.CanvasItemAddRect(canvas, new Rect2(x + line, rect.Position.Y, line, rect.Size.Y), Palette.Highlight);
        }
        else
        {
            var y = UiScale.Snap(rect.Position.Y + rect.Size.Y / 2 - line);
            RenderingServer.CanvasItemAddRect(canvas, new Rect2(rect.Position.X, y, rect.Size.X, line), Palette.Shadow);
            RenderingServer.CanvasItemAddRect(canvas, new Rect2(rect.Position.X, y + line, rect.Size.X, line), Palette.Highlight);
        }
    }

    public override Vector2 _GetMinimumSize() => new(UiScale.Line * 2, UiScale.Line * 2);
}
