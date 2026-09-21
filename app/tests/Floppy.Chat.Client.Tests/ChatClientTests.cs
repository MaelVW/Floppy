using System.Security.Cryptography;
using Floppy.Chat.Client;
using Floppy.Core.Chat;

namespace Floppy.Chat.Client.Tests;

[Collection("Loc")]
public sealed class ChatClientTests : IDisposable
{
    private const string Secret = "Schulhof-Diskette-42";

    private DateTimeOffset _now = new(2026, 9, 21, 12, 0, 0, TimeSpan.Zero);
    private readonly InMemoryChatHub _hub = new();
    private readonly List<ChatClient> _clients = [];
    private readonly List<string> _dirs = [];

    public ChatClientTests() => Loc.Load("de");

    public void Dispose()
    {
        foreach (var c in _clients) c.Dispose();
        foreach (var d in _dirs)
        {
            try { Directory.Delete(d, recursive: true); } catch { /* Temp-Ordner, egal */ }
        }
    }

    private string NewDir()
    {
        var dir = Directory.CreateTempSubdirectory("floppychat-test-").FullName;
        _dirs.Add(dir);
        return dir;
    }

    private ChatClient NewClient(MemoryChatSettings? settings = null, ISecretProtector? protector = null, string? dir = null)
    {
        var client = new ChatClient(dir ?? NewDir(), settings ?? new MemoryChatSettings(), protector, new InMemoryChatNetwork(_hub), () => _now);
        _clients.Add(client);
        return client;
    }

