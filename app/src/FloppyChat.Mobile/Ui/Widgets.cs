namespace FloppyChat.Mobile.Ui;

/// <summary>
/// Rahmen wie bei Windows 2000/WinRAR: erhaben (Knoepfe, Gruppen) oder eingesunken (Eingabefelder).
/// Fuenf uebereinanderliegende Flaechen ergeben die typische Kante: aussen hell/dunkel, innen Zwischentoene.
/// </summary>
internal sealed class Bevel : Grid
{
    private readonly BoxView[] _layers = new BoxView[5];
    private readonly Tone? _face;
    private bool _sunken;

    /// <param name="face">Fuellfarbe; null = Standard (erhaben: Flaeche, eingesunken: Feldfarbe).</param>
    /// <param name="edge">Breite einer Kantenlinie in dp.</param>
    public Bevel(View content, bool sunken = false, Tone? face = null, double edge = 1)
    {
        RowSpacing = 0;
        ColumnSpacing = 0;
        _face = face;
        _sunken = sunken;

        var t = edge;
        Thickness[] margins = [new(0), new(0, 0, t, t), new(t), new(t, t, 2 * t, 2 * t), new(2 * t)];
        for (var i = 0; i < _layers.Length; i++)
        {
            _layers[i] = new BoxView { Margin = margins[i], InputTransparent = true };
            Children.Add(_layers[i]);
        }

        content.Margin = new Thickness(2 * t);
        Children.Add(content);
        Apply();
    }

    /// <summary>Eingedrueckt darstellen (fuer den gedrueckten Knopf).</summary>
    public bool Sunken
    {
        get => _sunken;
        set
        {
            if (_sunken == value) return;
            _sunken = value;
            Apply();
        }
    }

    private void Apply()
    {
        Tone[] tones = _sunken
            ? [Theme.Highlight, Theme.Shadow, Theme.Light, Theme.DarkShadow, _face ?? Theme.Field]
            : [Theme.DarkShadow, Theme.Highlight, Theme.Shadow, Theme.Light, _face ?? Theme.Face];
        for (var i = 0; i < _layers.Length; i++) _layers[i].Tint(BoxView.ColorProperty, tones[i]);
    }
}

/// <summary>Knopf mit erhabener Kante; gedrueckt wirkt er eingesunken. Darunter steckt ein echter Button (Antippen, Barrierefreiheit, Welleneffekt).</summary>
internal sealed class BevelButton : Grid
{
    private readonly Bevel _bevel;

    public BevelButton(string text, Action onClick, bool primary = false, string? icon = null, double fontSize = 15)
    {
        Button = new Button
        {
            Text = text,
            FontSize = fontSize,
            FontAttributes = primary ? FontAttributes.Bold : FontAttributes.None,
            BackgroundColor = Colors.Transparent,
            BorderWidth = 0,
            CornerRadius = 0,
            Padding = new Thickness(8, 0),
            HeightRequest = 44,
        };
        if (primary) Button.TextColor = Colors.White;
        else Button.Tint(Button.TextColorProperty, Theme.Text);

        if (icon is not null)
        {
            Button.ImageSource = icon;
            Button.ContentLayout = new Button.ButtonContentLayout(Button.ButtonContentLayout.ImagePosition.Left, 6);
        }

        _bevel = new Bevel(Button, face: primary ? Theme.Selection : null);
        Children.Add(_bevel);

        Button.Clicked += (_, _) => onClick();
        Button.Pressed += (_, _) => _bevel.Sunken = true;
        Button.Released += (_, _) => _bevel.Sunken = false;
    }

    public Button Button { get; }

    protected override void OnPropertyChanged(string? propertyName = null)
    {
        base.OnPropertyChanged(propertyName);
        if (propertyName == nameof(IsEnabled)) Opacity = IsEnabled ? 1 : 0.45;
    }
}

/// <summary>Eingabefeld im eingesunkenen Rahmen.</summary>
internal sealed class Field : Grid
{
    public Field(string placeholder, int maxLength = 0)
    {
        Entry = new Entry
        {
            Placeholder = placeholder,
            FontSize = 16,
            BackgroundColor = Colors.Transparent,
            HeightRequest = 44,
        };
        if (maxLength > 0) Entry.MaxLength = maxLength;
        Entry.Tint(Entry.TextColorProperty, Theme.Text);
        Entry.Tint(Entry.PlaceholderColorProperty, Theme.TextDim);
        Children.Add(new Bevel(Entry, sunken: true));
    }

    public Entry Entry { get; }
}

