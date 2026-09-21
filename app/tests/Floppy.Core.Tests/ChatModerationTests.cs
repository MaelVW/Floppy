using System.Security.Cryptography;
using System.Text;
using Floppy.Core.Chat;

namespace Floppy.Core.Tests;

public class ChatAdminsTests
{
    [Fact]
    public void Only_full_fingerprints_of_listed_admins_count()
    {
        Assert.NotEmpty(ChatAdmins.All);
        foreach (var fp in ChatAdmins.All)
        {
            Assert.Equal(64, fp.Length);
            Assert.All(fp, c => Assert.True(char.IsAsciiHexDigit(c)));
            Assert.True(ChatAdmins.IsAdmin(fp));
            Assert.True(ChatAdmins.IsAdmin(fp.ToLowerInvariant()));
            Assert.False(ChatAdmins.IsAdmin(fp[..12]));   // die kurze ID-Nummer reicht nie
        }
        Assert.False(ChatAdmins.IsAdmin(null));
        Assert.False(ChatAdmins.IsAdmin(""));
        Assert.False(ChatAdmins.IsAdmin(ChatIdentity.CreateNew().Fingerprint));
    }
}

public class ChatBanListTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 21, 12, 0, 0, TimeSpan.Zero);
    private static string Fp(char c) => new(c, 64);

    [Fact]
    public void Newest_decision_wins_and_lifted_bans_stay_lifted()
    {
        var list = new ChatBanList();
        var t = Now.ToUnixTimeMilliseconds();
        Assert.True(list.Apply(new ChatBan(Fp('A'), "1111-2222-3333", 0, t, "Grund", false)));
        Assert.True(list.IsBanned(Fp('A'), Now, out var ban));
        Assert.Equal("Grund", ban!.Reason);
        Assert.False(list.Apply(new ChatBan(Fp('A'), "1111-2222-3333", 0, t, "Grund", false)));   // gleich alt: nichts neues

        Assert.True(list.Lift(Fp('A'), "", t + 1000));
        Assert.False(list.IsBanned(Fp('A'), Now, out _));

        // dieselbe (aeltere) Sperre taucht noch einmal auf - zaehlt nicht
        Assert.False(list.Apply(new ChatBan(Fp('A'), "1111-2222-3333", 0, t, "Grund", false)));
        Assert.False(list.IsBanned(Fp('A'), Now, out _));
    }

    [Fact]
    public void Temporary_bans_run_out()
    {
        var list = new ChatBanList();
        var until = Now.AddHours(1).ToUnixTimeMilliseconds();
        list.Apply(new ChatBan(Fp('B'), "", until, Now.ToUnixTimeMilliseconds(), "", false));
        Assert.True(list.IsBanned(Fp('B'), Now.AddMinutes(59), out _));
        Assert.False(list.IsBanned(Fp('B'), Now.AddMinutes(61), out _));
        Assert.Empty(list.Active(Now.AddHours(2)));
    }

    [Fact]
    public void Bans_survive_a_restart_and_odd_characters_cannot_break_the_file()
    {
        var dir = Path.Combine(Path.GetTempPath(), "floppy-bans-" + Guid.NewGuid());
        var file = Path.Combine(dir, "chat", ChatBanList.FileName);
        try
        {
            var list = new ChatBanList(file);
            list.Apply(new ChatBan(Fp('C'), "9999-8888-7777", 0, Now.ToUnixTimeMilliseconds(), "zwei\tSpalten\nund Zeile", false));
            list.Apply(new ChatBan(Fp('D'), "", Now.AddDays(1).ToUnixTimeMilliseconds(), Now.ToUnixTimeMilliseconds(), "", false));
            list.Lift(Fp('D'), "", Now.ToUnixTimeMilliseconds() + 5);

            var reloaded = new ChatBanList(file, Now);
            Assert.True(reloaded.IsBanned(Fp('C'), Now, out var c));
            Assert.Equal("9999-8888-7777", c!.MemberId);
            Assert.DoesNotContain('\t', c.Reason);
            Assert.DoesNotContain('\n', c.Reason);
            Assert.False(reloaded.IsBanned(Fp('D'), Now, out _));

            // kaputte Zeilen werden uebergangen
            File.AppendAllText(file, "Unsinn\n\t\t\t\t\nZZZ\t\t0\t0\t0\t\n");
            Assert.True(new ChatBanList(file, Now).IsBanned(Fp('C'), Now, out _));

            // alte, aufgehobene Eintraege verschwinden mit der Zeit; aktive dauerhafte bleiben
            var later = new ChatBanList(file, Now.AddDays(60));
            Assert.True(later.IsBanned(Fp('C'), Now.AddDays(60), out _));
            Assert.DoesNotContain(later.Active(Now.AddDays(60)), b => b.Fingerprint == Fp('D'));
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }
}

