using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Floppy.Core.Chat;

namespace Floppy.Core.Tests;

public class ChatCryptoTests
{
    // PBKDF2 ist absichtlich langsam: Schluessel pro Testlauf nur einmal ableiten.
    internal static readonly ChatRoomKey RoomA = ChatRoomKey.Derive("Schulhof-Diskette-42");
    internal static readonly ChatRoomKey RoomB = ChatRoomKey.Derive("ganz-anderer-Raum");

    [Fact]
    public void Identity_has_readable_id_and_signs()
    {
        using var me = ChatIdentity.CreateNew();
        Assert.Matches(new Regex(@"^\d{4}-\d{4}-\d{4}$"), me.Id);
        Assert.Equal(64, me.Fingerprint.Length);
        Assert.Equal(me.Id, ChatIdentity.IdOf(me.PublicKey));

        var data = "hallo"u8.ToArray();
        var sig = me.Sign(data);
        Assert.True(ChatIdentity.Verify(me.PublicKey, data, sig));
        Assert.False(ChatIdentity.Verify(me.PublicKey, "hallo!"u8.ToArray(), sig));

        using var other = ChatIdentity.CreateNew();
        Assert.False(ChatIdentity.Verify(other.PublicKey, data, sig));
        Assert.NotEqual(me.Id, other.Id);
    }

    [Fact]
    public void Identity_survives_restart_and_is_not_plaintext()
    {
        using var disk = new TempDisk();
        var file = Path.Combine(disk.Root, "chat", ChatIdentity.FileName);

        string id;
        using (var first = ChatIdentity.LoadOrCreate(file)) id = first.Id;
        using (var second = ChatIdentity.LoadOrCreate(file)) Assert.Equal(id, second.Id);

        // Datei ist per DPAPI geschuetzt, kein lesbares PKCS#8
        var bytes = File.ReadAllBytes(file);
        Assert.Throws<CryptographicException>(() => ECDsa.Create().ImportPkcs8PrivateKey(bytes, out _));
    }

    [Fact]
    public void Broken_identity_file_is_backed_up_and_replaced()
    {
        using var disk = new TempDisk();
        var file = disk.Write("identity.bin", "kaputt");
        using var identity = ChatIdentity.LoadOrCreate(file);
        Assert.True(File.Exists(file + ".kaputt"));
        using var again = ChatIdentity.LoadOrCreate(file);
        Assert.Equal(identity.Id, again.Id);
    }

    [Fact]
    public void Local_secret_roundtrip()
    {
        var secret = "geheim"u8.ToArray();
        var packed = LocalSecret.Protect(secret);
        Assert.False(packed.AsSpan().IndexOf(secret) >= 0);
        Assert.Equal(secret, LocalSecret.Unprotect(packed));
        Assert.Null(LocalSecret.Unprotect("kein dpapi"u8.ToArray()));
    }

    [Fact]
    public void Room_key_is_deterministic_and_separated()
    {
        var again = ChatRoomKey.Derive("  Schulhof-Diskette-42 ");   // Leerzeichen am Rand zaehlen nicht
        Assert.Equal(RoomA.Topic, again.Topic);
        Assert.Equal(RoomA.Check, again.Check);
        Assert.Equal(RoomA.AesKey, again.AesKey);

        Assert.NotEqual(RoomA.Topic, RoomB.Topic);
        Assert.NotEqual(RoomA.AesKey, RoomB.AesKey);
        Assert.NotEqual(RoomA.AesKey, RoomA.LanKey);

        Assert.Matches(new Regex("^floppyhub-[a-z2-7]{32}$"), RoomA.Topic);
        Assert.True(RoomA.Topic.Length <= 64);
        Assert.Matches(new Regex("^[2-9A-Z]{3}-[2-9A-Z]{3}$"), RoomA.Check);
        Assert.False(RoomA.IsOpen);
    }

    [Fact]
    public void Open_room_is_open_and_marked()
    {
        Assert.True(ChatRoomKey.OpenRoomAvailable);
        Assert.True(ChatRoomKey.Open.IsOpen);
        Assert.Same(ChatRoomKey.Open, ChatRoomKey.Open);
        Assert.NotEqual(RoomA.Topic, ChatRoomKey.Open.Topic);
    }

