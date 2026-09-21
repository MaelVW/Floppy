using FloppyHub.App.Art;
using Godot;
using static FloppyHub.App.Skin.BevelStyle;

namespace FloppyHub.App.Skin;

/// <summary>
/// Baut das Godot-Theme aus einer <see cref="Palette"/>. Hell und Dunkel
/// unterscheiden sich nur in den Farben - die Formen sind identisch.
/// </summary>
public static class SkinBuilder
{
    public const int FontSize = 12;
    public const int SmallFontSize = 11;
    public const int TitleFontSize = 15;

    public static Font UiFont { get; private set; } = null!;
    public static Font BoldFont { get; private set; } = null!;

    public static Theme Build(Palette p)
    {
        Palette.Current = p;
        UiFont ??= MakeFont(400);
        BoldFont ??= MakeFont(700);

        var t = new Theme { DefaultFont = UiFont, DefaultFontSize = FontSize };

        Button(t, p);
        Fields(t, p);
        Lists(t, p);
        Menus(t, p);
        Scrollbars(t, p);
        Misc(t, p);
        return t;
    }

    private static SystemFont MakeFont(int weight) => new()
    {
        // Tahoma = typische UI-Schrift der 2000er (WinRAR, Audacity unter Windows).
        FontNames = ["Tahoma", "Segoe UI", "Verdana", "Arial"],
        FontWeight = weight,
        Antialiasing = TextServer.FontAntialiasing.Gray,
        Hinting = TextServer.Hinting.Normal,
        SubpixelPositioning = TextServer.SubpixelPositioning.Disabled,
        GenerateMipmaps = false,
    };

    // ------------------------------------------------------------------

    private static void Button(Theme t, Palette p)
    {
        var normal = Make(Edge.Raised, p.FaceTop, p.FaceBottom, 10, 4);
        normal.CutCorners = true;
        var hover = Make(Edge.Raised, p.FaceTop.Lerp(p.Highlight, 0.6f), p.FaceBottom.Lerp(p.FaceTop, 0.35f), 10, 4);
        hover.CutCorners = true;
        var pressed = Make(Edge.Pressed, p.FaceBottom, p.FaceTop, 10, 4);
        pressed.CutCorners = true;
        // klassisch: Inhalt rutscht beim Druecken - Gesamtgroesse bleibt gleich
        pressed.ContentMarginLeft += 1;
        pressed.ContentMarginRight -= 1;
        pressed.ContentMarginTop += 1;
        pressed.ContentMarginBottom -= 1;
        var disabled = Make(Edge.Raised, p.Face, p.Face, 10, 4);
        disabled.CutCorners = true;
        var focus = new BevelStyle { Kind = Edge.None, FocusColor = p.Accent.Lerp(p.Face, 0.35f), Palette = p };

        foreach (var type in new[] { "Button", "OptionButton", "MenuButton", "ColorPickerButton" })
        {
            t.SetStylebox("normal", type, normal);
            t.SetStylebox("hover", type, hover);
            t.SetStylebox("pressed", type, pressed);
            t.SetStylebox("hover_pressed", type, pressed);
            t.SetStylebox("disabled", type, disabled);
            t.SetStylebox("focus", type, focus);
            t.SetColor("font_color", type, p.Text);
            t.SetColor("font_hover_color", type, p.Text);
            t.SetColor("font_pressed_color", type, p.Text);
            t.SetColor("font_hover_pressed_color", type, p.Text);
            t.SetColor("font_focus_color", type, p.Text);
            t.SetColor("font_disabled_color", type, p.TextDisabled);
            t.SetColor("icon_normal_color", type, Colors.White);
            t.SetColor("icon_disabled_color", type, new Color(1, 1, 1, 0.45f));
            t.SetConstant("h_separation", type, 6);
        }
        t.SetIcon("arrow", "OptionButton", Icons.Get("arrow_down", 1f));
        t.SetConstant("arrow_margin", "OptionButton", 6);

        // Werkzeugknoepfe: flach, erst beim Ueberfahren eine duenne Kante (WinRAR)
        var flat = new StyleBoxEmpty { ContentMarginLeft = 6, ContentMarginRight = 6, ContentMarginTop = 3, ContentMarginBottom = 3 };
        var toolHover = Make(Edge.ThinRaised, p.ToolbarTop.Lerp(p.Highlight, 0.5f), p.ToolbarBottom.Lerp(p.ToolbarTop, 0.4f), 6, 3);
        var toolPressed = Make(Edge.ThinSunken, p.SelectionSoft.Lerp(p.ToolbarBottom, 0.35f), p.SelectionSoft, 6, 3);
        toolPressed.ContentMarginTop += 1;
        toolPressed.ContentMarginBottom -= 1;
        toolPressed.ContentMarginLeft += 1;
        toolPressed.ContentMarginRight -= 1;
        t.SetTypeVariation("ToolButton", "Button");
        t.SetStylebox("normal", "ToolButton", flat);
        t.SetStylebox("hover", "ToolButton", toolHover);
        t.SetStylebox("pressed", "ToolButton", toolPressed);
        t.SetStylebox("hover_pressed", "ToolButton", toolPressed);
        t.SetStylebox("disabled", "ToolButton", flat);
        t.SetStylebox("focus", "ToolButton", new StyleBoxEmpty());

        // CheckBox / CheckButton: Kaestchen als Pixel-Icon
        foreach (var type in new[] { "CheckBox", "CheckButton" })
        {
            var box = new StyleBoxEmpty { ContentMarginLeft = 2, ContentMarginRight = 4, ContentMarginTop = 2, ContentMarginBottom = 2 };
            t.SetStylebox("normal", type, box);
            t.SetStylebox("hover", type, box);
            t.SetStylebox("pressed", type, box);
            t.SetStylebox("hover_pressed", type, box);
            t.SetStylebox("disabled", type, box);
            t.SetStylebox("focus", type, new StyleBoxEmpty());
            t.SetColor("font_color", type, p.Text);
            t.SetColor("font_hover_color", type, p.Text);
            t.SetColor("font_pressed_color", type, p.Text);
            t.SetColor("font_hover_pressed_color", type, p.Text);
            t.SetColor("font_focus_color", type, p.Text);
            t.SetColor("font_disabled_color", type, p.TextDisabled);
        }
        t.SetIcon("checked", "CheckBox", Icons.Get("check_on", 1f));
        t.SetIcon("unchecked", "CheckBox", Icons.Get("check_off", 1f));
        t.SetIcon("checked_disabled", "CheckBox", Icons.Get("check_on", 1f));
        t.SetIcon("unchecked_disabled", "CheckBox", Icons.Get("check_off", 1f));
        t.SetIcon("radio_checked", "CheckBox", Icons.Get("radio_on", 1f));
        t.SetIcon("radio_unchecked", "CheckBox", Icons.Get("radio_off", 1f));
        t.SetIcon("radio_checked_disabled", "CheckBox", Icons.Get("radio_on", 1f));
        t.SetIcon("radio_unchecked_disabled", "CheckBox", Icons.Get("radio_off", 1f));
        t.SetConstant("h_separation", "CheckBox", 5);
    }

