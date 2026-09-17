using FloppyHub.App.Skin;
using Godot;

namespace FloppyHub.App.Art;

/// <summary>
/// Liefert Icons als Textur. Reihenfolge: eigene PNG in res://assets/icons/ (Maels
/// Grafiken) -&gt; sonst Platzhalter aus <see cref="IconForge"/>.
/// Pixel-Art wird ganzzahlig vergroessert und dann weich auf die Anzeigegroesse
/// gebracht - so bleibt sie auch bei 150 % Windows-Skalierung gleichmaessig scharf.
/// </summary>
public static class Icons
{
    public const string IconDir = "res://assets/icons";

    private static readonly Dictionary<string, Texture2D> Cache = new();

    public static void ClearCache() => Cache.Clear();

    /// <param name="name">Icon-Name (siehe <see cref="IconForge.All"/>).</param>
    /// <param name="display">Anzeige-Vielfaches der Grundgroesse (z. B. 3 = dreimal so gross).</param>
    public static Texture2D Get(string name, float display = 1f)
    {
        var def = IconForge.All.GetValueOrDefault(name);
        var baseSize = def?.Size ?? 16;
        var logical = Mathf.RoundToInt(baseSize * display);
        var physical = Mathf.CeilToInt(logical * UiScale.Factor);
        var key = $"{name}|{logical}|{physical}|{(def?.Glyph == true ? Palette.Current.Name : "")}";
        if (Cache.TryGetValue(key, out var cached)) return cached;

        var image = (def is { Glyph: false } ? LoadCustom(name) : null) ?? IconForge.Draw(name, Palette.Current);

        // ganzzahlig hochskalieren (Nearest), bis mindestens die echte Pixelgroesse erreicht ist
        var factor = Math.Max(1, Mathf.CeilToInt(physical / (float)image.GetWidth() - 0.001f));
        if (factor > 1)
        {
            image = (Image)image.Duplicate();
            image.Resize(image.GetWidth() * factor, image.GetHeight() * factor, Image.Interpolation.Nearest);
        }

        var texture = ImageTexture.CreateFromImage(image);
        var height = Mathf.RoundToInt(logical * image.GetHeight() / (float)image.GetWidth());
        texture.SetSizeOverride(new Vector2I(logical, height));
        Cache[key] = texture;
        return texture;
    }

    /// <summary>Icon so skaliert, dass es <paramref name="logicalSize"/> Einheiten breit ist (16er doppelt, 32er einfach ...).</summary>
    public static Texture2D GetSized(string name, int logicalSize)
    {
        var baseSize = IconForge.All.GetValueOrDefault(name)?.Size ?? 16;
        return Get(name, logicalSize / (float)baseSize);
    }

    private static Image? LoadCustom(string name)
    {
        var path = $"{IconDir}/{name}.png";
        try
        {
            if (ResourceLoader.Exists(path))
                return GD.Load<Texture2D>(path)?.GetImage();
        }
        catch
        {
            // kaputte eigene Grafik: Platzhalter nehmen
        }
        return null;
    }

    /// <summary>TextureRect fuer ein Icon in fester logischer Groesse.</summary>
    public static TextureRect Rect(string name, float display = 1f)
    {
        var tex = Get(name, display);
        return new TextureRect
        {
            Texture = tex,
            CustomMinimumSize = tex.GetSize(),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            TextureFilter = CanvasItem.TextureFilterEnum.Linear,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
    }
}