public class ChatModerationTests
{
    private DateTimeOffset _now = new(2026, 9, 21, 12, 0, 0, TimeSpan.Zero);
    private readonly InMemoryChatHub _hub = new();
    private readonly List<ChatSession> _sessions = [];
    private readonly ChatIdentity _admin = ChatIdentity.CreateNew();
    private static readonly ChatRoomKey Open = ChatRoomKey.Open;

    private ChatSession Join(ChatIdentity? identity = null, ChatRoomKey? room = null, ChatBanList? bans = null)
    {
        var net = new InMemoryChatNetwork(_hub);
        var s = new ChatSession(identity ?? ChatIdentity.CreateNew(), room ?? Open, net, null, bans, fp => fp == _admin.Fingerprint);
        s.Start(_now);
        _sessions.Add(s);
        Settle(() => s.State == ChatSessionState.Connected);
        return s;
    }

    private void Settle(Func<bool>? until = null, int ms = 3000)
    {
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
        Settle();
        _now = _now.AddSeconds(seconds);
        Settle();
    }

    /// <summary>Eine Nachricht im Namen von <paramref name="who"/> direkt in den Raum kippen (wie ein veraenderter Client).</summary>
    private void Inject(ChatIdentity who, ChatPayload payload, ChatRoomKey? room = null)
    {
        room ??= Open;
        var frame = ChatFrame.Seal(room, who, payload with { Id = ChatFrame.NewId(), Time = _now.ToUnixTimeMilliseconds() });
        _hub.Inject(room.Topic, frame);
        Settle();
    }

    private ChatPayload BanOf(ChatSession target, string? reason = null, long? at = null, string kind = ChatKinds.Ban) => new()
    {
        Kind = kind,
        Bans = [new ChatBanEntry { Fingerprint = target.Me.Fingerprint, MemberId = target.Me.Id, At = at ?? _now.ToUnixTimeMilliseconds(), Reason = reason }],
    };

    private static bool HasNotice(ChatSession s, string key) => s.Lines.Any(l => l.Kind is ChatLineKind.Notice or ChatLineKind.Warning && l.Text == key);

    [Fact]
    public void Admin_ban_removes_the_member_hides_their_lines_and_mutes_their_session()
    {
        var admin = Join(_admin);
        var b = Join();
        var c = Join();
        Advance(3);
        c.SendText("etwas Anstoessiges", _now);
        Settle(() => b.Lines.Any(l => l.Kind == ChatLineKind.Theirs) && admin.Lines.Any(l => l.Kind == ChatLineKind.Theirs));

        Assert.Equal(ChatResult.Ok, admin.BanMember(c.Me.Fingerprint, "  Regel\nverstoss ", null, _now));
        Settle(() => c.IsBanned && !b.Members.Any(m => m.Fingerprint == c.Me.Fingerprint));

        Assert.Equal(ChatSessionState.Connected, c.State);   // bleibt verbunden - nur so erfaehrt sie von einer Aufhebung
        Assert.Equal(ChatResult.Banned, c.SendText("noch da?", _now));
        Assert.True(HasNotice(c, ChatNotice.YouBannedReason));
        Assert.True(b.Bans.IsBanned(c.Me.Fingerprint, _now, out var ban));
        Assert.Equal("Regel verstoss", ban!.Reason);
        Assert.DoesNotContain(b.Lines, l => l.Kind == ChatLineKind.Theirs);          // ausgeblendet
        Assert.DoesNotContain(admin.Lines, l => l.Kind == ChatLineKind.Theirs);
        Assert.Contains(b.Lines, l => l.Text == ChatNotice.BannedReason && l.Args is [{ } r] && r == "Regel verstoss");
        Assert.Contains(admin.Lines, l => l.Text == ChatNotice.BannedReason);
    }