    [Theory]
    [InlineData("", SecretProblem.Empty)]
    [InlineData("   ", SecretProblem.Empty)]
    [InlineData("abc", SecretProblem.TooShort)]
    [InlineData("abcdef", SecretProblem.None)]
    [InlineData("abcdef", SecretProblem.InvalidCharacters)]
    public void Secret_problems(string secret, SecretProblem expected) => Assert.Equal(expected, ChatRoomKey.Problem(secret));

    [Fact]
    public void Generated_secrets_are_strong_and_readable()
    {
        var a = ChatRoomKey.Generate();
        Assert.Matches(new Regex("^[2-9A-Z]{4}(-[2-9A-Z]{4}){4}$"), a);
        Assert.DoesNotContain('O', a);
        Assert.DoesNotContain('I', a);
        Assert.NotEqual(a, ChatRoomKey.Generate());
        Assert.False(ChatRoomKey.IsWeak(a));
        Assert.True(ChatRoomKey.IsWeak("123456"));
        Assert.True(ChatRoomKey.IsWeak("passwort"));
    }

    [Fact]
    public void Frame_roundtrip_and_sender_id()
    {
        using var me = ChatIdentity.CreateNew();
        var payload = new ChatPayload { Kind = ChatKinds.Text, Id = ChatFrame.NewId(), Time = 1, Text = "Hallo Diskette 💾" };
        var frame = ChatFrame.Seal(RoomA, me, payload);

        Assert.True(ChatFrame.TryOpen(RoomA, frame, out var env));
        Assert.Equal("Hallo Diskette 💾", env!.Payload.Text);
        Assert.Equal(me.Id, env.SenderId);
        Assert.Equal(me.Fingerprint, env.SenderFingerprint);

        // Klartext steht nicht im Paket
        Assert.DoesNotContain("Hallo", System.Text.Encoding.UTF8.GetString(frame));
    }

    [Fact]
    public void Frame_from_other_room_or_tampered_is_rejected()
    {
        using var me = ChatIdentity.CreateNew();
        var frame = ChatFrame.Seal(RoomA, me, new ChatPayload { Kind = ChatKinds.Text, Id = ChatFrame.NewId(), Text = "x" });

        Assert.False(ChatFrame.TryOpen(RoomB, frame, out _));

        var tampered = (byte[])frame.Clone();
        tampered[20] ^= 1;
        Assert.False(ChatFrame.TryOpen(RoomA, tampered, out _));

        Assert.False(ChatFrame.TryOpen(RoomA, frame.AsSpan(0, 10), out _));
        Assert.False(ChatFrame.TryOpen(RoomA, [], out _));
    }

    [Fact]
    public void Member_of_room_cannot_forge_someone_elses_id()
    {
        // Mallory hat die Verschluesselung und baut ein Paket mit Alices Schluessel, aber eigener Unterschrift.
        using var alice = ChatIdentity.CreateNew();
        using var mallory = ChatIdentity.CreateNew();
        var honest = ChatFrame.Seal(RoomA, mallory, new ChatPayload { Kind = ChatKinds.Text, Id = ChatFrame.NewId(), Text = "Ich bin Alice" });

        var plain = Decrypt(RoomA, honest);
        Assert.Equal(mallory.PublicKey.Length, plain[0]);
        Assert.Equal(alice.PublicKey.Length, mallory.PublicKey.Length);
        alice.PublicKey.CopyTo(plain, 1);   // Absender austauschen
        var forged = Encrypt(RoomA, plain);

        Assert.False(ChatFrame.TryOpen(RoomA, forged, out _));
    }

