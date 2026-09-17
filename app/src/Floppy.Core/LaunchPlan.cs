namespace Floppy.Core;

/// <summary>Was mit einer Diskette passieren soll.</summary>
public enum LaunchKind
{
    /// <summary>Steam-Spiel ueber steam://rungameid/&lt;id&gt;.</summary>
    Steam,

    /// <summary>Ausfuehrbare Datei (auf der Diskette oder auf dem PC).</summary>
    Process,

    /// <summary>Hub-Diskette: die Floppy-Hub-Oberflaeche oeffnen.</summary>
    Hub,

    /// <summary>Minispiel-Diskette: Levelpaket in der App spielen (nur App-Variante).</summary>
    Game,
}

/// <summary>Ergebnis der Auswertung einer Diskette.</summary>
/// <param name="Kind">Art des Starts.</param>
/// <param name="SteamId">Nur bei <see cref="LaunchKind.Steam"/>.</param>
/// <param name="Candidates">Bei <see cref="LaunchKind.Process"/>: eine oder mehrere Dateien (vollstaendige Pfade).</param>
/// <param name="Arguments">Optionale Startargumente (args=).</param>
/// <param name="NeedsConfirmation">true = nur nach ausdruecklicher Bestaetigung starten (pcrun=, mehrere EXE).</param>
/// <param name="Source">Woher die Angabe kommt (Referenzdatei oder "EXE-Suche").</param>
public sealed record LaunchPlan(
    LaunchKind Kind,
    string? SteamId,
    IReadOnlyList<string> Candidates,
    string? Arguments,
    bool NeedsConfirmation,
    string? Source)
{
    public string SteamUri => Kind == LaunchKind.Steam && SteamId is not null
        ? $"steam://rungameid/{SteamId}"
        : throw new InvalidOperationException("Kein Steam-Plan.");
}

public enum MessageLevel { Info, Ok, Warn, Error }

/// <summary>Meldung fuer Log und Oberflaeche (ASCII-Umschreibungen wie in V1-Logs).</summary>
/// <param name="Text">Deutscher Text (Log, V1-kompatibel).</param>
/// <param name="Code">Kennung fuer die Uebersetzung in der App (<c>CORE_&lt;Code&gt;</c> in der Sprachdatei), sonst <c>null</c>.</param>
/// <param name="Args">Werte fuer die Platzhalter der Uebersetzung.</param>
public sealed record PlanMessage(MessageLevel Level, string Text, string? Code = null, IReadOnlyList<string>? Args = null)
{
    public override string ToString() => $"[{Level.ToString().ToUpperInvariant(),-5}] {Text}";

    public static PlanMessage Of(MessageLevel level, string code, string text, params string[] args) => new(level, text, code, args);
}

/// <summary>Plan (oder null) plus alle Meldungen, die dabei entstanden sind.</summary>
public sealed record PlanResult(LaunchPlan? Plan, IReadOnlyList<PlanMessage> Messages)
{
    public bool HasErrors => Messages.Any(m => m.Level == MessageLevel.Error);
}
