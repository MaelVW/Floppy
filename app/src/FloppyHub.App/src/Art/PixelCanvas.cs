using Godot;

namespace FloppyHub.App.Art;

/// <summary>Kleine Pixel-Zeichenflaeche fuer die Platzhalter-Icons (keine Kantenglaettung).</summary>
public sealed class PixelCanvas
{
    public PixelCanvas(int width, int height)
    {
        Width = width;
        Height = height;
        Image = Image.CreateEmpty(width, height, false, Image.Format.Rgba8);
        Image.Fill(Colors.Transparent);
    }

    public Image Image { get; }
    public int Width { get; }
    public int Height { get; }

    public void Px(int x, int y, Color c)
    {
        if (x < 0 || y < 0 || x >= Width || y >= Height) return;
        if (c.A >= 1f) Image.SetPixel(x, y, c);
        else Image.SetPixel(x, y, Image.GetPixel(x, y).Blend(c));
    }

    /// <summary>Pixel wieder durchsichtig machen.</summary>
    public void Clear(int x, int y)
    {
        if (x >= 0 && y >= 0 && x < Width && y < Height) Image.SetPixel(x, y, Colors.Transparent);
    }

    /// <summary>Kreisrundes Loch stanzen.</summary>
    public void Punch(float cx, float cy, float r)
    {
        for (var y = (int)(cy - r - 1); y <= (int)(cy + r + 1); y++)
        for (var x = (int)(cx - r - 1); x <= (int)(cx + r + 1); x++)
        {
            float dx = x + 0.5f - cx, dy = y + 0.5f - cy;
            if (dx * dx + dy * dy <= r * r) Clear(x, y);
        }
    }

    public Color Get(int x, int y) =>
        x < 0 || y < 0 || x >= Width || y >= Height ? Colors.Transparent : Image.GetPixel(x, y);

    public void Rect(int x, int y, int w, int h, Color c)
    {
        for (var j = y; j < y + h; j++)
        for (var i = x; i < x + w; i++)
            Px(i, j, c);
    }

    public void Box(int x, int y, int w, int h, Color c)
    {
        HLine(x, y, w, c);
        HLine(x, y + h - 1, w, c);
        VLine(x, y, h, c);
        VLine(x + w - 1, y, h, c);
    }

    public void HLine(int x, int y, int length, Color c)
    {
        for (var i = 0; i < length; i++) Px(x + i, y, c);
    }

    public void VLine(int x, int y, int length, Color c)
    {
        for (var i = 0; i < length; i++) Px(x, y + i, c);
    }

    /// <summary>Bresenham-Linie.</summary>
    public void Line(int x0, int y0, int x1, int y1, Color c)
    {
        int dx = Math.Abs(x1 - x0), sx = x0 < x1 ? 1 : -1;
        int dy = -Math.Abs(y1 - y0), sy = y0 < y1 ? 1 : -1;
        var err = dx + dy;
        while (true)
        {
            Px(x0, y0, c);
            if (x0 == x1 && y0 == y1) break;
            var e2 = 2 * err;
            if (e2 >= dy) { err += dy; x0 += sx; }
            if (e2 <= dx) { err += dx; y0 += sy; }
        }
    }

    /// <summary>Gefuellter Kreis (Mittelpunkt kann halbzahlig sein).</summary>
    public void Disc(float cx, float cy, float r, Color c)
    {
        for (var y = (int)(cy - r - 1); y <= (int)(cy + r + 1); y++)
        for (var x = (int)(cx - r - 1); x <= (int)(cx + r + 1); x++)
        {
            float dx = x + 0.5f - cx, dy = y + 0.5f - cy;
            if (dx * dx + dy * dy <= r * r) Px(x, y, c);
        }
    }

    public void Ring(float cx, float cy, float r, float thickness, Color c)
    {
        for (var y = (int)(cy - r - 1); y <= (int)(cy + r + 1); y++)
        for (var x = (int)(cx - r - 1); x <= (int)(cx + r + 1); x++)
        {
            float dx = x + 0.5f - cx, dy = y + 0.5f - cy;
            var d = MathF.Sqrt(dx * dx + dy * dy);
            if (d <= r && d > r - thickness) Px(x, y, c);
        }
    }

    public void VGradient(int x, int y, int w, int h, Color top, Color bottom)
    {
        for (var j = 0; j < h; j++)
            HLine(x, y + j, w, top.Lerp(bottom, h <= 1 ? 0 : j / (float)(h - 1)));
    }

    /// <summary>Plastische Kante innen: hell oben/links, dunkel unten/rechts.</summary>
    public void Bevel(int x, int y, int w, int h, Color light, Color dark)
    {
        HLine(x, y, w - 1, light);
        VLine(x, y, h - 1, light);
        HLine(x, y + h - 1, w, dark);
        VLine(x + w - 1, y, h, dark);
    }

    public void Polygon(Color c, params Vector2I[] points)
    {
        int minY = points.Min(p => p.Y), maxY = points.Max(p => p.Y);
        for (var y = minY; y <= maxY; y++)
        {
            var xs = new List<float>();
            for (var i = 0; i < points.Length; i++)
            {
                var a = points[i];
                var b = points[(i + 1) % points.Length];
                if (a.Y == b.Y) continue;
                var yy = y + 0.5f;
                if ((yy >= a.Y && yy < b.Y) || (yy >= b.Y && yy < a.Y))
                    xs.Add(a.X + (yy - a.Y) * (b.X - a.X) / (float)(b.Y - a.Y));
            }
            xs.Sort();
            for (var i = 0; i + 1 < xs.Count; i += 2)
                for (var x = (int)MathF.Round(xs[i]); x < (int)MathF.Round(xs[i + 1]); x++)
                    Px(x, y, c);
        }
    }

    /// <summary>1-px-Umriss um alles Gezeichnete - macht Icons "knackig".</summary>
    public void Outline(Color c)
    {
        var copy = (Image)Image.Duplicate();
        for (var y = 0; y < Height; y++)
        for (var x = 0; x < Width; x++)
        {
            if (copy.GetPixel(x, y).A > 0) continue;
            var near = false;
            for (var oy = -1; oy <= 1 && !near; oy++)
            for (var ox = -1; ox <= 1 && !near; ox++)
            {
                if (Math.Abs(ox) + Math.Abs(oy) != 1) continue;
                int nx = x + ox, ny = y + oy;
                near = nx >= 0 && ny >= 0 && nx < Width && ny < Height && copy.GetPixel(nx, ny).A > 0.5f;
            }
            if (near) Image.SetPixel(x, y, c);
        }
    }

    /// <summary>Weicher Schlagschatten nach rechts unten (XP-Stil).</summary>
    public void DropShadow(Color c)
    {
        var copy = (Image)Image.Duplicate();
        for (var y = Height - 1; y >= 1; y--)
        for (var x = Width - 1; x >= 1; x--)
        {
            if (copy.GetPixel(x, y).A > 0) continue;
            if (copy.GetPixel(x - 1, y - 1).A > 0.5f) Image.SetPixel(x, y, c);
        }
    }
}