    /// <summary>Alle Clients pumpen, bis die Bedingung stimmt (Ableiten und Netz melden sich aus Hintergrund-Threads).</summary>
    private void Settle(Func<bool>? until = null, int ms = 8000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(until is null ? 40 : ms);
        do
        {
            foreach (var c in _clients) c.Pump();
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

    private void Join(params ChatClient[] clients)
    {
        foreach (var c in clients) c.Connect(Secret);
        Settle(() => clients.All(c => c.Session is { State: ChatSessionState.Connected }));
    }

    [Fact]
    public void Two_clients_chat_and_see_names()
    {
        var anna = NewClient(new MemoryChatSettings { Alias = "Anna", Color = 3 });
        var bob = NewClient();
        Join(anna, bob);
        Advance(3);   // "here"-Antworten kommen verzoegert
        Assert.Equal(2, anna.GetMembers().Count);

        Assert.Equal(ChatResult.Ok, anna.Send("Hallo Bob"));
        Settle(() => bob.Session!.Lines.Any(l => l.Kind == ChatLineKind.Theirs));

        var seen = Assert.Single(bob.ReadNewRows().Rows, r => r.Kind == ChatRowKind.Message);
        Assert.Equal("Hallo Bob", seen.Text);
        Assert.Equal($"Anna #{anna.Identity.Id[^4..]}", seen.Name);
        Assert.Equal(3, seen.ColorIndex);
        Assert.False(seen.IsMine);

        var own = Assert.Single(anna.ReadNewRows().Rows, r => r.Kind == ChatRowKind.Message);
        Assert.Equal("Du", own.Name);
        Assert.True(own.IsMine);
        Assert.Equal(3, own.ColorIndex);
    }

    [Fact]
    public void Members_without_alias_show_their_id_and_an_automatic_color()
    {
        var a = NewClient();
        var b = NewClient();
        Join(a, b);
        Advance(3);

        var other = Assert.Single(a.GetMembers(), m => !m.IsSelf);
        Assert.Equal($"ID {b.Identity.Id}", other.Name);
        Assert.Equal(ChatPalette.AutoIndex(b.Identity.Fingerprint), other.ColorIndex);
        Assert.InRange(other.ColorIndex, 1, ChatProfile.ColorCount);

        var me = Assert.Single(a.GetMembers(), m => m.IsSelf);
        Assert.Equal("Du", me.Name);
        Assert.Equal(0, me.ColorIndex);   // eigene Farbe ohne Wahl = Akzentfarbe der Oberflaeche
    }

    [Fact]
    public void Alias_changes_reach_the_others_right_away()
    {
        var settings = new MemoryChatSettings();
        var a = NewClient(settings);
        var b = NewClient();
        Join(a, b);
        Advance(3);

        settings.Alias = "Tom";
        settings.Color = 5;
        a.ApplyProfile();
        Advance(1);

        var seenByB = Assert.Single(b.GetMembers(), m => !m.IsSelf);
        Assert.Equal($"Tom #{a.Identity.Id[^4..]}", seenByB.Name);
        Assert.Equal(5, seenByB.ColorIndex);
    }

    [Fact]
    public void Reserved_alias_is_dropped_for_normal_users()
    {
        var a = NewClient(new MemoryChatSettings { Alias = "Admin" });
        Assert.Null(a.MyAlias);
        a.AdminCheck = _ => true;   // ein Admin darf so heissen
        Assert.Equal("Admin", a.MyAlias);
    }

    [Fact]
    public void Mentions_are_flagged_and_notify_only_when_asked()
    {
        var a = NewClient();
        var b = NewClient(new MemoryChatSettings { Alias = "Bob", Notify = ChatNotifyMode.Mentions });
        var notified = new List<(string Text, bool Mention)>();
        b.MessageReceived += (line, mention) => notified.Add((line.Text, mention));
        Join(a, b);
        Advance(3);

        a.Send("hi bob, alles gut?");
        a.Send("und noch was");
        Settle(() => b.Session!.Lines.Count(l => l.Kind == ChatLineKind.Theirs) == 2);

        var rows = b.ReadNewRows().Rows.Where(r => r.Kind == ChatRowKind.Message).ToList();
        Assert.True(rows[0].IsMention);
        Assert.False(rows[1].IsMention);
        var only = Assert.Single(notified);
        Assert.Equal("hi bob, alles gut?", only.Text);
        Assert.True(only.Mention);
    }

    [Fact]
    public void Feed_appends_and_resets()
    {
        var a = NewClient();
        var b = NewClient();
        Join(a, b);
        Advance(3);

        var first = a.ReadNewRows();
        Assert.True(first.Reset);
        Assert.NotEmpty(first.Rows);   // die Hinweise vom Beitritt

        var idle = a.ReadNewRows();
        Assert.False(idle.Reset);
        Assert.Empty(idle.Rows);

        b.Send("neu");
        Settle(() => a.Session!.Lines.Any(l => l.Text == "neu"));
        var next = a.ReadNewRows();
        Assert.False(next.Reset);
        Assert.Contains(next.Rows, r => r.Text == "neu");

        a.Reset();
        var afterReset = a.ReadNewRows();
        Assert.True(afterReset.Reset);
        Assert.Empty(afterReset.Rows);
        Assert.False(a.IsActive);
        Assert.Equal(ChatSessionState.Ended, a.Session?.State ?? ChatSessionState.Ended);
    }

    [Fact]
    public void Notices_are_real_sentences_not_keys()
    {
        var a = NewClient();
        var b = NewClient();
        Join(a, b);
        Advance(3);

        var rows = a.ReadNewRows().Rows.Where(r => r.Kind != ChatRowKind.Message).ToList();
        Assert.NotEmpty(rows);
        Assert.All(rows, r => Assert.DoesNotContain("CHAT_N_", r.Text));
        Assert.Contains(rows, r => r.Text.Contains("beigetreten") && r.Text.Contains(b.Identity.Id));
    }

    [Fact]
    public void Status_follows_the_connection()
    {
        var a = NewClient();
        Assert.Equal(ChatStatusLevel.Off, a.GetStatus().Level);

        a.Connect(Secret);
        Assert.True(a.IsDeriving);
        Assert.Equal(ChatStatusLevel.Warn, a.GetStatus().Level);
        Settle(() => a.Session is { State: ChatSessionState.Connected });

        var online = a.GetStatus();
        Assert.Equal(ChatStatusLevel.Ok, online.Level);
        Assert.Contains("ntfy.sh", online.Text);
        Assert.True(a.CanWrite);

        a.Leave();
        Assert.Equal(ChatStatusLevel.Off, a.GetStatus().Level);
        Assert.False(a.CanWrite);
        Assert.Equal(ChatResult.NotConnected, a.Send("noch da?"));
    }

    [Fact]
    public void Open_room_is_marked_and_carries_the_hint()
    {
        var a = NewClient();
        a.ConnectOpen();
        Settle(() => a.Session is { State: ChatSessionState.Connected });
        Assert.True(a.IsOpenRoom);
        Assert.Equal(Loc.T("CHAT_OPEN_ROOM"), a.RoomLabel);
        var (text, warn) = a.GetHint();
        Assert.Equal(Loc.T("CHAT_OPEN_HINT"), text);
        Assert.True(warn);

        var b = NewClient();
        Join(b);
        Assert.False(b.IsOpenRoom);
        Assert.Equal(Loc.T("CHAT_ROOM_CHECK", b.Session!.Room.Check), b.RoomLabel);
        Assert.Equal(("", false), b.GetHint());
    }

    [Fact]
    public void Invalid_secret_is_ignored()
    {
        var a = NewClient();
        a.Connect("abc");
        Assert.False(a.IsDeriving);
        Assert.Null(a.Session);
        Assert.False(a.IsActive);
        Assert.Equal(Loc.T("CHAT_SECRET_TOOSHORT"), ChatClient.SecretProblemText(ChatRoomKey.Problem("abc")));
        Assert.Null(ChatClient.SecretProblemText(SecretProblem.None));
    }

    [Fact]
    public void Local_switch_proposal_becomes_a_banner_for_the_others()
    {
        var a = NewClient();
        var b = NewClient();
        Join(a, b);
        Advance(3);
        Assert.True(a.CanProposeLocal);

        Assert.Equal(ChatResult.Ok, a.ProposeLocal());
        Settle(() => b.GetBanner() is { YesLabel: not null });

        var asking = b.GetBanner()!;
        Assert.Equal(Loc.T("CHAT_BTN_APPROVE"), asking.YesLabel);
        Assert.Equal(Loc.T("CHAT_BTN_DECLINE"), asking.NoLabel);
        Assert.Contains(a.Identity.Id, asking.Text);
        Assert.False(asking.IsCountdown);

        var waiting = a.GetBanner()!;
        Assert.Null(waiting.YesLabel);
        Assert.False(a.CanProposeLocal);   // es laeuft schon einer

        b.AnswerProposal(false);
        Settle(() => a.Session!.Proposal is null || a.GetBanner()!.Text != waiting.Text);
    }

    [Fact]
    public void Result_texts_are_sentences_not_keys()
    {
        foreach (var result in Enum.GetValues<ChatResult>())
        {
            var text = ChatClient.ResultText(result);
            if (result is ChatResult.Ok or ChatResult.Empty) Assert.Null(text);
            else
            {
                Assert.False(string.IsNullOrWhiteSpace(text));
                Assert.DoesNotContain("CHAT_RESULT_", text);
            }
        }
        Assert.Contains(ChatSession.OnlineMessagesPerMinute.ToString(), ChatClient.ResultText(ChatResult.TooFast));
        foreach (var problem in Enum.GetValues<SecretProblem>().Where(p => p != SecretProblem.None))
            Assert.DoesNotContain("CHAT_SECRET_", ChatClient.SecretProblemText(problem));
    }

    // ---- Identitaet mit eigenem Schutz (Handy: Keystore/Keychain) ----

    [Fact]
    public void Identity_survives_restart_with_a_custom_protector_and_is_not_plaintext()
    {
        var dir = NewDir();
        var first = NewClient(protector: new XorProtector(), dir: dir);
        first.EnsureIdentity();
        var id = first.Identity.Id;
        Assert.Null(first.IdentityWarning);
        first.Dispose();

        var second = NewClient(protector: new XorProtector(), dir: dir);
        Assert.Equal(id, second.Identity.Id);

        var bytes = File.ReadAllBytes(Path.Combine(dir, ChatIdentity.FileName));
        Assert.Throws<CryptographicException>(() => ECDsa.Create().ImportPkcs8PrivateKey(bytes, out _));
    }

    [Fact]
    public void Identity_from_another_device_is_replaced_and_backed_up()
    {
        var dir = NewDir();
        var first = NewClient(protector: new XorProtector(), dir: dir);
        var oldId = first.Identity.Id;
        first.Dispose();

        var second = NewClient(protector: new StrangerProtector(), dir: dir);   // kann die Datei nicht lesen
        Assert.NotEqual(oldId, second.Identity.Id);
        Assert.True(File.Exists(Path.Combine(dir, ChatIdentity.FileName + ".kaputt")));
    }

    [Fact]
    public void Failing_protector_falls_back_to_a_temporary_identity_with_a_warning()
    {
        var client = NewClient(protector: new BrokenProtector());
        Assert.NotNull(client.Identity);
        Assert.Equal(14, client.Identity.Id.Length);
        Assert.False(string.IsNullOrEmpty(client.IdentityWarning));
    }

    private sealed class XorProtector : ISecretProtector
    {
        public byte[] Protect(ReadOnlySpan<byte> plain)
        {
            var result = new byte[plain.Length + 1];
            result[0] = 0x42;
            for (var i = 0; i < plain.Length; i++) result[i + 1] = (byte)(plain[i] ^ 0x5A);
            return result;
        }

        public byte[]? Unprotect(ReadOnlySpan<byte> data)
        {
            if (data.Length < 2 || data[0] != 0x42) return null;
            var result = new byte[data.Length - 1];
            for (var i = 0; i < result.Length; i++) result[i] = (byte)(data[i + 1] ^ 0x5A);
            return result;
        }
    }

    private sealed class StrangerProtector : ISecretProtector
    {
        public byte[] Protect(ReadOnlySpan<byte> plain) => plain.ToArray();

        public byte[]? Unprotect(ReadOnlySpan<byte> data) => null;
    }

    private sealed class BrokenProtector : ISecretProtector
    {
        public byte[] Protect(ReadOnlySpan<byte> plain) => throw new InvalidOperationException("Schluesselbund nicht verfuegbar");

        public byte[]? Unprotect(ReadOnlySpan<byte> data) => throw new InvalidOperationException("Schluesselbund nicht verfuegbar");
    }
}
