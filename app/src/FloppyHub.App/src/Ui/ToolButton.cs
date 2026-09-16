using FloppyHub.App.Art;
using FloppyHub.App.Skin;
using Godot;

namespace FloppyHub.App.Ui;

/// <summary>Grosser Werkzeugknopf: 32-px-Icon, Beschriftung darunter (WinRAR-Stil).</summary>
public partial class ToolButton : Button
{
    public ToolButton() : this("", "floppy") { }

    public ToolButton(string text, string icon, string? tooltip = null)
    {
        ThemeTypeVariation = "ToolButton";
        Text = text;
        Icon = Icons.Get(icon);
        IconAlignment = HorizontalAlignment.Center;
        VerticalIconAlignment = VerticalAlignment.Top;
        FocusMode = FocusModeEnum.None;
        TooltipText = tooltip ?? "";
        CustomMinimumSize = new Vector2(64, 0);
        AutoTranslateMode = AutoTranslateModeEnum.Disabled;
        AddThemeFontSizeOverride("font_size", SkinBuilder.SmallFontSize + 1);
        AddThemeConstantOverride("h_separation", 3);
    }
}
