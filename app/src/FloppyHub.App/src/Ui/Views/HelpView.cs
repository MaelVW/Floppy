using FloppyHub.App.Core;
using Godot;

namespace FloppyHub.App.Ui.Views;

/// <summary>Häufige Fragen, nach Themen sortiert - reine Lesetext-Ansicht, kein Netz, keine Daten.</summary>
public partial class HelpView : ViewBase
{
    public override string Key => "help";

    private static readonly (string Cat, (string Q, string A)[] Items)[] Sections =
    [
        ("FAQ_CAT_START", [("FAQ_START_Q1", "FAQ_START_A1"), ("FAQ_START_Q2", "FAQ_START_A2"), ("FAQ_START_Q3", "FAQ_START_A3")]),
        ("FAQ_CAT_TRUST", [("FAQ_TRUST_Q1", "FAQ_TRUST_A1"), ("FAQ_TRUST_Q2", "FAQ_TRUST_A2"), ("FAQ_TRUST_Q3", "FAQ_TRUST_A3")]),
        ("FAQ_CAT_WRITE", [("FAQ_WRITE_Q1", "FAQ_WRITE_A1"), ("FAQ_WRITE_Q2", "FAQ_WRITE_A2")]),
        ("FAQ_CAT_LIBRARY", [("FAQ_LIBRARY_Q1", "FAQ_LIBRARY_A1"), ("FAQ_LIBRARY_Q2", "FAQ_LIBRARY_A2")]),
        ("FAQ_CAT_DRIVES", [("FAQ_DRIVES_Q1", "FAQ_DRIVES_A1"), ("FAQ_DRIVES_Q2", "FAQ_DRIVES_A2")]),
        ("FAQ_CAT_CHAT", [("FAQ_CHAT_Q1", "FAQ_CHAT_A1"), ("FAQ_CHAT_Q2", "FAQ_CHAT_A2"), ("FAQ_CHAT_Q3", "FAQ_CHAT_A3"), ("FAQ_CHAT_Q4", "FAQ_CHAT_A4"), ("FAQ_CHAT_Q5", "FAQ_CHAT_A5"), ("FAQ_CHAT_Q6", "FAQ_CHAT_A6"), ("FAQ_CHAT_Q7", "FAQ_CHAT_A7")]),
        ("FAQ_CAT_GAME", [("FAQ_GAME_Q1", "FAQ_GAME_A1"), ("FAQ_GAME_Q2", "FAQ_GAME_A2"), ("FAQ_GAME_Q3", "FAQ_GAME_A3")]),
        ("FAQ_CAT_SETTINGS", [("FAQ_SETTINGS_Q1", "FAQ_SETTINGS_A1"), ("FAQ_SETTINGS_Q2", "FAQ_SETTINGS_A2"), ("FAQ_SETTINGS_Q3", "FAQ_SETTINGS_A3"), ("FAQ_SETTINGS_Q4", "FAQ_SETTINGS_A4"), ("FAQ_SETTINGS_Q5", "FAQ_SETTINGS_A5")]),
        ("FAQ_CAT_TROUBLE", [("FAQ_TROUBLE_Q1", "FAQ_TROUBLE_A1"), ("FAQ_TROUBLE_Q2", "FAQ_TROUBLE_A2"), ("FAQ_TROUBLE_Q3", "FAQ_TROUBLE_A3")]),
        ("FAQ_CAT_PRIVACY", [("FAQ_PRIVACY_Q1", "FAQ_PRIVACY_A1"), ("FAQ_PRIVACY_Q2", "FAQ_PRIVACY_A2")]),
    ];

    protected override void Build()
    {
        var column = Ui.VBox(12);
        column.AddChild(Ui.Dim(Loc.T("FAQ_INTRO"), wrap: true));

        foreach (var (cat, items) in Sections)
        {
            var body = Ui.VBox(10);
            foreach (var (q, a) in items)
                body.AddChild(Ui.VBox(2, Ui.Label(Loc.T(q), "BoldLabel", wrap: true), Ui.Dim(Loc.T(a), wrap: true)));
            column.AddChild(new GroupBox(Loc.T(cat), body));
        }

        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled }.Expand(vertical: true);
        scroll.AddChild(Ui.Margin(column.Expand(), 0, 0, 8, 0).Expand());
        AddChild(scroll);
    }
}
