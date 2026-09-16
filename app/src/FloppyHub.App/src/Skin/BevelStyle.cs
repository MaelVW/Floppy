using Godot;

namespace FloppyHub.App.Skin;

/// <summary>
/// Selbst gezeichnete Flaeche im Stil klassischer Windows-Programme:
/// Verlauf + zweistufige Kante (Bevel), auf echte Bildschirmpixel gelegt.
/// Eine Klasse fuer Knoepfe, Felder, Werkzeugleiste, Statusleiste usw.
/// </summary>
public partial class BevelStyle : StyleBox
{
    public enum Edge
    {
        /// <summary>Keine Kante.</summary>
        None,
        /// <summary>Erhaben, 2 Stufen (Knopf).</summary>
        Raised,
        /// <summary>Gedrueckt, 2 Stufen.</summary>
        Pressed,
        /// <summary>Eingelassen, 2 Stufen (Eingabefeld, Liste).</summary>
        Sunken,
        /// <summary>Erhaben, 1 Stufe (Werkzeugknopf beim Ueberfahren, Menue).</summary>
        ThinRaised,
        /// <summary>Eingelassen, 1 Stufe (Statusleisten-Feld).</summary>
        ThinSunken,
        /// <summary>Rille (Gruppenrahmen, Trennlinie).</summary>
        Etched,
        /// <summary>Einfacher 1-px-Rand in <see cref="BorderColor"/>.</summary>
        Border,
    }

    public Edge Kind { get; set; } = Edge.Raised;
    public Color FillTop { get; set; } = Colors.Transparent;
    public Color FillBottom { get; set; } = Colors.Transparent;

    /// <summary>true = Verlauf von links nach rechts (Titelleiste).</summary>
    public bool Horizontal { get; set; }

    public Color BorderColor { get; set; } = Colors.Black;

    /// <summary>Ecken um ein Pixel abschneiden - der "moderne" Touch.</summary>
    public bool CutCorners { get; set; }

    /// <summary>Zusaetzliche Linie unten (Werkzeugleiste / Menueleiste).</summary>
    public bool BottomRule { get; set; }

    /// <summary>Akzentlinie innen (Fokus).</summary>
    public Color FocusColor { get; set; } = Colors.Transparent;

    /// <summary>Balken in Bloecken fuellen (Fortschrittsanzeige wie XP/Audacity).</summary>
    public bool Segmented { get; set; }

    public Palette Palette { get; set; } = Palette.Current;

    public override void _Draw(Rid canvas, Rect2 rect)
    {
        var p = Palette;
        var line = UiScale.Line;
        rect = SnapRect(rect);
        if (rect.Size.X <= 0 || rect.Size.Y <= 0) return;

        // --- Flaeche ---
        if (FillTop.A > 0 || FillBottom.A > 0)
        {
            var inset = Kind is Edge.None ? 0 : line;
            var fill = rect.Grow(-inset);
            if (Segmented) DrawSegments(canvas, fill);
            else DrawGradient(canvas, fill, FillTop, FillBottom, Horizontal);
        }

        // --- Kante ---
        switch (Kind)
        {
            case Edge.Raised:
                Frame(canvas, rect, p.Highlight, p.DarkShadow);
                Frame(canvas, rect.Grow(-line), p.Light, p.Shadow);
                break;
            case Edge.Pressed:
                Frame(canvas, rect, p.DarkShadow, p.Highlight);
                Frame(canvas, rect.Grow(-line), p.Shadow, p.Light);
                break;
            case Edge.Sunken:
                Frame(canvas, rect, p.Shadow, p.Highlight);
                Frame(canvas, rect.Grow(-line), p.DarkShadow, p.Light);
                break;
            case Edge.ThinRaised:
                Frame(canvas, rect, p.Highlight, p.Shadow);
                break;
            case Edge.ThinSunken:
                Frame(canvas, rect, p.Shadow, p.Highlight);
                break;
            case Edge.Etched:
                Frame(canvas, rect, p.Shadow, p.Highlight);
                Frame(canvas, rect.Grow(-line), p.Highlight, p.Shadow);
                break;
            case Edge.Border:
                Frame(canvas, rect, BorderColor, BorderColor);
                break;
        }

        if (FocusColor.A > 0)
        {
            var f = rect.Grow(-line * 3);
            Frame(canvas, f, FocusColor, FocusColor);
        }

        if (BottomRule)
        {
            var y = rect.End.Y - line * 2;
            RenderingServer.CanvasItemAddRect(canvas, new Rect2(rect.Position.X, y, rect.Size.X, line), p.Shadow);
            RenderingServer.CanvasItemAddRect(canvas, new Rect2(rect.Position.X, y + line, rect.Size.X, line), p.Highlight);
        }

        if (CutCorners && Kind is Edge.Raised or Edge.Pressed or Edge.Sunken)
        {
            var bg = p.Window;
            foreach (var corner in new[]
                     {
                         rect.Position, new Vector2(rect.End.X - line, rect.Position.Y),
                         new Vector2(rect.Position.X, rect.End.Y - line), rect.End - new Vector2(line, line),
                     })
                RenderingServer.CanvasItemAddRect(canvas, new Rect2(corner, new Vector2(line, line)), bg);
        }
    }