/// <summary>Titelleiste wie bei den alten Fenstern: blauer Verlauf, weisse Schrift; links optional "zurueck".</summary>
internal sealed class HeaderBar : Grid
{
    public HeaderBar(string title, Action? onBack = null)
    {
        Padding = new Thickness(6, 6, 8, 6);
        ColumnSpacing = 4;
        this.SetAppTheme<Brush>(BackgroundProperty, Gradient(Theme.TitleA.Light, Theme.TitleB.Light), Gradient(Theme.TitleA.Dark, Theme.TitleB.Dark));

        ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));

        if (onBack is not null)
        {
            var back = new Label
            {
                Text = "‹",
                FontSize = 32,
                TextColor = Theme.OnTitle,
                VerticalOptions = LayoutOptions.Center,
                HorizontalTextAlignment = TextAlignment.Center,
                WidthRequest = 36,
                HeightRequest = 44,
                VerticalTextAlignment = TextAlignment.Center,
            };
            back.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(onBack) });
            SemanticProperties.SetDescription(back, "Zurück");
            this.Add(back, 0, 0);
        }

        Title = new Label { Text = title, FontSize = 18, FontAttributes = FontAttributes.Bold, TextColor = Theme.OnTitle, LineBreakMode = LineBreakMode.TailTruncation };
        Subtitle = new Label { FontSize = 12, TextColor = Theme.OnTitleDim, LineBreakMode = LineBreakMode.TailTruncation, IsVisible = false };
        var texts = new VerticalStackLayout { Spacing = 0, VerticalOptions = LayoutOptions.Center, Children = { Title, Subtitle } };
        this.Add(texts, 1, 0);

        Right = new HorizontalStackLayout { Spacing = 6, VerticalOptions = LayoutOptions.Center };
        this.Add(Right, 2, 0);
    }

    public Label Title { get; }

    /// <summary>Zweite Zeile (z. B. Verbindungsstand); leer = ausgeblendet.</summary>
    public Label Subtitle { get; }

    /// <summary>Platz fuer Symbole rechts.</summary>
    public HorizontalStackLayout Right { get; }

    /// <param name="dot">Farbe eines Punktes vor dem Text (Verbindungs-Lampe); null = kein Punkt.</param>
    public void SetSubtitle(string text, Color? dot = null)
    {
        if (dot is null)
        {
            Subtitle.FormattedText = null;
            Subtitle.Text = text;
        }
        else
        {
            Subtitle.FormattedText = new FormattedString
            {
                Spans = { new Span { Text = "● ", TextColor = dot }, new Span { Text = text } },
            };
        }
        Subtitle.IsVisible = text.Length > 0;
    }

    private static LinearGradientBrush Gradient(Color left, Color right) =>
        new([new GradientStop(left, 0f), new GradientStop(right, 1f)], new Point(0, 0), new Point(1, 0));
}

/// <summary>Kleine Bausteine fuer die Seiten.</summary>
internal static class Kit
{
    public static Label Text(string text, double size = 15, bool bold = false, Tone? tone = null)
    {
        var label = new Label
        {
            Text = text,
            FontSize = size,
            FontAttributes = bold ? FontAttributes.Bold : FontAttributes.None,
            LineBreakMode = LineBreakMode.WordWrap,
        };
        label.Tint(Label.TextColorProperty, tone ?? Theme.Text);
        return label;
    }

    public static Label Dim(string text, double size = 12) => Text(text, size, false, Theme.TextDim);

    public static Image Icon(string name, double size = 24) => new()
    {
        Source = name,
        WidthRequest = size,
        HeightRequest = size,
        HorizontalOptions = LayoutOptions.Center,
        VerticalOptions = LayoutOptions.Center,
    };

    /// <summary>Kleine erhabene Flaeche mit Symbol fuer die Titelleiste (wie die Werkzeugleiste bei WinRAR).</summary>
    public static View ToolButton(string icon, Action onClick, string description)
    {
        var tile = new Bevel(new Grid { WidthRequest = 30, HeightRequest = 30, Children = { Icon(icon, 22) } });
        tile.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(onClick) });
        SemanticProperties.SetDescription(tile, description);
        return tile;
    }

    /// <summary>Erhabene Gruppe mit Ueberschrift.</summary>
    public static Bevel Panel(string? title, params View[] children)
    {
        var stack = new VerticalStackLayout { Spacing = 8, Padding = new Thickness(10) };
        if (title is not null) stack.Add(Text(title, 16, bold: true));
        foreach (var child in children) stack.Add(child);
        return new Bevel(stack);
    }

    /// <summary>Seite mit Fensterfarbe und ohne die eingebaute Titelleiste (wir haben eine eigene).</summary>
    public static void Prepare(ContentPage page)
    {
        NavigationPage.SetHasNavigationBar(page, false);
        page.Tint(Page.BackgroundColorProperty, Theme.Window);
        page.SafeAreaEdges = SafeAreaEdges.All;   // Statusleiste, Navigationsleiste und Tastatur bleiben frei
    }

    /// <summary>Grid mit Titelleiste oben und Inhalt darunter (fuellt den Rest).</summary>
    public static Grid Screen(HeaderBar title, View body, params View[] below)
    {
        var grid = new Grid { RowSpacing = 0 };
        grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        grid.RowDefinitions.Add(new RowDefinition(GridLength.Star));
        grid.Add(title, 0, 0);
        grid.Add(body, 0, 1);
        foreach (var view in below)
        {
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            grid.Add(view, 0, grid.RowDefinitions.Count - 1);
        }
        return grid;
    }
}
