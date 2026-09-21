using System.Text;

namespace Floppy.Core.Tests;

/// <summary>Steams Textformat (.vdf/.acf): muss Steams eigene Dateien lesen und bei Muell nie abstuerzen.</summary>
public class VdfTests
{
    [Fact]
    public void Reads_pairs_and_nested_blocks()
    {
        Assert.True(Vdf.TryParse("""
            "AppState"
            {
                "appid"    "220"
                "name"     "Half-Life 2"
                "UserConfig"
                {
                    "language"    "german"
                }
            }
            """, out var root));

        var state = Assert.Single(root.Children);
        Assert.True(state.IsBlock);
        Assert.Equal("AppState", state.Key);
        Assert.Equal("220", state.GetString("appid"));
        Assert.Equal("Half-Life 2", state.GetString("name"));
        Assert.Equal("german", state.Find("UserConfig")!.GetString("language"));
        Assert.Null(state.GetString("UserConfig"));   // ein Block hat keinen Text
        Assert.Null(state.Find("gibt es nicht"));
    }

    [Fact]
    public void Keys_ignore_case_but_values_stay_exactly_as_written()
    {
        Assert.True(Vdf.TryParse("\"AppState\" { \"StateFlags\" \"4\" \"Name\" \"HeLLo\" }", out var root));
        var state = root.Find("appstate")!;
        Assert.Equal("4", state.GetString("STATEFLAGS"));
        Assert.Equal("HeLLo", state.GetString("name"));
    }