    private void DrawSegments(Rid canvas, Rect2 r)
    {
        var line = UiScale.Line;
        var block = UiScale.Snap(MathF.Max(6, r.Size.Y * 0.7f));
        var gap = line * 2;
        for (var x = r.Position.X + gap; x + block <= r.End.X; x += block + gap)
            DrawGradient(canvas, new Rect2(x, r.Position.Y + gap, block, r.Size.Y - gap * 2), FillTop, FillBottom, false);
    }

    private static void Frame(Rid canvas, Rect2 r, Color topLeft, Color bottomRight)
    {
        var t = UiScale.Line;
        // oben + links
        RenderingServer.CanvasItemAddRect(canvas, new Rect2(r.Position.X, r.Position.Y, r.Size.X - t, t), topLeft);
        RenderingServer.CanvasItemAddRect(canvas, new Rect2(r.Position.X, r.Position.Y + t, t, r.Size.Y - t * 2), topLeft);
        // unten + rechts
        RenderingServer.CanvasItemAddRect(canvas, new Rect2(r.Position.X, r.End.Y - t, r.Size.X, t), bottomRight);
        RenderingServer.CanvasItemAddRect(canvas, new Rect2(r.End.X - t, r.Position.Y, t, r.Size.Y - t), bottomRight);
    }

    internal static void DrawGradient(Rid canvas, Rect2 r, Color a, Color b, bool horizontal)
    {
        if (r.Size.X <= 0 || r.Size.Y <= 0) return;
        if (a == b)
        {
            RenderingServer.CanvasItemAddRect(canvas, r, a);
            return;
        }
        var pts = new[] { r.Position, new Vector2(r.End.X, r.Position.Y), r.End, new Vector2(r.Position.X, r.End.Y) };
        var cols = horizontal ? new[] { a, b, b, a } : new[] { a, a, b, b };
        RenderingServer.CanvasItemAddPolygon(canvas, pts, cols);
    }

    private static Rect2 SnapRect(Rect2 r)
    {
        var pos = new Vector2(UiScale.Snap(r.Position.X), UiScale.Snap(r.Position.Y));
        var end = new Vector2(UiScale.Snap(r.End.X), UiScale.Snap(r.End.Y));
        return new Rect2(pos, end - pos);
    }

    // ------------------------------------------------------------------
    // Fabrik-Methoden
    // ------------------------------------------------------------------

    public static BevelStyle Make(Edge kind, Color top, Color? bottom = null, float padX = 6, float padY = 3)
    {
        var s = new BevelStyle { Kind = kind, FillTop = top, FillBottom = bottom ?? top, Palette = Palette.Current };
        s.ContentMarginLeft = padX;
        s.ContentMarginRight = padX;
        s.ContentMarginTop = padY;
        s.ContentMarginBottom = padY;
        return s;
    }
}
