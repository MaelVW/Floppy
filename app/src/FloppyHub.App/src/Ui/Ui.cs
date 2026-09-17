using System.Globalization;
using FloppyHub.App.Art;
using FloppyHub.App.Core;
using Godot;

namespace FloppyHub.App.Ui;

/// <summary>Kurze Helfer, damit der Oberflaechen-Code lesbar bleibt.</summary>
public static class Ui
{
    public static T With<T>(this T node, Action<T> configure) where T : GodotObject
    {
        configure(node);
        return node;
    }

    public static T AddTo<T>(this T child, Node parent) where T : Node
    {
        parent.AddChild(child);
        return child;
    }

    public static void ClearChildren(this Node node)
    {
        foreach (var child in node.GetChildren())
        {
            node.RemoveChild(child);
            child.QueueFree();
        }
    }

    public static Label Label(string text, string? variation = null, bool wrap = false) => new()
    {
        Text = text,
        ThemeTypeVariation = variation ?? "",
        AutowrapMode = wrap ? TextServer.AutowrapMode.WordSmart : TextServer.AutowrapMode.Off,
        VerticalAlignment = VerticalAlignment.Center,
        AutoTranslateMode = Node.AutoTranslateModeEnum.Disabled,
    };

    public static Label Dim(string text, bool wrap = false) => Label(text, "DimLabel", wrap);

    public static Button Button(string text, string? icon = null, Action? pressed = null)
    {
        var b = new Button
        {
            Text = text,
            Icon = icon is null ? null : Icons.Get(icon),
            AutoTranslateMode = Node.AutoTranslateModeEnum.Disabled,
            FocusMode = Control.FocusModeEnum.All,
        };
        if (pressed is not null) b.Pressed += pressed;
        return b;
    }

    public static VBoxContainer VBox(int separation = 6, params Control[] children)
    {
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", separation);
        foreach (var c in children) box.AddChild(c);
        return box;
    }

    public static HBoxContainer HBox(int separation = 6, params Control[] children)
    {
        var box = new HBoxContainer();
        box.AddThemeConstantOverride("separation", separation);
        foreach (var c in children) box.AddChild(c);
        return box;
    }

    public static PanelContainer Panel(string variation, Control? child = null)
    {
        var p = new PanelContainer { ThemeTypeVariation = variation };
        if (child is not null) p.AddChild(child);
        return p;
    }

    public static MarginContainer Margin(Control child, int left, int top, int right, int bottom)
    {
        var m = new MarginContainer();
        m.AddThemeConstantOverride("margin_left", left);
        m.AddThemeConstantOverride("margin_top", top);
        m.AddThemeConstantOverride("margin_right", right);
        m.AddThemeConstantOverride("margin_bottom", bottom);
        m.AddChild(child);
        return m;
    }

    public static MarginContainer Margin(Control child, int all) => Margin(child, all, all, all, all);

    public static Control Spacer(bool horizontal = true) => new()
    {
        SizeFlagsHorizontal = horizontal ? Control.SizeFlags.ExpandFill : Control.SizeFlags.Fill,
        SizeFlagsVertical = horizontal ? Control.SizeFlags.Fill : Control.SizeFlags.ExpandFill,
        MouseFilter = Control.MouseFilterEnum.Ignore,
    };

    public static T Expand<T>(this T control, bool horizontal = true, bool vertical = false) where T : Control
    {
        if (horizontal) control.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        if (vertical) control.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        return control;
    }

    /// <summary>Zeile "icon + text" fuer Hinweise.</summary>
    public static HBoxContainer IconLine(string icon, string text, string? variation = null)
    {
        var label = Label(text, variation, wrap: true).Expand();
        return HBox(6, Icons.Rect(icon), label);
    }

    // ------------------------------------------------------------------
    // Formatierung
    // ------------------------------------------------------------------

    public static string Number(double value, string format)
    {
        var s = value.ToString(format, CultureInfo.InvariantCulture);
        return Loc.T("FMT_DECIMAL") == "," ? s.Replace(',', '').Replace('.', ',').Replace('', '.') : s;
    }

    public static string Bytes(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024L * 1024 => $"{Number(bytes / 1024.0, "#,0")} KB",
        < 10L * 1024 * 1024 => $"{Number(bytes / 1048576.0, "0.00")} MB",
        < 1024L * 1024 * 1024 => $"{Number(bytes / 1048576.0, "#,0.0")} MB",
        _ => $"{Number(bytes / 1073741824.0, "#,0.0")} GB",
    };
}
