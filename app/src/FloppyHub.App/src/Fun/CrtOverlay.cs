using FloppyHub.App.Skin;
using Godot;

namespace FloppyHub.App.Fun;

/// <summary>Gruener Roehrenmonitor mit Bildzeilen - verschwindet nach ein paar Sekunden.</summary>
public partial class CrtOverlay : Control
{
    public CrtOverlay() : this(10) { }

    public CrtOverlay(double seconds)
    {
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        var tint = new ColorRect
        {
            Color = new Color(0.45f, 1f, 0.5f),
            MouseFilter = MouseFilterEnum.Ignore,
            Material = new CanvasItemMaterial { BlendMode = CanvasItemMaterial.BlendModeEnum.Mul },
        };
        tint.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(tint);

        Ready += () =>
        {
            var tween = CreateTween();
            tween.TweenInterval(seconds);
            tween.TweenProperty(this, "modulate:a", 0f, 1.2f);
            tween.TweenCallback(Callable.From(QueueFree));
        };
    }

    public override void _Draw()
    {
        // jede zweite echte Bildschirmzeile etwas dunkler
        var step = 2f / UiScale.Factor;
        var line = 1f / UiScale.Factor;
        var shade = new Color(0, 0.08f, 0, 0.22f);
        for (var y = 0f; y < Size.Y; y += step)
            DrawRect(new Rect2(0, y, Size.X, line), shade);
    }

    public override void _Notification(int what)
    {
        if (what == NotificationResized) QueueRedraw();
    }
}