    [Fact]
    public void Frame_size_is_limited_for_the_service()
    {
        using var me = ChatIdentity.CreateNew();
        var max = ChatFrame.Seal(RoomA, me, new ChatPayload { Kind = ChatKinds.Text, Id = ChatFrame.NewId(), Text = new string('ä', ChatPayload.MaxTextLength) });
        Assert.True(ChatFrame.ToText(max).Length <= 4096);

        Assert.Throws<ChatFrameTooLargeException>(() =>
            ChatFrame.Seal(RoomA, me, new ChatPayload { Kind = ChatKinds.Text, Id = ChatFrame.NewId(), Text = new string('ä', 2000) }));
        Assert.Equal(max, ChatFrame.FromText(ChatFrame.ToText(max)));
        Assert.Null(ChatFrame.FromText("%%%"));
    }

    [Fact]
    public void Payload_validation()
    {
        var ok = new ChatPayload { Kind = ChatKinds.Text, Id = "0123456789ABCDEF", Text = "hi" };
        Assert.True(ok.IsWellFormed());
        Assert.False((ok with { Kind = "exec" }).IsWellFormed());
        Assert.False((ok with { Id = "nope" }).IsWellFormed());
        Assert.False((ok with { Text = " " }).IsWellFormed());
        Assert.False((ok with { Text = new string('x', 501) }).IsWellFormed());
        Assert.False((ok with { Kind = ChatKinds.Propose }).IsWellFormed());
        Assert.True((ok with { Kind = ChatKinds.Propose, Proposal = "AB12AB12", Endpoints = ["10.0.0.1:45817"] }).IsWellFormed());
        Assert.False((ok with { Kind = ChatKinds.Vote, Proposal = "AB12AB12" }).IsWellFormed());
    }

    [Fact]
    public void Key_file_roundtrip_and_search()
    {
        using var disk = new TempDisk();
        ChatKeyFile.Write(disk.Root, "K7QM-P2XD-9HVT-R4WN");
        Assert.Equal("K7QM-P2XD-9HVT-R4WN", ChatKeyFile.Read(Path.Combine(disk.Root, ChatKeyFile.FileName)));
        Assert.Null(ChatKeyFile.Parse("# nur Kommentar\nchatkey = abc"));   // zu kurz
        Assert.Equal("mein Raum 1", ChatKeyFile.Parse("; x\r\nCHATKEY=  mein Raum 1  \r\n"));

        var found = ChatKeyFile.FindOn([disk.Root, Path.Combine(disk.Root, "gibtsnicht")]);
        Assert.Single(found);
        Assert.Equal("K7QM-P2XD-9HVT-R4WN", found[0].Secret);
    }

    [Fact]
    public void Contacts_store_name_id_and_protected_secret()
    {
        using var disk = new TempDisk();
        using var friend = ChatIdentity.CreateNew();
        var book = new ChatContactBook(Path.Combine(disk.Root, "chat", ChatContactBook.FileName));

        book.Save("  Mael\tvom Schulhof ", friend.Id, friend.Fingerprint, "Schulhof-Diskette-42");
        var c = book.Find(friend.Fingerprint.ToLowerInvariant());
        Assert.NotNull(c);
        Assert.Equal("Mael vom Schulhof", c!.Name);
        Assert.Equal(friend.Id, c.MemberId);
        Assert.True(c.HasSecret);
        Assert.Equal("Schulhof-Diskette-42", ChatContactBook.RevealSecret(c));
        Assert.DoesNotContain("Schulhof-Diskette", File.ReadAllText(book.FilePath));

        book.Save("Mael", friend.Id, friend.Fingerprint, secret: null);   // Name aendern, Schluessel behalten
        Assert.Equal("Schulhof-Diskette-42", ChatContactBook.RevealSecret(book.Find(friend.Fingerprint)!));
        Assert.Single(book.Load());

        Assert.True(book.Remove(friend.Fingerprint));
        Assert.Empty(book.Load());
    }

    internal static byte[] Decrypt(ChatRoomKey room, byte[] frame)
    {
        var plain = new byte[frame.Length - ChatFrame.Overhead];
        using var aes = new AesGcm(room.AesKey, 16);
        aes.Decrypt(frame.AsSpan(3, 12), frame.AsSpan(15, plain.Length), frame.AsSpan(frame.Length - 16), plain, frame.AsSpan(0, 3));
        return plain;
    }

