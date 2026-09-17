namespace Floppy.Core.Chat;

public sealed record LeaderboardEntry(string Fingerprint, string MemberId, ChatScore Score, int Rank);

/// <summary>Alle Ergebnisse zu einem Level (gleiche Level-ID).</summary>
public sealed record LeaderboardLevel(string Game, string LevelId, string LevelName, IReadOnlyList<LeaderboardEntry> Entries)
{
    /// <summary>Mindestens zwei haben dieses Level gespielt - hier kann man sich vergleichen.</summary>
    public bool IsShared => Entries.Count >= 2;
}

/// <param name="Wins">Wie oft Platz 1 in einem gemeinsamen Level.</param>
public sealed record LeaderboardTotal(string Fingerprint, string MemberId, int Points, int Levels, int Wins, int Rank);

/// <summary>
/// Rangliste im Chatraum: sammelt die besten Minispiel-Ergebnisse der Teilnehmer. Verglichen wird
/// ueber die Level-ID - so zaehlen auch eigene Level aus dem Editor, sobald zwei dasselbe haben.
/// Lebt nur im Speicher, wie der Chat.
/// </summary>
public sealed class ChatLeaderboard
{
    private readonly Dictionary<(string Game, string LevelId), Dictionary<string, (string MemberId, ChatScore Score)>> _levels = new();

    public int Version { get; private set; }

    public bool IsEmpty => _levels.Count == 0;

    public void Clear()
    {
        _levels.Clear();
        Version++;
    }

    public void Add(string fingerprint, string memberId, IEnumerable<ChatScore> scores)
    {
        foreach (var score in scores)
        {
            if (!IsValid(score)) continue;
            var key = (score.Game, score.LevelId.ToUpperInvariant());
            if (!_levels.TryGetValue(key, out var byMember))
            {
                if (_levels.Count >= 500) continue;
                _levels[key] = byMember = new Dictionary<string, (string, ChatScore)>();
            }
            if (byMember.TryGetValue(fingerprint, out var old) && !IsBetter(score, old.Score)) continue;
            byMember[fingerprint] = (memberId, score);
        }
        Version++;
    }

    /// <summary>Mehr Punkte gewinnt; bei Gleichstand weniger Zuege, dann weniger Zeit.</summary>
    public static bool IsBetter(ChatScore a, ChatScore b) =>
        a.Points != b.Points ? a.Points > b.Points :
        a.Moves != b.Moves ? a.Moves < b.Moves :
        a.Millis < b.Millis;

    public static bool IsValid(ChatScore s) =>
        s.Game.Length is > 0 and <= 24 && s.Game.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c == '-') &&
        s.LevelId.Length is >= 8 and <= 24 && s.LevelId.All(char.IsAsciiHexDigit) &&
        s.Moves > 0 && s.Pushes >= 0 && s.Pushes <= s.Moves && s.Millis >= 0 && s.Points >= 0 &&
        s.LevelName.Length <= 40 && !s.LevelName.Any(char.IsControl);

    /// <summary>Gemeinsame Level zuerst, dann nach Name.</summary>
    public IReadOnlyList<LeaderboardLevel> Levels(bool sharedOnly = false)
    {
        var result = new List<LeaderboardLevel>();
        foreach (var ((game, levelId), byMember) in _levels)
        {
            var ordered = byMember
                .OrderBy(e => e.Value.Score, Comparer<ChatScore>.Create((a, b) => IsBetter(a, b) ? -1 : IsBetter(b, a) ? 1 : 0))
                .ToList();
            var entries = new List<LeaderboardEntry>();
            for (var i = 0; i < ordered.Count; i++)
            {
                var (fingerprint, (memberId, score)) = ordered[i];
                var rank = i > 0 && !IsBetter(ordered[i - 1].Value.Score, score) ? entries[i - 1].Rank : i + 1;
                entries.Add(new LeaderboardEntry(fingerprint, memberId, score, rank));
            }
            // Name, den die meisten benutzen (eigene Level koennen unterschiedlich heissen)
            var name = ordered.Select(e => e.Value.Score.LevelName).Where(n => n.Length > 0)
                .GroupBy(n => n).OrderByDescending(g => g.Count()).Select(g => g.Key).FirstOrDefault() ?? levelId;
            var level = new LeaderboardLevel(game, levelId, name, entries);
            if (!sharedOnly || level.IsShared) result.Add(level);
        }
        return result
            .OrderByDescending(l => l.IsShared)
            .ThenBy(l => l.Game, StringComparer.Ordinal)
            .ThenBy(l => l.LevelName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public IReadOnlyList<LeaderboardTotal> Totals(bool sharedOnly = false)
    {
        var totals = new Dictionary<string, (string MemberId, int Points, int Levels, int Wins)>();
        foreach (var level in Levels(sharedOnly))
        {
            foreach (var e in level.Entries)
            {
                var t = totals.GetValueOrDefault(e.Fingerprint, (MemberId: e.MemberId, Points: 0, Levels: 0, Wins: 0));
                totals[e.Fingerprint] = (e.MemberId, t.Points + e.Score.Points, t.Levels + 1,
                    t.Wins + (level.IsShared && e.Rank == 1 ? 1 : 0));
            }
        }

        var ordered = totals.OrderByDescending(t => t.Value.Points).ThenByDescending(t => t.Value.Wins).ToList();
        var result = new List<LeaderboardTotal>();
        for (var i = 0; i < ordered.Count; i++)
        {
            var (fingerprint, (memberId, points, levels, wins)) = ordered[i];
            var rank = i > 0 && result[i - 1].Points == points && result[i - 1].Wins == wins ? result[i - 1].Rank : i + 1;
            result.Add(new LeaderboardTotal(fingerprint, memberId, points, levels, wins, rank));
        }
        return result;
    }
}
