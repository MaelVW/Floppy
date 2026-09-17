namespace Floppy.Core.Minigame;

/// <summary>
/// Punkte fuer ein geschafftes Level: Zeit + Effizienz.
/// <code>
/// Punkte = 1000 × Disketten − 5 × Züge − 5 × Schübe − 2 × Sekunden   (mindestens 10 %)
/// </code>
/// Wer mit wenigen Zuegen und schnell fertig ist, bekommt am meisten. Mehr Disketten = mehr
/// moegliche Punkte, damit grosse Level mehr zaehlen.
/// </summary>
public static class GameScoring
{
    /// <summary>Spiel-Kennung in der Rangliste (spaeter kommen weitere Spiele dazu).</summary>
    public const string GameId = "diskettenlager";

    public const int PointsPerBox = 1000;
    public const int MovePenalty = 5;
    public const int PushPenalty = 5;
    public const int SecondPenalty = 2;

    public static int Points(int boxes, int moves, int pushes, TimeSpan time)
    {
        var basis = PointsPerBox * Math.Max(1, boxes);
        var seconds = (long)Math.Round(Math.Max(0, time.TotalSeconds));
        var raw = basis - MovePenalty * (long)moves - PushPenalty * (long)pushes - SecondPenalty * seconds;
        return (int)Math.Max(basis / 10, raw);
    }

    public static int Points(Level level, int moves, int pushes, TimeSpan time) => Points(level.Boxes, moves, pushes, time);

    /// <summary>Zeit wie auf einer Stoppuhr: <c>1:07</c>, ab einer Stunde <c>1:02:07</c>.</summary>
    public static string FormatTime(long millis)
    {
        var t = TimeSpan.FromMilliseconds(Math.Max(0, millis));
        return t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{t.Minutes}:{t.Seconds:00}";
    }
}
