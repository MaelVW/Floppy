namespace Floppy.Core;

public enum GateAction
{
    /// <summary>Nichts zu tun (leere Diskette, Fehler in game.txt).</summary>
    None,
    StartSteam,
    OpenHub,

    /// <summary>Minispiel in der App oeffnen (reine Daten, keine Freigabe noetig).</summary>
    OpenGame,

    /// <summary>Bekanntes Programm - sofort starten.</summary>
    Start,

    /// <summary>Unbekannt, veraendert oder mehrere Programme - erst fragen.</summary>
    Ask,
}

public sealed record GateDecision(GateAction Action, LaunchPlan? Plan, string? Target, TrustState Trust, string Reason);

/// <summary>
/// Regel der App-Variante (Entscheidung 2026-09-16): Einlegen startet sofort -
/// ausser ein Programm ist neu, veraendert oder nicht eindeutig. Dann wird gefragt.
/// </summary>
public static class LaunchGate
{
    public static GateDecision Decide(LaunchPlan? plan, Func<string, string?, TrustState> checkTrust)
    {
        ArgumentNullException.ThrowIfNull(checkTrust);

        if (plan is null) return new(GateAction.None, null, null, TrustState.Unknown, "nichts Startbares auf der Diskette");

        switch (plan.Kind)
        {
            case LaunchKind.Steam:
                return new(GateAction.StartSteam, plan, null, TrustState.Trusted, "Steam-Spiel");
            case LaunchKind.Hub:
                return new(GateAction.OpenHub, plan, null, TrustState.Trusted, "Hub-Diskette");
            case LaunchKind.Game:
                return new(GateAction.OpenGame, plan, plan.Candidates.FirstOrDefault(), TrustState.Trusted, "Minispiel-Diskette");
        }

        if (plan.Candidates.Count != 1)
            return new(GateAction.Ask, plan, null, TrustState.Unknown, "mehrere Programme - Auswahl noetig");

        var target = plan.Candidates[0];
        TrustState state;
        try { state = checkTrust(target, plan.Arguments); }
        catch { state = TrustState.Unknown; }

        return state switch
        {
            TrustState.Trusted => new(GateAction.Start, plan, target, state, "freigegeben"),
            TrustState.Changed => new(GateAction.Ask, plan, target, state, "Datei hat sich seit der Freigabe veraendert"),
            _ => new(GateAction.Ask, plan, target, state, "zum ersten Mal - noch nicht freigegeben"),
        };
    }
}