    internal static byte[] Encrypt(ChatRoomKey room, byte[] plain)
    {
        var frame = new byte[ChatFrame.Overhead + plain.Length];
        frame[0] = (byte)'F';
        frame[1] = (byte)'H';
        frame[2] = 1;
        RandomNumberGenerator.Fill(frame.AsSpan(3, 12));
        using var aes = new AesGcm(room.AesKey, 16);
        aes.Encrypt(frame.AsSpan(3, 12), plain, frame.AsSpan(15, plain.Length), frame.AsSpan(frame.Length - 16), frame.AsSpan(0, 3));
        return frame;
    }
}

public class ChatLeaderboardTests
{
    private static ChatScore S(string level, int points, int moves = 10, long ms = 5000, string name = "L") =>
        new() { Game = "diskettenlager", LevelId = level, LevelName = name, Moves = moves, Pushes = 2, Millis = ms, Points = points };

    [Fact]
    public void Keeps_best_per_member_and_ranks_shared_levels_first()
    {
        var board = new ChatLeaderboard();
        board.Add("A", "1111-1111-1111", [S("AAAAAAAAAAAA", 900, name: "Erste Diskette"), S("BBBBBBBBBBBB", 500)]);
        board.Add("A", "1111-1111-1111", [S("AAAAAAAAAAAA", 800)]);   // schlechter: bleibt 900
        board.Add("B", "2222-2222-2222", [S("aaaaaaaaaaaa", 950, name: "Erste Diskette"), S("CCCCCCCCCCCC", 100)]);

        var levels = board.Levels();
        Assert.Equal(3, levels.Count);
        var shared = levels[0];
        Assert.True(shared.IsShared);
        Assert.Equal("Erste Diskette", shared.LevelName);
        Assert.Equal(["B", "A"], shared.Entries.Select(e => e.Fingerprint));
        Assert.Equal(900, shared.Entries[1].Score.Points);
        Assert.Single(board.Levels(sharedOnly: true));

        var totals = board.Totals();
        Assert.Equal("A", totals[0].Fingerprint);   // 900 + 500
        Assert.Equal(1400, totals[0].Points);
        Assert.Equal(0, totals[0].Wins);
        Assert.Equal(1, totals[1].Wins);

        var sharedTotals = board.Totals(sharedOnly: true);
        Assert.Equal("B", sharedTotals[0].Fingerprint);
    }

    [Fact]
    public void Ties_share_rank_and_moves_break_equal_points()
    {
        var board = new ChatLeaderboard();
        board.Add("A", "1", [S("AAAAAAAAAAAA", 900, moves: 20)]);
        board.Add("B", "2", [S("AAAAAAAAAAAA", 900, moves: 18)]);
        board.Add("C", "3", [S("AAAAAAAAAAAA", 900, moves: 18)]);
        var entries = board.Levels()[0].Entries;
        Assert.Equal([1, 1, 3], entries.Select(e => e.Rank));
        Assert.Equal("A", entries[2].Fingerprint);
    }

    [Fact]
    public void Invalid_scores_are_ignored()
    {
        var board = new ChatLeaderboard();
        board.Add("A", "1", [
            S("nicht-hex!!!", 10),
            S("AAAAAAAAAAAA", 10) with { Moves = 0 },
            S("AAAAAAAAAAAA", 10) with { Game = "Rm -rf" },
            S("AAAAAAAAAAAA", 10) with { Pushes = 99 },
        ]);
        Assert.True(board.IsEmpty);
    }
}

public class ChatSessionTests
{
    private DateTimeOffset _now = new(2026, 9, 16, 12, 0, 0, TimeSpan.Zero);
    private readonly InMemoryChatHub _hub = new();
    private readonly List<ChatSession> _sessions = [];

    private (ChatSession Session, InMemoryChatNetwork Net) Join(Func<IReadOnlyList<ChatScore>>? scores = null, ChatRoomKey? room = null, bool sameLan = true)
    {
        var net = new InMemoryChatNetwork(_hub) { SameLan = sameLan };
        var s = new ChatSession(ChatIdentity.CreateNew(), room ?? ChatCryptoTests.RoomA, net, scores);
        s.Start(_now);
        _sessions.Add(s);
        Settle(() => s.State == ChatSessionState.Connected);
        return (s, net);
    }

