using System.Text.RegularExpressions;
using Floppy.Core.Minigame;

namespace Floppy.Core.Tests;

/// <summary>Sprachdateien der App: Deutsch und Englisch muessen zusammenpassen.</summary>
public class LanguageFileTests
{
    private static string? LangDir()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "app", "src", "FloppyHub.App", "lang");
            if (Directory.Exists(candidate)) return candidate;
        }
        return null;
    }

    private static Dictionary<string, string> Read(string file)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var raw in File.ReadAllLines(file))
        {
            var line = raw.TrimEnd('\r');
            if (line.Length == 0 || line.TrimStart().StartsWith('#')) continue;
            var eq = line.IndexOf('=');
            if (eq <= 0) continue;
            var key = line[..eq].Trim();
            Assert.False(map.ContainsKey(key), $"{Path.GetFileName(file)}: Schluessel doppelt: {key}");
            map[key] = line[(eq + 1)..].Trim();
        }
        return map;
    }

    private static IEnumerable<string> Placeholders(string text) =>
        Regex.Matches(text, @"\{(\d+)\}").Select(m => m.Groups[1].Value).Distinct().Order();

    [Fact]
    public void German_and_English_have_the_same_keys_and_placeholders()
    {
        var dir = LangDir();
        if (dir is null) return;
        var de = Read(Path.Combine(dir, "de.lang"));
        var en = Read(Path.Combine(dir, "en.lang"));

        Assert.Empty(de.Keys.Except(en.Keys));
        Assert.Empty(en.Keys.Except(de.Keys));
        foreach (var (key, text) in de)
        {
            Assert.True(Placeholders(text).SequenceEqual(Placeholders(en[key])), $"Platzhalter passen nicht: {key}");
            Assert.False(string.IsNullOrWhiteSpace(en[key]), $"Englischer Text leer: {key}");
        }
    }

    [Fact]
    public void Built_in_levels_have_translated_names()
    {
        var dir = LangDir();
        var pack = LevelPackTests.BuiltInPackPath();
        if (dir is null || pack is null) return;
        var en = Read(Path.Combine(dir, "en.lang"));
        foreach (var level in LevelPack.Load(pack).Levels)
            Assert.True(en.ContainsKey("LEVELNAME_" + level.Id), $"Kein englischer Name fuer \"{level.Name}\" ({level.Id})");
    }
}