    [Fact]
    public void Resolves_the_escapes_steam_writes()
    {
        Assert.True(Vdf.TryParse(@"""k"" { ""path"" ""C:\\Program Files (x86)\\Steam"" ""q"" ""say \""hi\"""" ""n"" ""a\nb"" ""t"" ""a\tb"" }", out var root));
        var k = root.Find("k")!;
        Assert.Equal(@"C:\Program Files (x86)\Steam", k.GetString("path"));
        Assert.Equal("say \"hi\"", k.GetString("q"));
        Assert.Equal("a\nb", k.GetString("n"));
        Assert.Equal("a\tb", k.GetString("t"));
    }

    [Fact]
    public void Keeps_unknown_escapes_so_old_single_backslash_paths_survive()
    {
        Assert.True(Vdf.TryParse(@"""k"" ""D:\SteamLibrary\Games""", out var root));
        Assert.Equal(@"D:\SteamLibrary\Games", root.GetString("k"));
    }

    [Fact]
    public void Understands_comments_bare_words_and_a_byte_order_mark()
    {
        Assert.True(Vdf.TryParse("\uFEFF// Kommentar\nbare word\n\"a\" \"b\" // noch einer\r\nblock { x y }", out var root));
        Assert.Equal("word", root.GetString("bare"));
        Assert.Equal("b", root.GetString("a"));
        Assert.Equal("y", root.Find("block")!.GetString("x"));
    }

    [Fact]
    public void Quoted_braces_are_text_not_structure()
    {
        Assert.True(Vdf.TryParse("\"a\" \"{\" \"b\" \"}\"", out var root));
        Assert.Equal("{", root.GetString("a"));
        Assert.Equal("}", root.GetString("b"));
    }

    [Fact]
    public void Duplicate_keys_are_all_kept_and_Find_returns_the_first()
    {
        Assert.True(Vdf.TryParse("\"a\" \"1\" \"a\" \"2\"", out var root));
        Assert.Equal(2, root.Children.Count);
        Assert.Equal("1", root.GetString("a"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("// nur ein Kommentar")]
    public void Empty_text_is_valid_and_has_no_children(string text)
    {
        Assert.True(Vdf.TryParse(text, out var root));
        Assert.Empty(root.Children);
    }

    [Theory]
    [InlineData("\"a\" {")]                       // Block nie geschlossen
    [InlineData("\"a\" { \"b\" \"c\"")]           // ebenso, mit Inhalt
    [InlineData("}")]                             // schliessende Klammer ohne oeffnende
    [InlineData("\"a\" \"b\" }")]
    [InlineData("\"a\"")]                         // Schluessel ohne Wert
    [InlineData("\"a\" { \"b\" }")]               // ebenso, im Block
    [InlineData("{ \"a\" \"b\" }")]               // Klammer statt Schluessel
    [InlineData("\"a\" \"nie geschlossen")]       // Text ohne schliessendes Anfuehrungszeichen
    [InlineData("\"a\" \"b\\")]                   // Datei endet mitten in einem Escape
    public void Malformed_text_returns_false(string text)
    {
        Assert.False(Vdf.TryParse(text, out var root));
        Assert.Null(root);
    }

    [Fact]
    public void Null_returns_false()
    {
        Assert.False(Vdf.TryParse(null, out _));
    }

    private static string Nested(int depth) =>
        string.Concat(Enumerable.Repeat("\"a\" { ", depth)) + string.Concat(Enumerable.Repeat("} ", depth));

    [Fact]
    public void Nesting_is_limited_to_protect_the_stack()
    {
        Assert.True(Vdf.TryParse(Nested(Vdf.MaxDepth), out _));
        Assert.False(Vdf.TryParse(Nested(Vdf.MaxDepth + 1), out _));
        Assert.False(Vdf.TryParse(new string('{', 100_000), out _));                       // nichts Rekursives, das den Stapel sprengt
        Assert.False(Vdf.TryParse(string.Concat(Enumerable.Repeat("\"a\" {", 100_000)), out _));
    }

    [Fact]
    public void Random_garbage_never_throws()
    {
        var rng = new Random(20260921);
        const string alphabet = "\"\"\\{}/ \n\t\r\uFEFFabAB019=[]$";
        for (var i = 0; i < 4000; i++)
        {
            var text = new string(Enumerable.Range(0, rng.Next(0, 160)).Select(_ => alphabet[rng.Next(alphabet.Length)]).ToArray());
            _ = Vdf.TryParse(text, out _);   // Ergebnis egal - nur keine Ausnahme
        }
    }

    [Fact]
    public void TryLoad_reads_umlauts_and_trademark_signs_as_utf8()
    {
        using var dir = new TempDisk();
        var file = Path.Combine(dir.Root, "a.acf");
        File.WriteAllText(file, "\"AppState\" { \"name\" \"Battlefield™ 1 Ärger\" }", new UTF8Encoding(false));
        Assert.True(Vdf.TryLoad(file, out var root));
        Assert.Equal("Battlefield™ 1 Ärger", root.Find("AppState")!.GetString("name"));
    }

    [Fact]
    public void TryLoad_reads_a_file_that_steam_still_has_open_for_writing()
    {
        using var dir = new TempDisk();
        var file = dir.Write("busy.acf", "\"AppState\" { \"appid\" \"7\" }");
        using var writer = new FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);
        Assert.True(Vdf.TryLoad(file, out var root));
        Assert.Equal("7", root.Find("AppState")!.GetString("appid"));
    }

    [Fact]
    public void TryLoad_missing_broken_and_oversized_files_give_false()
    {
        using var dir = new TempDisk();
        Assert.False(Vdf.TryLoad(Path.Combine(dir.Root, "gibt-es-nicht.acf"), out _));
        Assert.False(Vdf.TryLoad(Path.Combine(dir.Root, "ungueltig\0name.acf"), out _));
        Assert.False(Vdf.TryLoad(dir.Write("kaputt.acf", "\"AppState\" {"), out _));
        Assert.False(Vdf.TryLoad(dir.Write("zu-gross.acf", new string(' ', Vdf.MaxBytes + 1)), out _));
    }
}

/// <summary>Steam-Bibliothek durchsuchen: findet installierte Spiele in allen Bibliotheksordnern.</summary>
public class SteamScannerTests
{
    private static string Esc(string text) => text.Replace("\\", "\\\\").Replace("\"", "\\\"");

    /// <summary>Ein appmanifest, wie Steam es schreibt (gekuerzt auf die Felder, die uns interessieren).</summary>
    private static string Acf(string appId, string name, int flags = 4, string? installDir = "Game", long size = 123456)
    {
        var sb = new StringBuilder("\"AppState\"\n{\n");
        sb.Append($"\t\"appid\"\t\t\"{appId}\"\n");
        sb.Append($"\t\"name\"\t\t\"{Esc(name)}\"\n");
        sb.Append($"\t\"StateFlags\"\t\t\"{flags}\"\n");
        if (installDir is not null) sb.Append($"\t\"installdir\"\t\t\"{Esc(installDir)}\"\n");
        sb.Append($"\t\"SizeOnDisk\"\t\t\"{size}\"\n}}\n");
        return sb.ToString();
    }

    private static void Manifest(TempDisk library, string appId, string name, int flags = 4, string? installDir = "Game", long size = 123456) =>
        library.Write($"steamapps/appmanifest_{appId}.acf", Acf(appId, name, flags, installDir, size));

    /// <summary>libraryfolders.vdf im neuen Format (Bloecke mit "path").</summary>
    private static void NewFolders(TempDisk steam, params string[] paths)
    {
        var sb = new StringBuilder("\"libraryfolders\"\n{\n");
        for (var i = 0; i < paths.Length; i++)
            sb.Append($"\t\"{i}\"\n\t{{\n\t\t\"path\"\t\t\"{Esc(paths[i])}\"\n\t\t\"label\"\t\t\"\"\n\t\t\"apps\"\n\t\t{{\n\t\t\t\"220\"\t\t\"123\"\n\t\t}}\n\t}}\n");
        sb.Append("}\n");
        steam.Write("steamapps/libraryfolders.vdf", sb.ToString());
    }

    [Fact]
    public void Finds_games_across_all_libraries_sorted_by_name()
    {
        using var steam = new TempDisk();
        using var second = new TempDisk();
        NewFolders(steam, steam.Root.TrimEnd('\\'), second.Root.TrimEnd('\\'));
        Manifest(steam, "220", "Half-Life 2", installDir: "Half-Life 2", size: 5_000_000_000);
        Manifest(steam, "1145360", "Hades");
        Manifest(second, "620", "portal 2");                       // klein geschrieben: sortiert trotzdem hinter "Hades"

        var scan = SteamScanner.Scan(steam.Root);

        Assert.True(scan.SteamFound);
        Assert.Equal(["Hades", "Half-Life 2", "portal 2"], scan.Games.Select(g => g.Name));
        Assert.Equal(["1145360", "220", "620"], scan.Games.Select(g => g.AppId));
        Assert.Equal(2, scan.Libraries.Count);
        Assert.Empty(scan.Unreachable);
        Assert.Equal(0, scan.Tools);
        Assert.Equal(0, scan.Damaged);

        var hl2 = scan.Games.Single(g => g.AppId == "220");
        Assert.Equal(5_000_000_000, hl2.SizeOnDisk);
        Assert.Equal(Path.Combine(steam.Root.TrimEnd('\\'), "steamapps", "common", "Half-Life 2"), hl2.InstallPath);
        Assert.True(hl2.Complete);
        Assert.Equal(second.Root.TrimEnd('\\'), scan.Games.Single(g => g.AppId == "620").LibraryPath);
    }

    [Fact]
    public void Reads_the_old_libraryfolders_format_and_ignores_the_other_numbers_in_it()
    {
        using var steam = new TempDisk();
        using var second = new TempDisk();
        steam.Write("steamapps/libraryfolders.vdf",
            "\"LibraryFolders\"\n{\n\t\"TimeNextStatsReport\"\t\t\"1400000000\"\n\t\"ContentStatsID\"\t\t\"-8214536\"\n" +
            $"\t\"1\"\t\t\"{Esc(second.Root.TrimEnd('\\'))}\"\n}}\n");
        Manifest(second, "400", "Portal");

        var scan = SteamScanner.Scan(steam.Root);

        Assert.Equal(["Portal"], scan.Games.Select(g => g.Name));
        Assert.Empty(scan.Unreachable);          // "TimeNextStatsReport" darf nicht als Ordner durchgehen
        Assert.Equal(2, scan.Libraries.Count);   // der zweite Ordner + der Steam-Ordner selbst
    }

    [Fact]
    public void The_steam_folder_counts_even_without_a_libraryfolders_file()
    {
        using var steam = new TempDisk();
        Manifest(steam, "70", "Half-Life");

        var scan = SteamScanner.Scan(steam.Root);

        Assert.Equal(["Half-Life"], scan.Games.Select(g => g.Name));
        Assert.Equal([steam.Root.TrimEnd('\\')], scan.Libraries);
    }

    [Fact]
    public void An_unplugged_library_is_reported_and_does_not_stop_the_scan()
    {
        using var steam = new TempDisk();
        var gone = Path.Combine(Path.GetTempPath(), "floppy-steam-weg-" + Guid.NewGuid().ToString("N"));
        NewFolders(steam, steam.Root.TrimEnd('\\'), gone);
        Manifest(steam, "70", "Half-Life");

        var scan = SteamScanner.Scan(steam.Root);

        Assert.Equal(["Half-Life"], scan.Games.Select(g => g.Name));
        Assert.Equal([gone], scan.Unreachable);
    }

    [Fact]
    public void The_same_folder_listed_twice_in_different_case_is_scanned_once()
    {
        using var steam = new TempDisk();
        var path = steam.Root.TrimEnd('\\');
        NewFolders(steam, path, path.ToUpperInvariant() + "\\");
        Manifest(steam, "70", "Half-Life");

        var scan = SteamScanner.Scan(steam.Root);

        Assert.Single(scan.Libraries);
        Assert.Single(scan.Games);
    }

    [Fact]
    public void Steam_runtimes_are_left_out_and_counted()
    {
        using var steam = new TempDisk();
        Manifest(steam, "228980", "Steamworks Common Redistributables", installDir: "Steamworks Shared");
        Manifest(steam, "1628350", "Steam Linux Runtime 3.0 (sniper)", installDir: "SteamLinuxRuntime_sniper");
        Manifest(steam, "1493710", "Proton Experimental", installDir: "Proton - Experimental");
        steam.Write("steamapps/common/Proton - Experimental/toolmanifest.vdf", "\"manifest\" { \"version\" \"2\" }");
        Manifest(steam, "999001", "Proton Pulse", installDir: "Proton Pulse");   // ein Spiel, das nur so heisst: bleibt
        Manifest(steam, "1145360", "Hades");

        var scan = SteamScanner.Scan(steam.Root);

        Assert.Equal(["Hades", "Proton Pulse"], scan.Games.Select(g => g.Name));
        Assert.Equal(3, scan.Tools);
        Assert.Equal(0, scan.Damaged);
    }

    [Theory]
    [InlineData("4", true)]       // installiert
    [InlineData("6", true)]       // installiert, ein Update wartet (Albion Online bei Mael)
    [InlineData("1026", false)]   // Update/Download laeuft, noch nicht fertig
    [InlineData("2", false)]
    [InlineData("0", false)]
    [InlineData("abc", false)]
    [InlineData("", false)]
    public void Complete_follows_the_fully_installed_bit(string flags, bool complete)
    {
        using var steam = new TempDisk();
        steam.Write("steamapps/appmanifest_5.acf",
            $"\"AppState\" {{ \"appid\" \"5\" \"name\" \"X\" \"StateFlags\" \"{flags}\" \"installdir\" \"X\" }}");

        Assert.Equal(complete, Assert.Single(SteamScanner.Scan(steam.Root).Games).Complete);
    }

    [Fact]
    public void Without_a_name_the_folder_name_and_then_the_app_id_stand_in()
    {
        using var steam = new TempDisk();
        steam.Write("steamapps/appmanifest_11.acf", "\"AppState\" { \"appid\" \"11\" \"name\" \"  \" \"installdir\" \"Mein Ordner\" \"StateFlags\" \"4\" }");
        steam.Write("steamapps/appmanifest_12.acf", "\"AppState\" { \"appid\" \"12\" \"StateFlags\" \"4\" }");

        var games = SteamScanner.Scan(steam.Root).Games.ToDictionary(g => g.AppId);

        Assert.Equal("Mein Ordner", games["11"].Name);
        Assert.Equal("Steam-App 12", games["12"].Name);
        Assert.Equal("", games["12"].InstallDir);
        Assert.Equal("", games["12"].InstallPath);
    }

    [Fact]
    public void The_app_id_comes_from_the_file_when_present_else_from_the_file_name()
    {
        using var steam = new TempDisk();
        steam.Write("steamapps/appmanifest_100.acf", "\"AppState\" { \"appid\" \"0100\" \"name\" \"Aus der Datei\" }");   // fuehrende Null
        steam.Write("steamapps/appmanifest_200.acf", "\"AppState\" { \"name\" \"Aus dem Dateinamen\" }");
        steam.Write("steamapps/appmanifest_300.acf", "\"AppState\" { \"appid\" \"kaputt\" \"name\" \"Ersatz\" }");

        var games = SteamScanner.Scan(steam.Root).Games.ToDictionary(g => g.Name);

        Assert.Equal("100", games["Aus der Datei"].AppId);
        Assert.Equal("200", games["Aus dem Dateinamen"].AppId);
        Assert.Equal("300", games["Ersatz"].AppId);
    }

    [Fact]
    public void Damaged_manifests_are_counted_and_the_rest_is_still_found()
    {
        using var steam = new TempDisk();
        Manifest(steam, "1", "Gut");
        steam.Write("steamapps/appmanifest_2.acf", "\"AppState\" {\n\t\"appid\"\t\t\"2\"\n\t\"name\"");                       // halb geschrieben
        steam.Write("steamapps/appmanifest_3.acf", "");                                                                     // leer
        steam.Write("steamapps/appmanifest_4.acf", "\"SomethingElse\" { \"appid\" \"4\" }");                                // keine AppState
        steam.Write("steamapps/appmanifest_x.acf", "\"AppState\" { \"name\" \"Ohne Zahl\" }");                              // keine AppID
        steam.Write("steamapps/appmanifest_5.acf", "\"AppState\" { \"appid\" \"99999999999\" \"name\" \"Zu gross\" }");     // AppID ueber 32 Bit - aber der Dateiname sagt 5
        steam.Write("steamapps/notes.txt", "gehoert nicht dazu");

        var scan = SteamScanner.Scan(steam.Root);

        Assert.Equal(["Gut", "Zu gross"], scan.Games.Select(g => g.Name));
        Assert.Equal("5", scan.Games.Single(g => g.Name == "Zu gross").AppId);
        Assert.Equal(4, scan.Damaged);   // halb geschrieben, leer, keine AppState, keine AppID
    }

    [Fact]
    public void The_same_app_in_two_libraries_shows_once_and_the_finished_copy_wins()
    {
        using var steam = new TempDisk();
        using var second = new TempDisk();
        NewFolders(steam, steam.Root.TrimEnd('\\'), second.Root.TrimEnd('\\'));
        Manifest(steam, "70", "Half-Life", flags: 1026);
        Manifest(second, "70", "Half-Life", flags: 4);

        var game = Assert.Single(SteamScanner.Scan(steam.Root).Games);

        Assert.True(game.Complete);
        Assert.Equal(second.Root.TrimEnd('\\'), game.LibraryPath);
    }

    [Fact]
    public void Umlauts_and_trademark_signs_in_names_survive_the_trip_through_the_file()
    {
        using var steam = new TempDisk();
        Manifest(steam, "1238840", "Battlefield™ 1 – Größe \"Alpha\"");   // Steam schreibt UTF-8 ohne BOM, Anfuehrungszeichen als \"

        Assert.Equal("Battlefield™ 1 – Größe \"Alpha\"", Assert.Single(SteamScanner.Scan(steam.Root).Games).Name);
    }

    [Fact]
    public void A_manifest_that_steam_holds_open_is_still_read()
    {
        using var steam = new TempDisk();
        var file = steam.Write("steamapps/appmanifest_9.acf", Acf("9", "Laeuft gerade"));
        using var steamHasItOpen = new FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);

        Assert.Equal(["Laeuft gerade"], SteamScanner.Scan(steam.Root).Games.Select(g => g.Name));
    }

    [Fact]
    public void No_steam_folder_means_NotFound()
    {
        Assert.False(SteamScanner.Scan(null).SteamFound);
        Assert.False(SteamScanner.Scan("").SteamFound);
        Assert.False(SteamScanner.Scan(Path.Combine(Path.GetTempPath(), "floppy-kein-steam-" + Guid.NewGuid().ToString("N"))).SteamFound);
        Assert.Empty(SteamScan.NotFound.Games);
    }

    [Fact]
    public void An_empty_steam_folder_is_found_with_no_games()
    {
        using var steam = new TempDisk();
        var scan = SteamScanner.Scan(steam.Root);
        Assert.True(scan.SteamFound);
        Assert.Empty(scan.Games);
    }

    [Fact]
    public void FindSteamRoot_takes_the_folder_the_user_chose_and_accepts_quotes_and_a_trailing_backslash()
    {
        using var steam = new TempDisk();
        steam.Write("steam.exe", "MZ");                                    // nur steam.exe, noch kein steamapps

        var expected = steam.Root.TrimEnd('\\');
        Assert.Equal(expected, SteamScanner.FindSteamRoot(steam.Root), ignoreCase: true);
        Assert.Equal(expected, SteamScanner.FindSteamRoot($"\"{steam.Root}\""), ignoreCase: true);
        Assert.True(SteamScanner.IsSteamRoot(steam.Root));
    }

    [Fact]
    public void IsSteamRoot_rejects_folders_without_steam_and_garbage()
    {
        using var folder = new TempDisk();
        Assert.False(SteamScanner.IsSteamRoot(folder.Root));
        Assert.False(SteamScanner.IsSteamRoot(null));
        Assert.False(SteamScanner.IsSteamRoot(""));
        Assert.False(SteamScanner.IsSteamRoot("ungueltig\0pfad"));
    }

    [Theory]
    [InlineData("220", "220")]
    [InlineData(" 220 ", "220")]
    [InlineData("0220", "220")]
    [InlineData("4294967295", "4294967295")]
    [InlineData("4294967296", null)]        // groesser als 32 Bit
    [InlineData("0", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    [InlineData("12a", null)]
    [InlineData("-5", null)]
    [InlineData("١٢٣", null)]               // arabisch-indische Ziffern sind keine AppID
    [InlineData("12345678901", null)]
    public void NormalizeAppId_accepts_only_plain_positive_32_bit_numbers(string? input, string? expected) =>
        Assert.Equal(expected, SteamScanner.NormalizeAppId(input));

    [Fact]
    public void Names_are_cleaned_of_control_and_direction_characters_and_cut_short()
    {
        Assert.Equal("Gut so", SteamScanner.CleanName("  Gut \t\r\n so  "));
        Assert.Equal("a b", SteamScanner.CleanName("a\u0000b"));                                  // Steuerzeichen -> Leerzeichen
        Assert.Equal("Spiel gpj.exe", SteamScanner.CleanName("Spiel \u202Egpj.exe"));            // Umkehr der Schreibrichtung: kein Trick mit .exe
        Assert.Equal("A B", SteamScanner.CleanName("A\u200BB"));                                  // unsichtbares Formatzeichen
        Assert.Equal("A B", SteamScanner.CleanName("A\u2028B"));                                  // Zeilentrenner
        Assert.Equal("", SteamScanner.CleanName("   "));
        Assert.Equal("", SteamScanner.CleanName(null));

        var long1 = SteamScanner.CleanName(new string('x', 500));
        Assert.Equal(SteamScanner.MaxNameLength, long1.Length);

        // ein Zeichen ausserhalb der Grundebene darf an der Schnittstelle nicht halbiert werden
        var emoji = SteamScanner.CleanName(new string('x', SteamScanner.MaxNameLength - 1) + "😀");
        Assert.Equal(SteamScanner.MaxNameLength - 1, emoji.Length);
        Assert.False(char.IsHighSurrogate(emoji[^1]));
    }

    // ------------------------------------------------------------------
    // Uebernahme in die Bibliothek
    // ------------------------------------------------------------------

    private static SteamGame Game(string id, string name) => new(id, name, name, @"C:\Steam", 1, true);

    [Fact]
    public void AddToLibrary_adds_only_what_is_missing_and_leaves_existing_entries_alone()
    {
        LibraryEntry[] library =
        [
            new("Mein Half-Life 2", "steam", "220", "2026-01-01", "meine Notiz"),                        // eigener Name + Notiz
            new("Portal (Link)", "steam", "https://store.steampowered.com/app/400/Portal/", "2026-01-02"),   // Wert als Store-Link
            new("Notepad", "pcrun", "620", "2026-01-03"),                                              // andere Art: zaehlt nicht als Steam 620
        ];

        var result = SteamScanner.AddToLibrary(library,
            [Game("220", "Half-Life 2"), Game("400", "Portal"), Game("620", "Portal 2"), Game("1145360", "Hades")],
            "2026-09-21", out var count);

        Assert.Equal(2, count);
        Assert.Equal(5, result.Count);
        Assert.Equal(library, result.Take(3));                                                          // nichts veraendert, nichts umsortiert
        Assert.Equal(new LibraryEntry("Portal 2", "steam", "620", "2026-09-21", SteamScanner.ImportNote), result[3]);
        Assert.Equal(new LibraryEntry("Hades", "steam", "1145360", "2026-09-21", SteamScanner.ImportNote), result[4]);
    }

    [Fact]
    public void AddToLibrary_counts_an_app_only_once_even_if_the_input_repeats_it()
    {
        var result = SteamScanner.AddToLibrary([], [Game("70", "Half-Life"), Game("70", "Half-Life"), Game("220", "Half-Life 2")], "2026-09-21", out var count);
        Assert.Equal(2, count);
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void AddToLibrary_with_nothing_new_returns_zero_and_the_same_entries()
    {
        LibraryEntry[] library = [new("Half-Life", "STEAM", "0070", "2026-01-01")];   // Kind gross, Wert mit fuehrender Null
        var result = SteamScanner.AddToLibrary(library, [Game("70", "Half-Life")], "2026-09-21", out var count);
        Assert.Equal(0, count);
        Assert.Equal(library, result);
    }

    [Fact]
    public void KnownAppIds_reads_plain_ids_links_and_run_uris_of_steam_entries_only()
    {
        var ids = SteamScanner.KnownAppIds(
        [
            new("a", "steam", "220", ""),
            new("b", "steam", "steam://rungameid/400", ""),
            new("c", "steam", "https://store.steampowered.com/app/620/Portal_2/", ""),
            new("d", "pcrun", "730", ""),
            new("e", "steam", "kein Spiel", ""),
        ]);
        Assert.Equal(["220", "400", "620"], ids.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Imported_entries_survive_the_csv_round_trip_with_quotes_commas_and_umlauts()
    {
        using var dir = new TempDisk();
        var file = Path.Combine(dir.Root, "library.csv");
        var result = SteamScanner.AddToLibrary([], [Game("1", "Say \"Hi\", Größe™"), Game("2", "Zeile\r\nzwei")], "2026-09-21", out _);

        LibraryCsv.Write(file, result);
        var back = LibraryCsv.Read(file);

        Assert.Equal(result, back);
    }

    [Fact]
    public void A_scan_end_to_end_lands_in_library_csv()
    {
        using var steam = new TempDisk();
        using var data = new TempDisk();
        Manifest(steam, "1145360", "Hades");
        Manifest(steam, "228980", "Steamworks Common Redistributables");
        var file = Path.Combine(data.Root, "library.csv");
        LibraryCsv.Write(file, [new LibraryEntry("Notepad", "pcrun", "C:\\Windows\\notepad.exe", "2026-01-01")]);

        var count = SteamScanner.AddToLibraryFile(file, SteamScanner.Scan(steam.Root).Games, "2026-09-21");

        Assert.Equal(1, count);
        Assert.Equal(["Notepad", "Hades"], LibraryCsv.Read(file).Select(e => e.Label));
        Assert.Equal(["Notepad"], LibraryCsv.Read(file + ".bak").Select(e => e.Label));   // Sicherung = Stand davor
        Assert.Equal(["library.csv", "library.csv.bak"], Directory.GetFiles(data.Root).Select(Path.GetFileName).Order());
    }

    [Fact]
    public void Adding_to_a_library_file_that_does_not_exist_yet_creates_it_without_a_backup()
    {
        using var data = new TempDisk();
        var file = Path.Combine(data.Root, "neu", "library.csv");   // auch der Ordner fehlt noch

        Assert.Equal(2, SteamScanner.AddToLibraryFile(file, [Game("70", "Half-Life"), Game("220", "Half-Life 2")], "2026-09-21"));

        Assert.Equal(["Half-Life", "Half-Life 2"], LibraryCsv.Read(file).Select(e => e.Label));
        Assert.False(File.Exists(file + ".bak"));
    }

    [Fact]
    public void Adding_nothing_new_leaves_the_file_and_the_old_backup_untouched()
    {
        using var data = new TempDisk();
        var file = Path.Combine(data.Root, "library.csv");
        LibraryCsv.Write(file, [new LibraryEntry("Half-Life", "steam", "70", "2026-01-01")]);
        var before = File.ReadAllBytes(file);

        Assert.Equal(0, SteamScanner.AddToLibraryFile(file, [Game("70", "Half-Life")], "2026-09-21"));

        Assert.Equal(before, File.ReadAllBytes(file));                       // Byte fuer Byte gleich, nicht einmal neu geschrieben
        Assert.False(File.Exists(file + ".bak"));
    }

    [Fact]
    public void Adding_reads_the_file_fresh_so_a_change_made_elsewhere_is_not_lost()
    {
        using var data = new TempDisk();
        var file = Path.Combine(data.Root, "library.csv");
        LibraryCsv.Write(file, [new LibraryEntry("Eins", "pcrun", "a.exe", "x")]);
        var stale = LibraryCsv.Read(file);                                   // Stand, den die Oberflaeche noch im Kopf hat
        LibraryCsv.Write(file, [.. stale, new LibraryEntry("Zwei (extern)", "pcrun", "b.exe", "x")]);   // ein anderes Programm haengt etwas an

        SteamScanner.AddToLibraryFile(file, [Game("70", "Half-Life")], "2026-09-21");

        Assert.Equal(["Eins", "Zwei (extern)", "Half-Life"], LibraryCsv.Read(file).Select(e => e.Label));
    }

    // ------------------------------------------------------------------
    // library.csv sicher schreiben
    // ------------------------------------------------------------------

    [Fact]
    public void Write_replaces_the_file_and_leaves_no_temp_file_behind()
    {
        using var dir = new TempDisk();
        var file = Path.Combine(dir.Root, "library.csv");
        LibraryCsv.Write(file, [new LibraryEntry("Alt", "steam", "1", "x")]);
        LibraryCsv.Write(file, [new LibraryEntry("Neu", "steam", "2", "x")]);

        Assert.Equal(["Neu"], LibraryCsv.Read(file).Select(e => e.Label));
        Assert.Equal(["library.csv"], Directory.GetFiles(dir.Root).Select(Path.GetFileName));
    }

    [Fact]
    public void A_failed_write_keeps_the_old_library_and_cleans_up()
    {
        using var dir = new TempDisk();
        var file = Path.Combine(dir.Root, "library.csv");
        LibraryCsv.Write(file, [new LibraryEntry("Alt", "steam", "1", "x")]);

        // Jemand haelt die Datei offen (ohne Teilen): das Austauschen scheitert.
        using (new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var error = Record.Exception(() => LibraryCsv.Write(file, [new LibraryEntry("Neu", "steam", "2", "x")]));
            Assert.True(error is IOException or UnauthorizedAccessException, $"unerwartet: {error?.GetType().Name ?? "keine Ausnahme"}");   // Windows meldet "Zugriff verweigert"
        }

        Assert.Equal(["Alt"], LibraryCsv.Read(file).Select(e => e.Label));
        Assert.Equal(["library.csv"], Directory.GetFiles(dir.Root).Select(Path.GetFileName));
    }

    [Fact]
    public void Backup_copies_the_file_and_replaces_an_older_backup()
    {
        using var dir = new TempDisk();
        var file = Path.Combine(dir.Root, "library.csv");
        Assert.False(LibraryCsv.Backup(file));                                      // nichts da, nichts zu sichern

        LibraryCsv.Write(file, [new LibraryEntry("Eins", "steam", "1", "x")]);
        Assert.True(LibraryCsv.Backup(file));
        LibraryCsv.Write(file, [new LibraryEntry("Zwei", "steam", "2", "x")]);
        Assert.True(LibraryCsv.Backup(file));

        Assert.Equal(["Zwei"], LibraryCsv.Read(file + ".bak").Select(e => e.Label));
    }
}
