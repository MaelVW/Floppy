using Godot;

namespace FloppyHub.App.Skin;

/// <summary>
/// Farben der Oberflaeche. Hell = Windows 2000/XP-Werkzeug wie WinRAR,
/// Dunkel = Audacity/VLC-dunkel. Gleiche Formen, andere Farben.
/// </summary>
public sealed record Palette
{
    public required bool Dark { get; init; }
    public required string Name { get; init; }

    public required Color Window { get; init; }
    public required Color FaceTop { get; init; }
    public required Color FaceBottom { get; init; }
    public required Color Highlight { get; init; }
    public required Color Light { get; init; }
    public required Color Shadow { get; init; }
    public required Color DarkShadow { get; init; }

    public required Color Field { get; init; }
    public required Color FieldAlt { get; init; }

    public required Color Text { get; init; }
    public required Color TextDim { get; init; }
    public required Color TextDisabled { get; init; }

    public required Color Selection { get; init; }
    public required Color SelectionText { get; init; }
    public required Color SelectionSoft { get; init; }
    public required Color SelectionUnfocused { get; init; }

    public required Color TitleA { get; init; }
    public required Color TitleB { get; init; }
    public required Color TitleText { get; init; }

    public required Color ToolbarTop { get; init; }
    public required Color ToolbarBottom { get; init; }

    public required Color Accent { get; init; }
    public required Color Ok { get; init; }
    public required Color Warn { get; init; }
    public required Color Error { get; init; }

    public required Color Tooltip { get; init; }
    public required Color TooltipText { get; init; }

    public Color Face => FaceTop.Lerp(FaceBottom, 0.5f);

    public static Palette Classic { get; } = new()
    {
        Dark = false,
        Name = "light",
        Window = new("#d8d5cf"),
        FaceTop = new("#f4f2ee"),
        FaceBottom = new("#d0ccc4"),
        Highlight = new("#ffffff"),
        Light = new("#e8e6e1"),
        Shadow = new("#8e8a82"),
        DarkShadow = new("#3c3a36"),
        Field = new("#ffffff"),
        FieldAlt = new("#f3f1ed"),
        Text = new("#141414"),
        TextDim = new("#5c5850"),
        TextDisabled = new("#9f9b93"),
        Selection = new("#2f5fb3"),
        SelectionText = new("#ffffff"),
        SelectionSoft = new("#cddcf3"),
        SelectionUnfocused = new("#b9b5ad"),
        TitleA = new("#0a246a"),
        TitleB = new("#5b8bc9"),
        TitleText = new("#ffffff"),
        ToolbarTop = new("#fbfaf8"),
        ToolbarBottom = new("#d9d5cd"),
        Accent = new("#2f5fb3"),
        Ok = new("#2e9e3e"),
        Warn = new("#c98208"),
        Error = new("#c0392b"),
        Tooltip = new("#ffffe1"),
        TooltipText = new("#000000"),
    };

    public static Palette Midnight { get; } = new()
    {
        Dark = true,
        Name = "dark",
        Window = new("#2d2f33"),
        FaceTop = new("#4b4e54"),
        FaceBottom = new("#37393d"),
        Highlight = new("#6b6e75"),
        Light = new("#515459"),
        Shadow = new("#1c1d20"),
        DarkShadow = new("#0d0e0f"),
        Field = new("#1e1f22"),
        FieldAlt = new("#25272a"),
        Text = new("#e9e9e6"),
        TextDim = new("#a6a9ad"),
        TextDisabled = new("#6a6d72"),
        Selection = new("#3d6db5"),
        SelectionText = new("#ffffff"),
        SelectionSoft = new("#33445c"),
        SelectionUnfocused = new("#4a4d52"),
        TitleA = new("#18305a"),
        TitleB = new("#3f6eab"),
        TitleText = new("#ffffff"),
        ToolbarTop = new("#3d4045"),
        ToolbarBottom = new("#2b2d30"),
        Accent = new("#5b8fdb"),
        Ok = new("#4cbb5e"),
        Warn = new("#e2a63a"),
        Error = new("#e25a4b"),
        Tooltip = new("#3b3c31"),
        TooltipText = new("#f1f1dc"),
    };

    public static Palette Current { get; set; } = Classic;
}
