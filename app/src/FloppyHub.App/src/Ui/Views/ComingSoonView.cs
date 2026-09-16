using FloppyHub.App.Art;
using FloppyHub.App.Core;
using Godot;

namespace FloppyHub.App.Ui.Views;

/// <summary>Platzhalter fuer Funktionen, die in einer spaeteren Etappe kommen.</summary>
public partial class ComingSoonView : ViewBase
{
    private readonly string _key;
    private readonly string _icon;
    private readonly int _stage;

    public ComingSoonView() : this("soon", "help", 0) { }

    public ComingSoonView(string key, string icon, int stage)
    {
        _key = key;
        _icon = icon;
        _stage = stage;
    }

    public override string Key => _key;

    protected override void Build()
    {
        var prefix = "SOON_" + _key.ToUpperInvariant();
        var icon = Icons.Rect(_icon, 3f);
        icon.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
        var title = Ui.Label(Loc.T(prefix + "_TITLE"), "TitleLabel");
        title.HorizontalAlignment = HorizontalAlignment.Center;
        var text = Ui.Label(Loc.T(prefix + "_TEXT"), wrap: true);
        text.HorizontalAlignment = HorizontalAlignment.Center;
        text.CustomMinimumSize = new Vector2(440, 0);
        var stage = Ui.Panel("HintPanel", Ui.HBox(6, Icons.Rect("info"), Ui.Label(Loc.T("SOON_STAGE", _stage))));
        stage.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;

        var center = new CenterContainer().Expand(vertical: true);
        center.AddChild(Ui.VBox(14, icon, title, text, stage));
        AddChild(center);
    }
}