    [Fact]
    public void Nothing_from_a_banned_identity_is_shown_or_counted_any_more()
    {
        var admin = Join(_admin);
        var b = Join();
        var badId = ChatIdentity.CreateNew();
        var c = Join(badId);
        Advance(3);
        admin.BanMember(c.Me.Fingerprint, null, null, _now);
        Settle(() => c.IsBanned && b.Bans.IsBanned(badId.Fingerprint, _now, out _));

        // ein veraenderter Client versucht es trotzdem
        Inject(badId, new ChatPayload { Kind = ChatKinds.Join, Mode = "online" });
        Inject(badId, new ChatPayload { Kind = ChatKinds.Text, Text = "ich bin wieder da" });
        Advance(1);

        Assert.DoesNotContain(b.Lines, l => l.Kind == ChatLineKind.Theirs);
        Assert.DoesNotContain(b.Members, m => m.Fingerprint == badId.Fingerprint);
    }

    [Fact]
    public void A_ban_from_someone_who_is_not_an_admin_is_ignored()
    {
        var admin = Join(_admin);
        var b = Join();
        var c = Join();
        Advance(3);

        Assert.Equal(ChatResult.NotAllowed, b.BanMember(c.Me.Fingerprint, "ich will das", null, _now));   // Bedienung
        Inject(b.Me, BanOf(c, "gefaelscht"));                                                                 // Protokoll
        Advance(1);

        Assert.Equal(ChatSessionState.Connected, c.State);
        Assert.False(admin.Bans.IsBanned(c.Me.Fingerprint, _now, out _));
        Assert.Contains(admin.Members, m => m.Fingerprint == c.Me.Fingerprint);
    }

    [Fact]
    public void Bans_only_exist_in_the_open_room()
    {
        var privateRoom = ChatCryptoTests.RoomA;
        var admin = Join(_admin, privateRoom);
        var c = Join(room: privateRoom);
        Advance(3);

        Assert.Equal(ChatResult.NotAllowed, admin.BanMember(c.Me.Fingerprint, null, null, _now));
        Inject(_admin, BanOf(c), privateRoom);   // selbst mit echter Admin-Unterschrift
        Advance(1);
        Assert.Equal(ChatSessionState.Connected, c.State);
        Assert.False(c.Bans.IsBanned(c.Me.Fingerprint, _now, out _));
    }

    [Fact]
    public void Admins_cannot_be_banned_and_cannot_ban_themselves()
    {
        var admin = Join(_admin);
        var other = Join();
        Advance(3);

        Assert.Equal(ChatResult.NotAllowed, admin.BanMember(admin.Me.Fingerprint, null, null, _now));
        Inject(_admin, BanOf(admin));                 // Admin-Unterschrift, Ziel = Admin
        Advance(1);
        Assert.Equal(ChatSessionState.Connected, admin.State);
        Assert.Contains(other.Members, m => m.Fingerprint == admin.Me.Fingerprint);
    }