    /// <summary>Alle Sitzungen pumpen, bis die Bedingung stimmt (Netzwerk-Rueckmeldungen kommen asynchron).</summary>
    private void Settle(Func<bool>? until = null, int ms = 3000)
    {
        // ohne Bedingung: nur kurz, bis die Warteschlangen leer sind
        var deadline = DateTime.UtcNow.AddMilliseconds(until is null ? 40 : ms);
        do
        {
            foreach (var s in _sessions) s.Pump(_now);
            if (until?.Invoke() == true) return;
            Thread.Sleep(2);
        } while (DateTime.UtcNow < deadline);
        if (until is not null) Assert.Fail("Bedingung nicht erreicht.");
    }

    private void Advance(double seconds)
    {
        Settle();   // erst alles zum alten Zeitpunkt verarbeiten (Zeitgeber starten dort)
        _now = _now.AddSeconds(seconds);
        Settle();
    }

    private static bool HasNotice(ChatSession s, string key) => s.Lines.Any(l => l.Kind is ChatLineKind.Notice or ChatLineKind.Warning && l.Text == key);

    [Fact]
    public void Two_members_see_each_other_and_chat()
    {
        var (a, _) = Join();
        var (b, _) = Join();
        Advance(3);   // "here"-Antworten kommen verzoegert

        Assert.Equal(2, a.Members.Count);
        Assert.Equal(2, b.Members.Count);
        Assert.Contains(a.Lines, l => l.Text == ChatNotice.Joined && l.MemberId == b.Me.Id);
        Assert.Equal(ChatMode.Online, a.Mode);

        Assert.Equal(ChatResult.Ok, a.SendText("  Hallo\nB!  ", _now));
        Settle(() => b.Lines.Any(l => l.Kind == ChatLineKind.Theirs));
        var line = b.Lines.Single(l => l.Kind == ChatLineKind.Theirs);
        Assert.Equal("Hallo B!", line.Text);
        Assert.Equal(a.Me.Id, line.MemberId);
        Assert.Single(a.Lines, l => l.Kind == ChatLineKind.Mine);   // eigenes Echo vom Dienst nicht doppelt
        Assert.DoesNotContain(a.Lines, l => l.Kind == ChatLineKind.Theirs);
    }

    [Fact]
    public void Other_rooms_stay_separate()
    {
        var (a, _) = Join();
        var (b, _) = Join(room: ChatCryptoTests.RoomB);
        Advance(3);
        a.SendText("geheim", _now);
        Advance(1);
        Assert.Single(a.Members);
        Assert.DoesNotContain(b.Lines, l => l.Kind == ChatLineKind.Theirs);
    }

    [Fact]
    public void Leaving_is_announced()
    {
        var (a, _) = Join();
        var (b, _) = Join();
        Advance(3);
        b.Leave(_now);
        Settle(() => a.Members.Count == 1);
        Assert.True(HasNotice(a, ChatNotice.Left));
        Assert.Equal(ChatSessionState.Ended, b.State);
        Assert.Equal(ChatResult.NotConnected, b.SendText("noch da?", _now));
    }