    private static void Fields(Theme t, Palette p)
    {
        var field = Make(Edge.Sunken, p.Field, p.Field, 5, 3);
        var focus = Make(Edge.Sunken, p.Field, p.Field, 5, 3);
        var readOnly = Make(Edge.Sunken, p.Window, p.Window, 5, 3);
        foreach (var type in new[] { "LineEdit", "TextEdit", "SpinBox" })
        {
            t.SetStylebox("normal", type, field);
            t.SetStylebox("focus", type, new StyleBoxEmpty());
            t.SetStylebox("read_only", type, readOnly);
            t.SetColor("font_color", type, p.Text);
            t.SetColor("font_readonly_color", type, p.TextDim);
            t.SetColor("font_placeholder_color", type, p.TextDisabled);
            t.SetColor("font_selected_color", type, p.SelectionText);
            t.SetColor("selection_color", type, p.Selection);
            t.SetColor("caret_color", type, p.Text);
            t.SetColor("background_color", type, Colors.Transparent);
        }
        _ = focus;

        // Fortschritt / Belegung: eingelassen, gefuellt in Bloecken
        t.SetStylebox("background", "ProgressBar", Make(Edge.Sunken, p.Field, p.Field, 2, 2));
        var fill = Make(Edge.None, p.Accent.Lightened(0.25f), p.Accent.Darkened(0.1f), 0, 0);
        fill.Segmented = true;
        t.SetStylebox("fill", "ProgressBar", fill);
        t.SetColor("font_color", "ProgressBar", p.Text);
        t.SetColor("font_outline_color", "ProgressBar", p.Field);
    }

