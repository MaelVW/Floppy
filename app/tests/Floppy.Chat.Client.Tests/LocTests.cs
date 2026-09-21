using System.Reflection;
using System.Text.RegularExpressions;
using Floppy.Chat.Client;
using Floppy.Core.Chat;

namespace Floppy.Chat.Client.Tests;

[Collection("Loc")]   // Loc ist global (Sprache) - nicht parallel zu anderen Tests, die Texte pruefen
public class LocTests
{
    [Fact]
    public void German_and_english_load_and_differ()
    {
        Loc.Load("de");
        var de = Loc.T("CHAT_BTN_SEND");
        Assert.Equal("Senden", de);

        Loc.Load("en-US");   // nur die ersten zwei Buchstaben zaehlen
        Assert.Equal("en", Loc.Language);
        Assert.NotEqual(de, Loc.T("CHAT_BTN_SEND"));

        Loc.Load("fr");      // unbekannt -> Deutsch
        Assert.Equal("de", Loc.Language);
        Loc.Load(null);
        Assert.Equal("de", Loc.Language);
    }

    [Fact]
    public void Placeholders_are_filled_and_unknown_keys_come_back_as_key()
    {
        Loc.Load("de");
        Assert.Equal("ID 4827-1935-0062", Loc.T("CHAT_ID", "4827-1935-0062"));
        Assert.Equal("GIBT_ES_NICHT", Loc.T("GIBT_ES_NICHT"));
        Assert.False(Loc.Has("GIBT_ES_NICHT"));
    }

    [Fact]
    public void Mobile_texts_override_and_exist_in_both_languages()
    {
        var de = Loc.Keys("de").Where(k => k.StartsWith("M_", StringComparison.Ordinal)).ToHashSet();
        var en = Loc.Keys("en").Where(k => k.StartsWith("M_", StringComparison.Ordinal)).ToHashSet();
        Assert.NotEmpty(de);
        Assert.Equal(de.OrderBy(k => k), en.OrderBy(k => k));
    }

    [Fact]
    public void Every_chat_notice_has_a_text_in_both_languages()
    {
        var codes = typeof(ChatNotice).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!)
            .ToList();
        Assert.True(codes.Count > 30);

        foreach (var language in Loc.Languages)
        {
            var keys = Loc.Keys(language).ToHashSet();
            var missing = codes.Where(c => !keys.Contains("CHAT_N_" + c.ToUpperInvariant())).ToList();
            Assert.True(missing.Count == 0, $"{language}: Hinweis ohne Text: {string.Join(", ", missing)}");
        }
    }

    [Fact]
    public void Every_key_used_in_the_phone_code_exists()
    {
        var root = FindRepoRoot();
        var files = new[] { "Floppy.Chat.Client", "FloppyChat.Mobile" }
            .Select(d => Path.Combine(root, "app", "src", d))
            .Where(Directory.Exists)
            .SelectMany(d => Directory.EnumerateFiles(d, "*.cs", SearchOption.AllDirectories))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") &&
                        !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .ToList();
        Assert.NotEmpty(files);

        var used = new SortedSet<string>(StringComparer.Ordinal);
        var pattern = new Regex(@"Loc\.T\(""([A-Z][A-Z0-9_]*)""");
        foreach (var file in files)
            foreach (Match m in pattern.Matches(File.ReadAllText(file)))
            {
                // "CHAT_N_" + code und Aehnliches sind nur der Anfang eines Schluessels - dafuer gibt es eigene Tests
                if (!m.Groups[1].Value.EndsWith('_')) used.Add(m.Groups[1].Value);
            }
        Assert.NotEmpty(used);

        foreach (var language in Loc.Languages)
        {
            var keys = Loc.Keys(language).ToHashSet();
            var missing = used.Where(k => !keys.Contains(k)).ToList();
            Assert.True(missing.Count == 0, $"{language}: Text fehlt fuer {string.Join(", ", missing)}");
        }
    }

    private static string FindRepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (Directory.Exists(Path.Combine(dir.FullName, "app", "src", "Floppy.Chat.Client"))) return dir.FullName;
        throw new InvalidOperationException("Repo-Wurzel nicht gefunden.");
    }
}