    [Fact]
    public void Unban_lets_them_back_in_and_an_old_ban_cannot_return()
    {
        var admin = Join(_admin);
        var b = Join();
        var id = ChatIdentity.CreateNew();
        var c = Join(id);
        Advance(3);
        var banTime = _now.ToUnixTimeMilliseconds();
        admin.BanMember(c.Me.Fingerprint, "Test", null, _now);
        Settle(() => b.Bans.IsBanned(id.Fingerprint, _now, out _));

        Advance(30);
        Assert.Equal(ChatResult.Ok, admin.UnbanMember(id.Fingerprint, _now));
        Settle(() => !b.Bans.IsBanned(id.Fingerprint, _now, out _) && !c.IsBanned);   // auch bei der Betroffenen sofort aufgehoben
        Assert.True(HasNotice(b, ChatNotice.Unbanned));
        Assert.True(HasNotice(c, ChatNotice.YouUnbanned));

        // dieselbe alte Sperre wird noch einmal abgeschickt (z. B. mitgeschnitten und wiederholt)
        Inject(_admin, new ChatPayload { Kind = ChatKinds.Ban, Bans = [new ChatBanEntry { Fingerprint = id.Fingerprint, MemberId = id.Id, At = banTime }] });
        Advance(1);
        Assert.False(b.Bans.IsBanned(id.Fingerprint, _now, out _));

        Inject(id, new ChatPayload { Kind = ChatKinds.Text, Text = "wieder da" });
        Advance(1);
        Assert.Contains(b.Lines, l => l.Kind == ChatLineKind.Theirs && l.Text == "wieder da");
    }

    // ---- Issue #5: "Sperrungen koennen nicht fruehzeitig aufgehoben werden" ----

    [Fact]
    public void A_ban_lifted_while_the_person_was_away_reaches_them_when_they_come_back()
    {
        var admin = Join(_admin);
        var b = Join();
        var id = ChatIdentity.CreateNew();
        var savedBans = new ChatBanList();              // was auf der Platte steht: bleibt ueber Neustarts
        var c = Join(id, bans: savedBans);
        Advance(3);
        admin.BanMember(id.Fingerprint, "Test", TimeSpan.FromDays(1), _now);
        Settle(() => c.IsBanned);

        c.Leave(_now);                                   // App wird geschlossen
        Settle(() => c.State == ChatSessionState.Ended);
        Advance(30);
        Assert.Equal(ChatResult.Ok, admin.UnbanMember(id.Fingerprint, _now));   // Aufhebung, waehrend sie nicht da ist
        Settle(() => !b.Bans.IsBanned(id.Fingerprint, _now, out _));
        Assert.True(savedBans.IsBanned(id.Fingerprint, _now, out _));           // ihre gemerkte Liste weiss davon noch nichts

        // App wird wieder gestartet, gleiche gemerkte Liste: erst gesperrt, dann fragt sie still nach und wird freigegeben
        var c2 = Join(id, bans: savedBans);
        Settle(() => !c2.IsBanned);
        Assert.True(HasNotice(c2, ChatNotice.YouBannedReason));   // stand beim Start noch als gesperrt da
        Assert.True(HasNotice(c2, ChatNotice.YouUnbanned));
        Assert.False(savedBans.IsBanned(id.Fingerprint, _now, out _));

        // ... und ist wirklich wieder dabei
        Advance(3);
        Assert.Contains(b.Members, m => m.Fingerprint == id.Fingerprint);
        Assert.Equal(ChatResult.Ok, c2.SendText("da bin ich wieder", _now));
    }

    [Fact]
    public void A_banned_member_can_neither_see_nor_write_but_keeps_listening()
    {
        var admin = Join(_admin);
        var b = Join();
        var c = Join();
        Advance(3);
        admin.BanMember(c.Me.Fingerprint, null, null, _now);
        Settle(() => c.IsBanned);

        b.SendText("hallo zusammen", _now);
        Advance(1);
        Assert.Contains(b.Lines, l => l.Kind == ChatLineKind.Mine && l.Text == "hallo zusammen");   // b selbst sieht es
        Assert.DoesNotContain(c.Lines, l => l.Kind == ChatLineKind.Theirs);                                      // c sieht nichts
        Assert.Single(c.Members);                                                                                // und kennt niemanden
        Assert.Equal(ChatResult.Banned, c.RequestScores(_now));
        Assert.Equal(ChatSessionState.Connected, c.State);
    }

