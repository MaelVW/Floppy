namespace FloppyHub.App.Core;

/// <summary>
/// Tastenkuerzel, die die App selbst belegt (Menue in <c>MainWindow</c>: F5 = Aktualisieren, F1 = Ueber).
/// "Sofort beenden" darf keine davon nehmen. Wer im Menue ein Kuerzel aendert, traegt es hier mit ein.
/// </summary>
public static class AppShortcuts
{
    public static readonly string[] InUse = ["F1", "F5"];
}