    [Fact]
    public void Approvers_move_to_local_and_others_are_excluded_after_countdown()
    {
        var (a, _) = Join();
        var (b, _) = Join();
        var (c, _) = Join();
        Advance(3);

        Assert.Equal(ChatResult.Ok, a.ProposeLocal(_now));
        Settle(() => b.Proposal is { Stage: ProposalStage.Asking } && c.Proposal is { Stage: ProposalStage.Asking });
        Assert.Equal(a.Me.Id, b.Proposal!.ProposerId);
        Assert.Equal(ChatResult.Busy, b.ProposeLocal(_now));

        b.AnswerProposal(true, _now);
        Settle(() => b.Mode == ChatMode.Local && a.Mode == ChatMode.Local);
        Assert.True(HasNotice(a, ChatNotice.SwitchedByYou));
        Settle(() => c.Proposal is { Stage: ProposalStage.Countdown });
        Assert.Equal(ChatMode.Online, c.Mode);

        // lokal geht es weiter
        b.SendText("jetzt ohne Dienst", _now);
        Settle(() => a.Lines.Any(l => l.Kind == ChatLineKind.Theirs && l.Text == "jetzt ohne Dienst"));
        Assert.DoesNotContain(c.Lines, l => l.Text == "jetzt ohne Dienst");

        Advance(9);
        Assert.Equal(ChatSessionState.Connected, c.State);
        Advance(1.5);
        Assert.Equal(ChatSessionState.Ended, c.State);
        Assert.Equal(ChatNotice.Excluded, c.EndReason);

        Advance(2);
        Assert.Equal(2, a.Members.Count);   // C ist aus der Liste
        Assert.True(HasNotice(a, ChatNotice.StayedOnline));
    }

    [Fact]
    public void Latecomer_can_still_follow_during_countdown()
    {
        var (a, _) = Join();
        var (b, _) = Join();
        var (c, _) = Join();
        Advance(3);
        a.ProposeLocal(_now);
        Settle(() => c.Proposal is not null && b.Proposal is not null);
        c.AnswerProposal(false, _now);
        b.AnswerProposal(true, _now);
        Settle(() => c.Proposal is { Stage: ProposalStage.Countdown });

        Advance(5);
        c.AnswerProposal(true, _now);   // doch mitkommen
        Settle(() => c.Mode == ChatMode.Local);
        Advance(10);
        Assert.Equal(ChatSessionState.Connected, c.State);
        Assert.Equal(3, a.Members.Count);
    }

    [Fact]
    public void All_declining_keeps_everyone_online()
    {
        var (a, _) = Join();
        var (b, _) = Join();
        var (c, _) = Join();
        Advance(3);
        a.ProposeLocal(_now);
        Settle(() => b.Proposal is not null && c.Proposal is not null);

        b.AnswerProposal(false, _now);
        Settle(() => HasNotice(a, ChatNotice.VoteNo));
        Assert.NotNull(a.Proposal);
        c.AnswerProposal(false, _now);
        Settle(() => a.Proposal is null && b.Proposal is null && c.Proposal is null);

        Assert.True(HasNotice(a, ChatNotice.ProposalRejectedMine));
        Assert.True(HasNotice(b, ChatNotice.ProposalRejected));
        Assert.All(new[] { a, b, c }, s => Assert.Equal(ChatMode.Online, s.Mode));
        Assert.Equal(0, _hub.LocalRooms);   // lokaler Raum wieder zu

        a.SendText("immer noch online", _now);
        Settle(() => c.Lines.Any(l => l.Text == "immer noch online"));
    }

    [Fact]
    public void Nobody_answering_times_out()
    {
        var (a, _) = Join();
        var (b, _) = Join();
        Advance(3);
        a.ProposeLocal(_now);
        Settle(() => b.Proposal is not null);
        Advance(ChatSession.ProposalSeconds + 1);
        Assert.Null(a.Proposal);
        Assert.True(HasNotice(a, ChatNotice.ProposalTimeoutMine));
        Settle(() => b.Proposal is null);
        Assert.Equal(ChatMode.Online, b.Mode);
    }

    [Fact]
    public void Approver_in_other_network_counts_as_no()
    {
        var (a, _) = Join();
        var (b, _) = Join(sameLan: false);
        Advance(3);
        a.ProposeLocal(_now);
        Settle(() => b.Proposal is not null);
        b.AnswerProposal(true, _now);
        Settle(() => HasNotice(b, ChatNotice.LocalUnreachable));
        Settle(() => a.Proposal is null);
        Assert.True(HasNotice(a, ChatNotice.VoteUnreachable));
        Assert.Equal(ChatMode.Online, b.Mode);
        Assert.Equal(ChatSessionState.Connected, b.State);
    }

    [Fact]
    public void Alone_cannot_propose()
    {
        var (a, _) = Join();
        Assert.Equal(ChatResult.Alone, a.ProposeLocal(_now));
    }