    [Fact]
    public void A_temporary_ban_runs_out_on_its_own_and_the_person_is_back()
    {
        var admin = Join(_admin);
        var b = Join();
        var c = Join();
        Advance(3);
        admin.BanMember(c.Me.Fingerprint, null, TimeSpan.FromHours(1), _now);
        Settle(() => c.IsBanned);

        Advance(30 * 60);
        Assert.True(c.IsBanned);
        Assert.DoesNotContain(b.Members, m => m.Fingerprint == c.Me.Fingerprint);

        Advance(31 * 60);                                 // eine Stunde ist um
        Settle(() => !c.IsBanned && b.Members.Any(m => m.Fingerprint == c.Me.Fingerprint));
        Assert.True(HasNotice(c, ChatNotice.YouUnbanned));
    }

    [Fact]
    public void Only_the_admin_answers_a_ban_check_and_not_more_than_twice_a_minute()
    {
        var admin = Join(_admin);
        var b = Join();
        var id = ChatIdentity.CreateNew();
        var stranger = ChatIdentity.CreateNew();
        var c = Join(id);
        Advance(3);
        admin.BanMember(id.Fingerprint, null, null, _now);
        Settle(() => c.IsBanned);
        c.Leave(_now);
        Settle(() => c.State == ChatSessionState.Ended);
        Advance(31);   // die erste, automatische Nachfrage beim Sperren wurde schon beantwortet - jetzt ist die Bremse (30 s) vorbei

        var before = _hub.OnlineFrames;
        Inject(id, new ChatPayload { Kind = ChatKinds.BanCheck });
        Assert.Equal(before + 1, _hub.OnlineFrames);                        // Antwort: Sperre nochmal geschickt
        Inject(id, new ChatPayload { Kind = ChatKinds.BanCheck });          // gleich nochmal
        Assert.Equal(before + 1, _hub.OnlineFrames);                        // ... nicht mehr als eine Antwort pro halber Minute

        Inject(stranger, new ChatPayload { Kind = ChatKinds.BanCheck });    // Fremde ohne Eintrag: keine Antwort
        Assert.Equal(before + 1, _hub.OnlineFrames);
        Assert.DoesNotContain(b.Members, m => m.Fingerprint == stranger.Fingerprint);
    }

    [Fact]
    public void Newcomers_get_the_ban_list_from_the_admin()
    {
        var admin = Join(_admin);
        var c = Join();
        Advance(3);
        admin.BanMember(c.Me.Fingerprint, "Regelverstoss", TimeSpan.FromDays(7), _now);
        Settle(() => c.IsBanned);

        var newcomer = Join();                    // eigene, leere Sperrliste - sie kennt die Sperre nicht
        Assert.False(newcomer.Bans.IsBanned(c.Me.Fingerprint, _now, out _));
        Advance(6);                               // Verzoegerung des Abgleichs
        Assert.True(newcomer.Bans.IsBanned(c.Me.Fingerprint, _now, out var ban));
        Assert.Equal("Regelverstoss", ban!.Reason);
        Assert.DoesNotContain(newcomer.Lines, l => l.Text is ChatNotice.Banned or ChatNotice.BannedReason);   // stiller Abgleich
    }

