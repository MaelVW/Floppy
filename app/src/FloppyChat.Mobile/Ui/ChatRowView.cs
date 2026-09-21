using Floppy.Chat.Client;

namespace FloppyChat.Mobile.Ui;

/// <summary>
/// Eine Zeile im Chatverlauf: "12:31  Tom #1417: Hallo" - Zeit gedaempft, Name farbig und fett, Text normal.
/// Der Text steht in einem Label (kein Formatieren durch fremden Text moeglich). Die Farben werden beim Zeichnen
/// fuer das gerade aktive Design gewaehlt; wechselt das Handy das Design, baut die Chatseite die Liste neu auf.
/// </summary>
internal sealed class ChatRowView : ContentView
{
    private readonly Label _label = new() { LineBreakMode = LineBreakMode.WordWrap };
    private readonly double _fontSize;

    public ChatRowView(double fontSize)
    {
        _fontSize = fontSize;
        Padding = new Thickness(10, 2);
        Content = _label;
    }

    protected override void OnBindingContextChanged()
    {
        base.OnBindingContextChanged();
        if (BindingContext is ChatRow row) Render(row);
    }

    private void Render(ChatRow row)
    {
        var dark = Theme.IsDark;
        var text = Theme.Text.Pick(dark);
        var dim = Theme.TextDim.Pick(dark);
        var small = _fontSize * 0.8;

        var formatted = new FormattedString();
        formatted.Spans.Add(new Span { Text = row.Time + "  ", TextColor = dim, FontSize = small });

        switch (row.Kind)
        {
            case ChatRowKind.Message:
                formatted.Spans.Add(new Span
                {
                    Text = row.Name,
                    TextColor = Theme.NameTone(row.ColorIndex).Pick(dark),
                    FontAttributes = FontAttributes.Bold,
                    FontSize = _fontSize,
                });
                formatted.Spans.Add(new Span { Text = ": " + row.Text, TextColor = text, FontSize = _fontSize });
                break;
            case ChatRowKind.Warning:
                formatted.Spans.Add(new Span { Text = "▲ " + row.Text, TextColor = Theme.Warn.Pick(dark), FontSize = _fontSize * 0.92 });
                break;
            default:
                formatted.Spans.Add(new Span { Text = "• " + row.Text, TextColor = dim, FontSize = _fontSize * 0.92 });
                break;
        }

        _label.FormattedText = formatted;
        // Wer mich erwaehnt, faellt mit einem Hintergrund auf
        BackgroundColor = row.IsMention ? Theme.SelectionSoft.Pick(dark) : Colors.Transparent;
    }
}