    [Fact]
    public void Joining_later_finds_the_local_room()
    {
        var (a, _) = Join();
        var (b, _) = Join();
        Advance(3);
        a.ProposeLocal(_now);
        Settle(() => b.Proposal is not null);
        b.AnswerProposal(true, _now);
        Settle(() => a.Mode == ChatMode.Local && b.Mode == ChatMode.Local);
        Advance(15);

        var framesBefore = _hub.OnlineFrames;
        var (d, _) = Join();
        Assert.Equal(ChatMode.Local, d.Mode);
        Assert.True(HasNotice(d, ChatNotice.FoundLocal));
        Settle(() => d.Members.Count == 3);
        Assert.Equal(framesBefore, _hub.OnlineFrames);   // ganz ohne Dienst
    }

    [Fact]
    public void Leaderboard_collects_scores_from_room()
    {
        var level = new ChatScore { Game = "diskettenlager", LevelId = "0123456789AB", LevelName = "Erste Diskette", Moves = 3, Pushes = 1, Millis = 2100, Points = 978 };
        var (a, _) = Join(() => [level]);
        var (b, _) = Join(() => [level with { Moves = 5, Points = 960 }]);
        Advance(3);

        Assert.Equal(ChatResult.Ok, a.RequestScores(_now));
        Assert.Equal(ChatResult.TooFast, a.RequestScores(_now));
        Advance(2.5);
        Settle(() => a.Leaderboard.Levels().FirstOrDefault()?.Entries.Count == 2);

        var entries = a.Leaderboard.Levels()[0].Entries;
        Assert.Equal(a.Me.Fingerprint, entries[0].Fingerprint);
        Assert.Equal(b.Me.Id, entries[1].MemberId);
    }

    [Fact]
    public void Online_sending_is_throttled()
    {
        var (a, _) = Join();
        Advance(1);
        var results = Enumerable.Range(0, 20).Select(i => a.SendText($"Nachricht {i}", _now)).ToList();
        Assert.Contains(ChatResult.TooFast, results);
        Assert.True(results.Count(r => r == ChatResult.Ok) < ChatSession.OnlineMessagesPerMinute);
        Advance(61);
        Assert.Equal(ChatResult.Ok, a.SendText("wieder erlaubt", _now));
    }

    [Fact]
    public void Garbage_and_replays_are_ignored()
    {
        var (a, _) = Join();
        var (b, _) = Join();
        Advance(3);

        var topic = ChatCryptoTests.RoomA.Topic;
        _hub.Inject(topic, [1, 2, 3]);
        using var stranger = ChatIdentity.CreateNew();
        var foreign = ChatFrame.Seal(ChatCryptoTests.RoomB, stranger, new ChatPayload { Kind = ChatKinds.Text, Id = ChatFrame.NewId(), Time = _now.ToUnixTimeMilliseconds(), Text = "falscher Raum" });
        _hub.Inject(topic, foreign);
        var old = ChatFrame.Seal(ChatCryptoTests.RoomA, stranger, new ChatPayload { Kind = ChatKinds.Text, Id = ChatFrame.NewId(), Time = _now.AddHours(-2).ToUnixTimeMilliseconds(), Text = "von gestern" });
        _hub.Inject(topic, old);
        var fresh = ChatFrame.Seal(ChatCryptoTests.RoomA, stranger, new ChatPayload { Kind = ChatKinds.Text, Id = ChatFrame.NewId(), Time = _now.ToUnixTimeMilliseconds(), Text = "einmal" });
        _hub.Inject(topic, fresh);
        _hub.Inject(topic, fresh);   // Wiederholung
        Advance(1);

        Assert.Single(a.Lines, l => l.Kind == ChatLineKind.Theirs);
        Assert.Single(b.Lines, l => l.Kind == ChatLineKind.Theirs && l.Text == "einmal");
    }

    [Fact]
    public void Clean_text_removes_control_characters()
    {
        Assert.Equal("a b c", ChatSession.CleanText("a b\r\n\tc "));
        Assert.Equal("", ChatSession.CleanText(null));
    }
}