    [Fact]
    public void Ban_messages_have_a_strict_shape_and_always_fit_into_one_frame()
    {
        var fp = new string('A', 64);
        var good = new ChatBanEntry { Fingerprint = fp, MemberId = "1234-5678-9012", At = 5 };
        Assert.True(new ChatPayload { Kind = ChatKinds.Ban, Id = "0011223344556677", Bans = [good] }.IsWellFormed());
        Assert.True(new ChatPayload { Kind = ChatKinds.BanCheck, Id = "0011223344556677" }.IsWellFormed());
        Assert.False(new ChatPayload { Kind = ChatKinds.Ban, Id = "0011223344556677" }.IsWellFormed());
        Assert.False(new ChatPayload { Kind = ChatKinds.Ban, Id = "0011223344556677", Bans = [good, good] }.IsWellFormed());
        Assert.False(new ChatPayload { Kind = ChatKinds.Ban, Id = "0011223344556677", Bans = [good with { Fingerprint = "1234-5678-9012" }] }.IsWellFormed());
        Assert.False(new ChatPayload { Kind = ChatKinds.Ban, Id = "0011223344556677", Bans = [good with { At = 0 }] }.IsWellFormed());
        Assert.False(new ChatPayload { Kind = ChatKinds.Ban, Id = "0011223344556677", Bans = [good with { Reason = new string('x', 61) }] }.IsWellFormed());
        Assert.False(new ChatPayload { Kind = ChatKinds.BanList, Id = "0011223344556677", Bans = [.. Enumerable.Repeat(good, ChatPayload.MaxBans + 1)] }.IsWellFormed());

        // Hoechstfall: volle Liste, jeder Grund mit voller Laenge aus lauter Umlauten
        var full = Enumerable.Range(0, ChatPayload.MaxBans)
            .Select(i => good with { Fingerprint = new string("0123456789ABCDEF"[i], 64), Reason = new string('ä', ChatPayload.MaxBanReasonLength), Until = 1_900_000_000_000 })
            .ToArray();
        var frame = ChatFrame.Seal(Open, ChatIdentity.CreateNew(), new ChatPayload { Kind = ChatKinds.BanList, Id = ChatFrame.NewId(), Time = 1_800_000_000_000, Bans = full });
        Assert.InRange(frame.Length, 1, ChatFrame.MaxFrameBytes);
        Assert.True(ChatFrame.TryOpen(Open, frame, out var envelope));
        Assert.Equal(ChatPayload.MaxBans, envelope!.Payload.Bans!.Length);
    }

    [Fact]
    public void Crafted_json_with_null_fields_is_dropped_instead_of_throwing()
    {
        // Wer den Raumschluessel kennt (offener Chat: jeder) kann beliebiges JSON schicken.
        var who = ChatIdentity.CreateNew();
        foreach (var json in new[]
                 {
                     """{"k":"text","i":"0011223344556677","t":1,"x":"hi","s":[{"g":null}]}""",
                     """{"k":"ban","i":"0011223344556677","t":1,"b":[{"f":null,"i":null,"t":5}]}""",
                     """{"k":"text","i":null,"t":1,"x":"hi"}""",
                 })
        {
            var frame = SealRaw(Open, who, json);
            Assert.False(ChatFrame.TryOpen(Open, frame, out _), json);
        }
    }

    /// <summary>Wie <see cref="ChatFrame.Seal"/>, aber mit selbst geschriebenem JSON.</summary>
    private static byte[] SealRaw(ChatRoomKey room, ChatIdentity sender, string json)
    {
        var body = Encoding.UTF8.GetBytes(json);
        var signed = room.Tag.Concat(body).ToArray();
        var signature = sender.Sign(signed);
        var key = sender.PublicKey;

        var plain = new byte[1 + key.Length + 1 + signature.Length + body.Length];
        plain[0] = (byte)key.Length;
        key.CopyTo(plain, 1);
        plain[1 + key.Length] = (byte)signature.Length;
        signature.CopyTo(plain, 2 + key.Length);
        body.CopyTo(plain, 2 + key.Length + signature.Length);

        byte[] header = [(byte)'F', (byte)'H', 1];
        var frame = new byte[ChatFrame.Overhead + plain.Length];
        header.CopyTo(frame, 0);
        var nonce = frame.AsSpan(header.Length, 12);
        RandomNumberGenerator.Fill(nonce);
        using var aes = new AesGcm(room.AesKey, 16);
        aes.Encrypt(nonce, plain, frame.AsSpan(header.Length + 12, plain.Length), frame.AsSpan(frame.Length - 16), header);
        return frame;
    }
}
