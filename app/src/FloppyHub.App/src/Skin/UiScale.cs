using Godot;

namespace FloppyHub.App.Skin;

/// <summary>
/// Skalierung der Oberflaeche (Windows-Anzeige "150 %" usw.).
/// Linien werden auf echte Bildschirmpixel gelegt, damit Kanten scharf bleiben.
/// </summary>
public static class UiScale
{
    public static float Factor { get; private set; } = 1f;

    /// <summary>So viele echte Pixel ist eine Bevel-Linie dick (1 bei 100 %, 2 ab 150 %).</summary>
    public static int LinePixels => Math.Max(1, (int)MathF.Round(Factor, MidpointRounding.AwayFromZero));

    /// <summary>Eine Bevel-Linie in logischen Einheiten.</summary>
    public static float Line => LinePixels / Factor;

    /// <summary>Ganzzahliger Vergroesserungsfaktor fuer Pixel-Art (danach weich auf Zielgroesse).</summary>
    public static int ArtFactor(float displayMultiplier = 1f) => Math.Max(1, (int)MathF.Ceiling(Factor * displayMultiplier - 0.01f));

    public static float Detect()
    {
        var dpi = DisplayServer.ScreenGetDpi();
        var raw = dpi > 0 ? dpi / 96f : 1f;
        return MathF.Round(raw * 4f) / 4f;   // auf 25-%-Schritte
    }

    public static void Apply(Window window, float factor)
    {
        Factor = Math.Clamp(factor <= 0 ? Detect() : factor, 1f, 3f);
        window.ContentScaleFactor = Factor;
    }

    /// <summary>Logischen Wert auf das Pixelraster legen.</summary>
    public static float Snap(float v) => MathF.Round(v * Factor) / Factor;
}
