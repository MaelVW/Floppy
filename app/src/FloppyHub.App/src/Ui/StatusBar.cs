using FloppyHub.App.Art;
using Godot;

namespace FloppyHub.App.Ui;

/// <summary>Statusleiste mit eingelassenen Feldern (Icon + Text).</summary>
public partial class StatusBar : PanelContainer
{
    private readonly HBoxContainer _row;
    private readonly Dictionary<string, (TextureRect Icon, Label Text)> _cells = new();

    public StatusBar()
    {
        ThemeTypeVariation = "StatusPanel";
        _row = Ui.HBox(3);
        AddChild(_row);
    }

    public void AddCell(string key, float minWidth = 0, bool expand = false)
    {
        var icon = Icons.Rect("led_off");
        icon.Visible = false;
        var label = Ui.Label("");
        label.ClipText = true;
        label.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        label.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        var cell = Ui.Panel("StatusCell", Ui.HBox(4, icon, label));
        cell.CustomMinimumSize = new Vector2(minWidth, 0);
        if (expand) cell.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _row.AddChild(cell);
        _cells[key] = (icon, label);
    }

    public void Set(string key, string text, string? icon = null, string? tooltip = null)
    {
        if (!_cells.TryGetValue(key, out var cell)) return;
        cell.Text.Text = text;
        cell.Text.TooltipText = tooltip ?? text;
        cell.Text.MouseFilter = MouseFilterEnum.Pass;
        if (icon is null)
        {
            cell.Icon.Visible = false;
        }
        else
        {
            cell.Icon.Texture = Icons.Get(icon);
            cell.Icon.Visible = true;
        }
    }
}
