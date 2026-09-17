using FloppyHub.App.Skin;
using Godot;

namespace FloppyHub.App.Ui;

/// <summary>
/// 3D-Kuchendiagramm "belegt/frei" wie im Eigenschaften-Dialog von Windows XP
/// (blau = belegt, magenta = frei).
/// </summary>
public partial class CapacityPie : Control
{
    public static readonly Color UsedColor = new("#3b5fd0");
    public static readonly Color FreeColor = new("#d34fd3");

    private const int Segments = 96;
    private double _ratio;
    private bool _empty = true;

    public CapacityPie()
    {
        CustomMinimumSize = new Vector2(200, 120);
        MouseFilter = MouseFilterEnum.Ignore;
    }

    /// <summary>Belegter Anteil 0..1; <c>null</c> = kein Datentraeger (graue Scheibe).</summary>
    public void SetRatio(double? usedRatio)
    {
        _empty = usedRatio is null;
        _ratio = Math.Clamp(usedRatio ?? 0, 0, 1);
        QueueRedraw();
    }

    public override void _Draw()
    {
        var depth = MathF.Round(Size.Y * 0.14f);
        var rx = Size.X / 2f - 2;
        var ry = (Size.Y - depth) / 2f - 2;
        var center = new Vector2(Size.X / 2f, ry + 2);

        var p = Palette.Current;
        Color used = _empty ? p.Shadow : UsedColor;
        Color free = _empty ? p.Light : FreeColor;

        // Winkel: belegt beginnt oben (-90 Grad) und laeuft im Uhrzeigersinn
        var split = -MathF.PI / 2 + (float)(_ratio * Math.PI * 2);
        Color ColorAt(float angle, bool side)
        {
            var a = Normalize(angle);
            var isUsed = !_empty && _ratio > 0 && (a < Normalize(split) || _ratio >= 1);
            var c = isUsed ? used : free;
            return side ? c.Darkened(0.42f) : c;
        }

        Vector2 Point(float angle, float dy = 0) =>
            center + new Vector2(MathF.Cos(angle) * rx, MathF.Sin(angle) * ry + dy);

        // Seitenwand (nur vordere Haelfte sichtbar: Winkel 0..PI)
        for (var i = 0; i < Segments / 2; i++)
        {
            var a0 = MathF.PI * i / (Segments / 2f);
            var a1 = MathF.PI * (i + 1) / (Segments / 2f);
            var mid = (a0 + a1) / 2;
            DrawPolygon([Point(a0), Point(a1), Point(a1, depth), Point(a0, depth)], [ColorAt(mid, side: true)]);
        }

        // Oberseite als Faecher
        for (var i = 0; i < Segments; i++)
        {
            var a0 = -MathF.PI / 2 + MathF.Tau * i / Segments;
            var a1 = -MathF.PI / 2 + MathF.Tau * (i + 1) / Segments;
            DrawPolygon([center, Point(a0), Point(a1)], [ColorAt((a0 + a1) / 2, side: false)]);
        }

        // Kanten
        var edge = p.DarkShadow with { A = 0.55f };
        var rim = new Vector2[Segments + 1];
        for (var i = 0; i <= Segments; i++) rim[i] = Point(MathF.Tau * i / Segments);
        DrawPolyline(rim, edge, 1f / UiScale.Factor);
        var bottom = new Vector2[Segments / 2 + 1];
        for (var i = 0; i <= Segments / 2; i++) bottom[i] = Point(MathF.PI * i / (Segments / 2f), depth);
        DrawPolyline(bottom, edge, 1f / UiScale.Factor);
        DrawLine(Point(0), Point(0, depth), edge, 1f / UiScale.Factor);
        DrawLine(Point(MathF.PI), Point(MathF.PI, depth), edge, 1f / UiScale.Factor);
        if (!_empty && _ratio is > 0 and < 1)
        {
            DrawLine(center, Point(-MathF.PI / 2), edge, 1f / UiScale.Factor);
            DrawLine(center, Point(split), edge, 1f / UiScale.Factor);
        }
    }

    /// <summary>Winkel relativ zum Start (oben) auf 0..2PI bringen.</summary>
    private static float Normalize(float angle)
    {
        var a = (angle + MathF.PI / 2) % MathF.Tau;
        return a < 0 ? a + MathF.Tau : a;
    }
}