    private static void Lists(Theme t, Palette p)
    {
        var panel = Make(Edge.Sunken, p.Field, p.Field, 3, 3);
        var selected = Make(Edge.None, p.Selection, p.Selection, 0, 0);
        var selectedUnfocused = Make(Edge.None, p.SelectionUnfocused, p.SelectionUnfocused, 0, 0);
        var hovered = Make(Edge.None, p.SelectionSoft, p.SelectionSoft, 0, 0);
        var cursor = new StyleBoxEmpty();

        t.SetStylebox("panel", "Tree", panel);
        t.SetStylebox("focus", "Tree", new StyleBoxEmpty());
        t.SetStylebox("selected", "Tree", selectedUnfocused);
        t.SetStylebox("selected_focus", "Tree", selected);
        t.SetStylebox("hovered", "Tree", hovered);
        t.SetStylebox("hovered_dimmed", "Tree", hovered);
        t.SetStylebox("cursor", "Tree", cursor);
        t.SetStylebox("cursor_unfocused", "Tree", cursor);
        t.SetStylebox("title_button_normal", "Tree", Make(Edge.Raised, p.FaceTop, p.FaceBottom, 6, 2));
        t.SetStylebox("title_button_hover", "Tree", Make(Edge.Raised, p.FaceTop.Lerp(p.Highlight, 0.6f), p.FaceBottom, 6, 2));
        t.SetStylebox("title_button_pressed", "Tree", Make(Edge.Pressed, p.FaceBottom, p.FaceTop, 6, 2));
        t.SetColor("font_color", "Tree", p.Text);
        t.SetColor("font_selected_color", "Tree", p.SelectionText);
        t.SetColor("font_hovered_color", "Tree", p.Text);
        t.SetColor("title_button_color", "Tree", p.Text);
        t.SetColor("guide_color", "Tree", Colors.Transparent);
        t.SetColor("relationship_line_color", "Tree", Colors.Transparent);
        t.SetFont("title_button_font", "Tree", UiFont);
        t.SetConstant("draw_guides", "Tree", 0);
        t.SetConstant("draw_relationship_lines", "Tree", 0);
        t.SetConstant("v_separation", "Tree", 3);
        t.SetConstant("h_separation", "Tree", 5);
        t.SetConstant("item_margin", "Tree", 4);
        t.SetConstant("inner_item_margin_left", "Tree", 3);
        t.SetConstant("inner_item_margin_right", "Tree", 3);
        t.SetConstant("icon_max_width", "Tree", 16);
        // Haken in Listen (z. B. "Steam durchsuchen"): sonst zeichnet Godot weisse Standard-Kaestchen, die auf hellem Grund verschwinden
        t.SetIcon("checked", "Tree", Icons.Get("check_on", 1f));
        t.SetIcon("unchecked", "Tree", Icons.Get("check_off", 1f));
        t.SetIcon("checked_disabled", "Tree", Icons.Get("check_on", 1f));
        t.SetIcon("unchecked_disabled", "Tree", Icons.Get("check_off", 1f));

        t.SetStylebox("panel", "ItemList", panel);
        t.SetStylebox("focus", "ItemList", new StyleBoxEmpty());
        t.SetStylebox("selected", "ItemList", selectedUnfocused);
        t.SetStylebox("selected_focus", "ItemList", selected);
        t.SetStylebox("hovered", "ItemList", hovered);
        t.SetStylebox("hovered_selected", "ItemList", selected);
        t.SetStylebox("hovered_selected_focus", "ItemList", selected);
        t.SetStylebox("cursor", "ItemList", cursor);
        t.SetStylebox("cursor_unfocused", "ItemList", cursor);
        t.SetColor("font_color", "ItemList", p.Text);
        t.SetColor("font_selected_color", "ItemList", p.SelectionText);
        t.SetColor("font_hovered_color", "ItemList", p.Text);
        t.SetColor("font_hovered_selected_color", "ItemList", p.SelectionText);
        t.SetColor("guide_color", "ItemList", Colors.Transparent);
        t.SetConstant("v_separation", "ItemList", 6);
        t.SetConstant("h_separation", "ItemList", 8);
        t.SetConstant("icon_margin", "ItemList", 4);
        t.SetConstant("line_separation", "ItemList", 2);

        t.SetStylebox("normal", "RichTextLabel", Make(Edge.Sunken, p.Field, p.Field, 6, 4));
        t.SetStylebox("focus", "RichTextLabel", new StyleBoxEmpty());
        t.SetColor("default_color", "RichTextLabel", p.Text);
        t.SetColor("selection_color", "RichTextLabel", p.Selection);
        t.SetFont("normal_font", "RichTextLabel", UiFont);
        t.SetFont("bold_font", "RichTextLabel", BoldFont);
        t.SetFontSize("normal_font_size", "RichTextLabel", FontSize);
        t.SetFontSize("bold_font_size", "RichTextLabel", FontSize);
    }

