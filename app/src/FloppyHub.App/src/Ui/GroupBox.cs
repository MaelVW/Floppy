using FloppyHub.App.Skin;
using Godot;

namespace FloppyHub.App.Ui;

/// <summary>
/// Klassischer Gruppenrahmen: eingeritzte Linie, Titel sitzt auf der oberen Kante.
/// </summary>
public partial class GroupBox : Container
{
    private const float PadX = 10;
    private const float PadBottom = 10;
    private const float TitleX = 8;

    private readonly Label _title;
    private readonly Control _content;

    public GroupBox() : this("", new Control()) { }

    public GroupBox(string title, Control content)
    {
        _title = Ui.Label(title, "BoldLabel");
        _title.MouseFilter = MouseFilterEnum.Ignore;
        _content = content;
        AddChild(_title);
        AddChild(_content);
    }

    public string Title
    {
        get => _title.Text;
        set => _title.Text = value;
    }

    private float TitleHeight => _title.GetCombinedMinimumSize().Y;

    public override Vector2 _GetMinimumSize()
    {
        var c = _content.GetCombinedMinimumSize();
        var t = _title.GetCombinedMinimumSize();
        return new Vector2(Math.Max(c.X + PadX * 2, t.X + TitleX * 2 + 8), c.Y + TitleHeight + 6 + PadBottom);
    }

    public override void _Notification(int what)
    {
        if (what == NotificationSortChildren)
        {
            var t = _title.GetCombinedMinimumSize();
            FitChildInRect(_title, new Rect2(TitleX + 4, 0, t.X, t.Y));
            var top = TitleHeight + 6;
            FitChildInRect(_content, new Rect2(PadX, top, Size.X - PadX * 2, Size.Y - top - PadBottom));
        }
        else if (what == NotificationThemeChanged)
        {
            UpdateMinimumSize();
            QueueRedraw();
        }
    }

    public override void _Draw()
    {
        var p = Palette.Current;
        var line = UiScale.Line;
        var y = UiScale.Snap(TitleHeight / 2);
        var r = new Rect2(0, y, UiScale.Snap(Size.X), UiScale.Snap(Size.Y) - y);

        // Rille: dunkel, darunter hell versetzt
        DrawRect(new Rect2(r.Position.X, r.Position.Y, r.Size.X - line, line), p.Shadow);
        DrawRect(new Rect2(r.Position.X, r.Position.Y, line, r.Size.Y - line), p.Shadow);
        DrawRect(new Rect2(r.Position.X + line, r.Position.Y + line, r.Size.X - line * 2, line), p.Highlight);
        DrawRect(new Rect2(r.Position.X + line, r.Position.Y + line, line, r.Size.Y - line * 2), p.Highlight);
        DrawRect(new Rect2(r.End.X - line * 2, r.Position.Y, line, r.Size.Y - line), p.Shadow);
        DrawRect(new Rect2(r.End.X - line, r.Position.Y, line, r.Size.Y), p.Highlight);
        DrawRect(new Rect2(r.Position.X, r.End.Y - line * 2, r.Size.X - line, line), p.Shadow);
        DrawRect(new Rect2(r.Position.X, r.End.Y - line, r.Size.X, line), p.Highlight);

        // Titel-Hintergrund unterbricht die Linie
        var t = _title.GetCombinedMinimumSize();
        DrawRect(new Rect2(TitleX, 0, t.X + 8, t.Y), p.Window);
    }
}
