using Floppy.Core.Chat;
using FloppyHub.App.Core;
using FloppyHub.App.Services;
using FloppyHub.App.Skin;
using Godot;

namespace FloppyHub.App.Ui;

/// <summary>
/// "Chat anpassen": fuenf Kleinigkeiten, die den Chat persoenlicher und angenehmer machen.
/// <list type="number">
/// <item>Anzeigename (statt nur der ID) und</item>
/// <item>Namensfarbe - beides sehen alle im Raum;</item>
/// <item>Benachrichtigung (Ton + blinkende Taskleiste, wenn der Chat nicht im Blick ist),</item>
/// <item>Hervorhebung von Nachrichten, in denen man erwaehnt wird, und</item>
/// <item>Schriftgroesse im Verlauf - das bleibt bei einem selbst.</item>
/// </list>
/// </summary>
public static class ChatCustomizeDialog
{
    public static void Open(IAppHost host, Action applied)
    {
        var s = host.Services;
        var chat = s.Chat;
        var dark = Palette.Current.Dark;
        var admin = chat.IAmAdmin;
        var color = s.Settings.ChatColor;

        // ---- 1. Anzeigename ----
        var alias = new LineEdit
        {
            Text = s.Settings.ChatAlias,
            MaxLength = ChatProfile.MaxAliasLength,
            PlaceholderText = Loc.T("CHAT_ALIAS_PLACEHOLDER"),
            ClearButtonEnabled = true,
        };
        var problem = Ui.Label("", wrap: true);
        problem.AddThemeColorOverride("font_color", Palette.Current.Warn);

        // ---- 2. Namensfarbe ----
        var preview = new RichTextLabel
        {
            BbcodeEnabled = false,
            FitContent = true,
            ScrollActive = false,
            SelectionEnabled = false,
            CustomMinimumSize = new Vector2(0, 24),
            AutoTranslateMode = Node.AutoTranslateModeEnum.Disabled,
        };
        var group = new ButtonGroup { AllowUnpress = false };
        var swatches = Ui.HBox(4);
        var auto = Ui.Button(Loc.T("CHAT_COLOR_AUTO"));
        auto.ToggleMode = true;
        auto.ButtonGroup = group;
        auto.ButtonPressed = color == 0;
        auto.Pressed += () => { color = 0; UpdatePreview(); };
        swatches.AddChild(auto);
        for (var i = 1; i <= ChatProfile.ColorCount; i++)
        {
            var index = i;
            var swatch = Swatch(ChatColors.Get(index, dark), group);
            swatch.ButtonPressed = color == index;
            swatch.Pressed += () => { color = index; UpdatePreview(); };
            swatches.AddChild(swatch);
        }

        // ---- 3. Benachrichtigung ----
        var notify = new OptionButton();
        foreach (var key in new[] { "CHAT_NOTIFY_OFF", "CHAT_NOTIFY_MENTIONS", "CHAT_NOTIFY_ALL" }) notify.AddItem(Loc.T(key));
        notify.Select((int)s.Settings.ChatNotify);

        // ---- 4. Erwaehnungen ----
        var highlight = new CheckBox { Text = Loc.T("CHAT_HIGHLIGHT"), ButtonPressed = s.Settings.ChatHighlightMentions };

        // ---- 5. Schriftgroesse ----
        var font = new OptionButton();
        foreach (var key in new[] { "CHAT_FONT_SMALL", "CHAT_FONT_NORMAL", "CHAT_FONT_LARGE", "CHAT_FONT_HUGE" }) font.AddItem(Loc.T(key));
        font.Select((int)s.Settings.ChatFont);

        var dialog = new RetroDialog(Loc.T("CHAT_CUSTOMIZE_TITLE"), "chat", 560);
        var test = Ui.Button(Loc.T("CHAT_NOTIFY_TEST"), null, () => ChatSound.Play(dialog));

        var visible = new GridContainer { Columns = 2 };
        visible.AddThemeConstantOverride("h_separation", 12);
        visible.AddThemeConstantOverride("v_separation", 8);
        visible.AddChild(Ui.Label(Loc.T("CHAT_ALIAS")));
        visible.AddChild(Ui.VBox(2, alias, problem).Expand());
        visible.AddChild(Ui.Label(Loc.T("CHAT_COLOR")));
        visible.AddChild(swatches.Expand());
        visible.AddChild(Ui.Label(Loc.T("CHAT_CUSTOMIZE_PREVIEW")));
        visible.AddChild(Ui.Panel("FieldPanel", Ui.Margin(preview, 6)).Expand());

        var mine = new GridContainer { Columns = 2 };
        mine.AddThemeConstantOverride("h_separation", 12);
        mine.AddThemeConstantOverride("v_separation", 8);
        mine.AddChild(Ui.Label(Loc.T("CHAT_NOTIFY")));
        mine.AddChild(Ui.HBox(8, notify.Expand(), test).Expand());
        mine.AddChild(Ui.Label(""));
        mine.AddChild(Ui.Dim(Loc.T("CHAT_NOTIFY_HINT"), wrap: true).Expand());
        mine.AddChild(Ui.Label(Loc.T("CHAT_MENTIONS")));
        mine.AddChild(highlight.Expand());
        mine.AddChild(Ui.Label(Loc.T("CHAT_FONT")));
        mine.AddChild(font.Expand());

        dialog.Body.AddChild(Ui.Label(Loc.T("CHAT_CUSTOMIZE_INTRO"), wrap: true));
        dialog.Body.AddChild(new GroupBox(Loc.T("CHAT_CUSTOMIZE_VISIBLE"), visible));
        dialog.Body.AddChild(new GroupBox(Loc.T("CHAT_CUSTOMIZE_MINE"), mine));

        alias.TextChanged += _ => CheckAlias();
        CheckAlias();

        dialog.AddButton(Loc.T("BTN_OK"), Apply, closes: false, icon: "ok");
        dialog.AddButton(Loc.T("BTN_CANCEL"), () => { });
        dialog.Open(host.DialogLayer);
        Callable.From(() => alias.GrabFocus()).CallDeferred();
        return;

        // ------------------------------------------------------------------

        void CheckAlias()
        {
            var problemKind = string.IsNullOrWhiteSpace(alias.Text) ? AliasProblem.None : ChatProfile.Check(alias.Text, admin);
            problem.Text = problemKind == AliasProblem.None ? "" : Loc.T("CHAT_ALIAS_PROBLEM_" + problemKind.ToString().ToUpperInvariant());
            UpdatePreview();
        }

        // So sehen dich die anderen: Uhrzeit, Name mit ID-Endung, Beispieltext - in der gewaehlten Farbe
        void UpdatePreview()
        {
            var p = Palette.Current;
            var name = ChatProfile.Clean(alias.Text, admin) is { } cleaned
                ? $"{cleaned} {ChatProfile.IdTag(chat.Identity.Id)}"
                : Loc.T("CHAT_ID", chat.Identity.Id);
            var nameColor = color > 0 ? ChatColors.Get(color, p.Dark) : ChatColors.Auto(chat.Identity.Fingerprint, p.Dark);

            preview.Clear();
            preview.PushColor(p.TextDim);
            preview.AddText(DateTime.Now.ToString("HH:mm") + "  ");
            preview.Pop();
            preview.PushColor(nameColor);
            preview.PushBold();
            preview.AddText(name);
            preview.Pop();
            preview.Pop();
            preview.AddText(": " + Loc.T("CHAT_CUSTOMIZE_SAMPLE"));
        }

        void Apply()
        {
            if (!string.IsNullOrWhiteSpace(alias.Text) && ChatProfile.Check(alias.Text, admin) != AliasProblem.None)
            {
                CheckAlias();   // bleibt offen, der Grund steht schon da
                return;
            }
            s.Settings.ChatAlias = ChatProfile.Clean(alias.Text, allowReserved: true) ?? "";
            s.Settings.ChatColor = color;
            s.Settings.ChatNotify = (ChatNotifyMode)notify.Selected;
            s.Settings.ChatHighlightMentions = highlight.ButtonPressed;
            s.Settings.ChatFont = (ChatFontSize)font.Selected;
            s.SaveSettings();
            chat.ApplyProfile();
            host.SetStatusMessage(Loc.T("CHAT_CUSTOMIZED"), "ok");
            applied();
            dialog.Close();
        }
    }

    /// <summary>Farbfeld zum Anklicken; das gewaehlte bekommt einen dicken Rahmen.</summary>
    private static Button Swatch(Color color, ButtonGroup group)
    {
        var p = Palette.Current;

        StyleBoxFlat Box(int border, Color borderColor)
        {
            var box = new StyleBoxFlat { BgColor = color, BorderColor = borderColor };
            box.SetBorderWidthAll(border);
            box.SetCornerRadiusAll(0);
            return box;
        }

        var button = new Button
        {
            ToggleMode = true,
            ButtonGroup = group,
            CustomMinimumSize = new Vector2(26, 24),
            FocusMode = Control.FocusModeEnum.All,
            AutoTranslateMode = Node.AutoTranslateModeEnum.Disabled,
        };
        button.AddThemeStyleboxOverride("normal", Box(1, color.Darkened(0.4f)));
        button.AddThemeStyleboxOverride("hover", Box(2, p.TextDim));
        button.AddThemeStyleboxOverride("pressed", Box(3, p.Text));
        button.AddThemeStyleboxOverride("hover_pressed", Box(3, p.Text));
        button.AddThemeStyleboxOverride("focus", Box(2, p.Accent));
        return button;
    }
}