    private static void Menus(Theme t, Palette p)
    {
        var bar = new StyleBoxEmpty { ContentMarginLeft = 7, ContentMarginRight = 7, ContentMarginTop = 2, ContentMarginBottom = 2 };
        var barHover = Make(Edge.ThinRaised, p.Window.Lerp(p.Highlight, 0.35f), null, 7, 2);
        var barPressed = Make(Edge.ThinSunken, p.Window, null, 7, 2);
        t.SetStylebox("normal", "MenuBar", bar);
        t.SetStylebox("hover", "MenuBar", barHover);
        t.SetStylebox("pressed", "MenuBar", barPressed);
        t.SetStylebox("hover_pressed", "MenuBar", barPressed);
        t.SetStylebox("disabled", "MenuBar", bar);
        t.SetStylebox("focus", "MenuBar", new StyleBoxEmpty());
        t.SetColor("font_color", "MenuBar", p.Text);
        t.SetColor("font_hover_color", "MenuBar", p.Text);
        t.SetColor("font_pressed_color", "MenuBar", p.Text);
        t.SetColor("font_hover_pressed_color", "MenuBar", p.Text);
        t.SetColor("font_disabled_color", "MenuBar", p.TextDisabled);

        var popup = Make(Edge.Raised, p.Window.Lerp(p.Highlight, p.Dark ? 0.05f : 0.45f), null, 3, 3);
        t.SetStylebox("panel", "PopupMenu", popup);
        t.SetStylebox("hover", "PopupMenu", Make(Edge.None, p.Selection, null, 0, 0));
        t.SetStylebox("separator", "PopupMenu", new SeparatorStyle { Palette = p });
        t.SetStylebox("labeled_separator_left", "PopupMenu", new SeparatorStyle { Palette = p });
        t.SetStylebox("labeled_separator_right", "PopupMenu", new SeparatorStyle { Palette = p });
        t.SetColor("font_color", "PopupMenu", p.Text);
        t.SetColor("font_hover_color", "PopupMenu", p.SelectionText);
        t.SetColor("font_disabled_color", "PopupMenu", p.TextDisabled);
        t.SetColor("font_accelerator_color", "PopupMenu", p.TextDim);
        t.SetColor("font_separator_color", "PopupMenu", p.TextDim);
        t.SetConstant("v_separation", "PopupMenu", 5);
        t.SetConstant("h_separation", "PopupMenu", 8);
        t.SetConstant("item_start_padding", "PopupMenu", 8);
        t.SetConstant("item_end_padding", "PopupMenu", 12);
        t.SetConstant("icon_max_width", "PopupMenu", 16);
        t.SetIcon("checked", "PopupMenu", Icons.Get("check_on", 1f));
        t.SetIcon("unchecked", "PopupMenu", Icons.Get("check_off", 1f));
        t.SetIcon("radio_checked", "PopupMenu", Icons.Get("radio_on", 1f));
        t.SetIcon("radio_unchecked", "PopupMenu", Icons.Get("radio_off", 1f));
        t.SetIcon("submenu", "PopupMenu", Icons.Get("arrow_right", 1f));

        t.SetStylebox("panel", "PopupPanel", popup);
        t.SetStylebox("panel", "TooltipPanel", new BevelStyle
        {
            Kind = Edge.Border, BorderColor = p.DarkShadow, FillTop = p.Tooltip, FillBottom = p.Tooltip, Palette = p,
            ContentMarginLeft = 5, ContentMarginRight = 5, ContentMarginTop = 3, ContentMarginBottom = 3,
        });
        t.SetColor("font_color", "TooltipLabel", p.TooltipText);
        t.SetFontSize("font_size", "TooltipLabel", SmallFontSize);
    }

    private static void Scrollbars(Theme t, Palette p)
    {
        foreach (var type in new[] { "VScrollBar", "HScrollBar" })
        {
            var track = Make(Edge.None, p.Light, null, 0, 0);
            var v = type == "VScrollBar";
            track.ContentMarginLeft = track.ContentMarginRight = v ? 7 : 0;
            track.ContentMarginTop = track.ContentMarginBottom = v ? 0 : 7;
            var grab = Make(Edge.Raised, p.FaceTop, p.FaceBottom, v ? 7 : 8, v ? 8 : 7);
            grab.Horizontal = v;
            var grabHover = Make(Edge.Raised, p.FaceTop.Lerp(p.Highlight, 0.5f), p.FaceBottom, v ? 7 : 8, v ? 8 : 7);
            grabHover.Horizontal = v;
            var grabPressed = Make(Edge.Raised, p.FaceBottom, p.FaceBottom, v ? 7 : 8, v ? 8 : 7);
            t.SetStylebox("scroll", type, track);
            t.SetStylebox("scroll_focus", type, track);
            t.SetStylebox("grabber", type, grab);
            t.SetStylebox("grabber_highlight", type, grabHover);
            t.SetStylebox("grabber_pressed", type, grabPressed);
        }
    }

    private static void Misc(Theme t, Palette p)
    {
        t.SetColor("font_color", "Label", p.Text);
        t.SetColor("font_shadow_color", "Label", Colors.Transparent);

        t.SetTypeVariation("DimLabel", "Label");
        t.SetColor("font_color", "DimLabel", p.TextDim);
        t.SetFontSize("font_size", "DimLabel", SmallFontSize);

        t.SetTypeVariation("TitleLabel", "Label");
        t.SetFont("font", "TitleLabel", BoldFont);
        t.SetFontSize("font_size", "TitleLabel", TitleFontSize);

        t.SetTypeVariation("BoldLabel", "Label");
        t.SetFont("font", "BoldLabel", BoldFont);

        t.SetTypeVariation("CaptionLabel", "Label");
        t.SetFont("font", "CaptionLabel", BoldFont);
        t.SetColor("font_color", "CaptionLabel", p.TitleText);

        t.SetStylebox("panel", "Panel", Make(Edge.None, p.Window, null, 0, 0));
        t.SetStylebox("panel", "PanelContainer", Make(Edge.None, p.Window, null, 0, 0));

        // Varianten fuer Flaechen
        Variation(t, "WindowPanel", Make(Edge.None, p.Window, null, 0, 0));
        Variation(t, "ToolbarPanel", Toolbar(p));
        Variation(t, "RaisedPanel", Make(Edge.Raised, p.Window, null, 8, 8));
        Variation(t, "FieldPanel", Make(Edge.Sunken, p.Field, null, 6, 6));
        Variation(t, "StatusPanel", Make(Edge.None, p.Window, null, 2, 3));
        Variation(t, "StatusCell", Make(Edge.ThinSunken, Colors.Transparent, null, 6, 2));
        Variation(t, "GroupPanel", Make(Edge.Etched, Colors.Transparent, null, 10, 12));
        Variation(t, "SelectedTile", Make(Edge.Sunken, p.SelectionSoft, null, 8, 8));
        Variation(t, "DialogPanel", Make(Edge.Raised, p.Window, null, 3, 3));
        var title = Make(Edge.None, p.TitleA, p.TitleB, 8, 4);
        title.Horizontal = true;
        Variation(t, "TitlebarPanel", title);
        var hint = Make(Edge.ThinSunken, p.Dark ? p.Field.Lerp(p.Accent, 0.12f) : p.Tooltip, null, 8, 6);
        Variation(t, "HintPanel", hint);

        t.SetStylebox("separator", "HSeparator", new SeparatorStyle { Palette = p });
        t.SetConstant("separation", "HSeparator", 8);
        t.SetStylebox("separator", "VSeparator", new SeparatorStyle { Palette = p, Vertical = true });
        t.SetConstant("separation", "VSeparator", 8);

        t.SetConstant("separation", "HBoxContainer", 6);
        t.SetConstant("separation", "VBoxContainer", 6);
        t.SetConstant("h_separation", "GridContainer", 10);
        t.SetConstant("v_separation", "GridContainer", 6);
        t.SetConstant("separation", "HSplitContainer", 6);
        t.SetIcon("grabber", "HSplitContainer", new ImageTexture());
        t.SetConstant("minimum_grab_thickness", "HSplitContainer", 6);
    }

    private static BevelStyle Toolbar(Palette p)
    {
        var s = Make(Edge.None, p.ToolbarTop, p.ToolbarBottom, 4, 3);
        s.BottomRule = true;
        s.ContentMarginBottom = 5;
        return s;
    }

    private static void Variation(Theme t, string name, StyleBox box)
    {
        t.SetTypeVariation(name, "PanelContainer");
        t.SetStylebox("panel", name, box);
    }
}
