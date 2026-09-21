using Floppy.Core.Chat;

namespace Floppy.Core.Tests;

public class ChatProfileTests
{
    [Theory]
    [InlineData("Tom", "Tom")]
    [InlineData("  Lea   Müller ", "Lea Müller")]
    [InlineData("Max_99", "Max_99")]
    [InlineData("O'Brien", "O'Brien")]
    [InlineData("Anna-Lena", "Anna-Lena")]
    [InlineData("Jürgen", "Jürgen")]
    [InlineData("Tom\tB\nx", "Tom B x")]
    [InlineData("東京", "東京")]
    [InlineData("12345678901234567890", "12345678901234567890")]   // genau 20 Zeichen
    public void Gute_Namen_werden_gesaeubert_uebernommen(string raw, string expected)
    {
        Assert.Equal(AliasProblem.None, ChatProfile.Check(raw));
        Assert.Equal(expected, ChatProfile.Clean(raw));
    }

    [Theory]
    [InlineData(null, AliasProblem.Empty)]
    [InlineData("", AliasProblem.Empty)]
    [InlineData("   \t\n ", AliasProblem.Empty)]
    [InlineData("123456789012345678901", AliasProblem.TooLong)]
    [InlineData("Tom😀", AliasProblem.BadCharacters)]                 // Emoji
    [InlineData("(Admin) Tom", AliasProblem.BadCharacters)]          // Klammern gehoeren dem (Admin)-Zeichen
    [InlineData("[Mod] Tom", AliasProblem.BadCharacters)]
    [InlineData("Tom<b>", AliasProblem.BadCharacters)]
    [InlineData("Tom​", AliasProblem.BadCharacters)]            // unsichtbares Zeichen
    [InlineData("Tom‮", AliasProblem.BadCharacters)]            // Umkehr der Schreibrichtung
    [InlineData("Tóm", AliasProblem.BadCharacters)]            // kombinierendes Zeichen (Zalgo)
    [InlineData("Tom", AliasProblem.BadCharacters)]            // Steuerzeichen
    [InlineData("...", AliasProblem.BadCharacters)]                  // nur Satzzeichen
    [InlineData("Tom#1417", AliasProblem.BadCharacters)]             // '#' gehoert der ID-Endung
    public void Schlechte_Namen_werden_abgelehnt(string? raw, AliasProblem expected)
    {
        Assert.Equal(expected, ChatProfile.Check(raw));
        Assert.Null(ChatProfile.Clean(raw));
    }

    [Theory]
    [InlineData("Admin")]
    [InlineData("admin")]
    [InlineData("Der Admin")]
    [InlineData("ADMIN123")]
    [InlineData("A.d.m.i.n")]
    [InlineData("Moderator")]
    [InlineData("Floppy Hub")]
    [InlineData("FloppyHub")]
    [InlineData("System")]
    public void Vorbehaltene_Namen_nur_fuer_Admins(string raw)
    {
        Assert.Equal(AliasProblem.Reserved, ChatProfile.Check(raw));
        Assert.Null(ChatProfile.Clean(raw));
        Assert.Equal(AliasProblem.None, ChatProfile.Check(raw, allowReserved: true));
        Assert.Equal(ChatProfile.Normalize(raw), ChatProfile.Clean(raw, allowReserved: true));
    }

    [Theory]
    [InlineData(null, 0)]
    [InlineData(0, 0)]
    [InlineData(-3, 0)]
    [InlineData(1, 1)]
    [InlineData(8, 8)]
    [InlineData(9, 0)]
    [InlineData(int.MaxValue, 0)]
    public void Farbe_ist_automatisch_oder_eine_der_acht(int? raw, int expected) =>
        Assert.Equal(expected, ChatProfile.CleanColor(raw));

    [Theory]
    [InlineData("7439-3925-1417", "#1417")]
    [InlineData("1234", "#1234")]
    [InlineData("12", "12")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void ID_Endung(string? id, string expected) => Assert.Equal(expected, ChatProfile.IdTag(id));

    [Theory]
    [InlineData("hallo Tom, wie geht's?", "Tom", true)]
    [InlineData("hallo tom", "Tom", true)]
    [InlineData("TOM!", "Tom", true)]
    [InlineData("@Tom kommst du?", "Tom", true)]
    [InlineData("Tom", "Tom", true)]
    [InlineData("Tom's Level ist schwer", "Tom", true)]
    [InlineData("Tomate", "Tom", false)]
    [InlineData("Atom", "Tom", false)]
    [InlineData("Tomate und Tom", "Tom", true)]                // erstes Vorkommen zaehlt nicht, das zweite schon
    [InlineData("Hi jürgen", "Jürgen", true)]
    [InlineData("Hi Max Mustermann", "Max Mustermann", true)]
    [InlineData("Hi Max", "Max Mustermann", false)]
    [InlineData("nichts", "Tom", false)]
    [InlineData("", "Tom", false)]
    [InlineData(null, "Tom", false)]
    [InlineData("hallo", null, false)]
    [InlineData("hallo", "", false)]
    public void Erwaehnungen_ganzes_Wort(string? text, string? alias, bool expected) =>
        Assert.Equal(expected, ChatProfile.Mentions(text, alias));

    [Fact]
    public void Auch_die_ganze_ID_ist_eine_Erwaehnung()
    {
        Assert.True(ChatProfile.Mentions("Wem gehoert 7439-3925-1417?", null, "7439-3925-1417"));
        Assert.False(ChatProfile.Mentions("Wem gehoert 7439-3925-1418?", null, "7439-3925-1417"));
        Assert.False(ChatProfile.Mentions("Wem gehoert #1417?", "Tom", "7439-3925-1417"));   // nur die Endung reicht nicht
    }

    [Theory]
    [InlineData(ChatNotifyMode.Off, true, false)]
    [InlineData(ChatNotifyMode.Off, false, false)]
    [InlineData(ChatNotifyMode.Mentions, true, true)]
    [InlineData(ChatNotifyMode.Mentions, false, false)]
    [InlineData(ChatNotifyMode.All, true, true)]
    [InlineData(ChatNotifyMode.All, false, true)]
    public void Wann_der_Chat_sich_meldet(ChatNotifyMode mode, bool mention, bool expected) =>
        Assert.Equal(expected, ChatProfile.ShouldNotify(mode, mention));

    [Fact]
    public void Schriftgroessen_wachsen_und_Normal_ist_eins()
    {
        Assert.Equal(1f, ChatProfile.FontScale(ChatFontSize.Normal));
        Assert.True(ChatProfile.FontScale(ChatFontSize.Small) < 1f);
        Assert.True(ChatProfile.FontScale(ChatFontSize.Large) > 1f);
        Assert.True(ChatProfile.FontScale(ChatFontSize.Huge) > ChatProfile.FontScale(ChatFontSize.Large));
    }

    [Theory]
    [InlineData("off", ChatNotifyMode.Off)]
    [InlineData("Mentions", ChatNotifyMode.Mentions)]
    [InlineData(" ALL ", ChatNotifyMode.All)]
    [InlineData("", ChatNotifyMode.Off)]
    [InlineData(null, ChatNotifyMode.Off)]
    [InlineData("laut", ChatNotifyMode.Off)]
    [InlineData("7", ChatNotifyMode.Off)]
    public void Benachrichtigung_aus_der_INI(string? text, ChatNotifyMode expected) =>
        Assert.Equal(expected, ChatProfile.ParseNotify(text));

    [Theory]
    [InlineData("small", ChatFontSize.Small)]
    [InlineData("Large", ChatFontSize.Large)]
    [InlineData("huge", ChatFontSize.Huge)]
    [InlineData("normal", ChatFontSize.Normal)]
    [InlineData("riesig", ChatFontSize.Normal)]
    [InlineData(null, ChatFontSize.Normal)]
    [InlineData("42", ChatFontSize.Normal)]
    public void Schriftgroesse_aus_der_INI(string? text, ChatFontSize expected) =>
        Assert.Equal(expected, ChatProfile.ParseFontSize(text));
}

public class ChatProfileSessionTests
{
    private DateTimeOffset _now = new(2026, 9, 21, 12, 0, 0, TimeSpan.Zero);
    private readonly InMemoryChatHub _hub = new();
    private readonly List<ChatSession> _sessions = [];
    private readonly ChatIdentity _admin = ChatIdentity.CreateNew();
    private static readonly ChatRoomKey Room = ChatRoomKey.Open;

    private ChatSession Join(string? alias = null, int color = 0, ChatIdentity? identity = null)
    {
        var s = new ChatSession(identity ?? ChatIdentity.CreateNew(), Room, new InMemoryChatNetwork(_hub), null, null, fp => fp == _admin.Fingerprint);
        s.SetProfile(alias, color, _now);
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

    private void Inject(ChatIdentity who, ChatPayload payload)
    {
        var frame = ChatFrame.Seal(Room, who, payload with { Id = ChatFrame.NewId(), Time = _now.ToUnixTimeMilliseconds() });
        _hub.Inject(Room.Topic, frame);
        Settle();
    }

    [Fact]
    public void Name_und_Farbe_gehen_beim_Beitritt_und_im_Lebenszeichen_mit()
    {
        var tom = Join("Tom", 3);
        var lea = Join("Lea", 5);
        Advance(3);   // Lebenszeichen ("Hier!") kommt mit kleiner Verzoegerung

        Assert.Equal(new ChatMemberProfile("Lea", 5), tom.ProfileOf(lea.Me.Fingerprint));
        Assert.Equal(new ChatMemberProfile("Tom", 3), lea.ProfileOf(tom.Me.Fingerprint));
        Assert.Equal(new ChatMemberProfile("Tom", 3), tom.MyProfile);
    }

    [Fact]
    public void Ohne_Angaben_bleibt_alles_wie_bisher()
    {
        var a = Join();
        var b = Join();
        Advance(3);
        Assert.Equal(ChatMemberProfile.None, a.ProfileOf(b.Me.Fingerprint));
        Assert.Equal(ChatMemberProfile.None, b.ProfileOf(a.Me.Fingerprint));
        Assert.Equal(ChatMemberProfile.None, a.ProfileOf(null));
        Assert.Equal(ChatMemberProfile.None, a.ProfileOf("gibt-es-nicht"));
    }

    [Fact]
    public void Umbenennen_waehrend_des_Chats_erfahren_die_anderen_sofort()
    {
        var tom = Join("Tom", 3);
        var lea = Join("Lea");
        Advance(3);

        tom.SetProfile("Tommy", 7, _now);
        Settle();

        Assert.Equal(new ChatMemberProfile("Tommy", 7), lea.ProfileOf(tom.Me.Fingerprint));
        Assert.Equal(new ChatMemberProfile("Tommy", 7), tom.MyProfile);

        tom.SetProfile(null, 0, _now);   // wieder ohne Namen
        Settle();
        Assert.Equal(ChatMemberProfile.None, lea.ProfileOf(tom.Me.Fingerprint));
    }

    [Fact]
    public void Auch_ein_Text_traegt_Name_und_Farbe_falls_der_Beitritt_verpasst_wurde()
    {
        var lea = Join("Lea");
        var stranger = ChatIdentity.CreateNew();

        Inject(stranger, new ChatPayload { Kind = ChatKinds.Text, Text = "Hallo!", Alias = "Xena", Color = 6 });

        Assert.Equal(new ChatMemberProfile("Xena", 6), lea.ProfileOf(stranger.Fingerprint));
        Assert.Contains(lea.Lines, l => l.Kind == ChatLineKind.Theirs && l.Text == "Hallo!" && l.Fingerprint == stranger.Fingerprint);
    }

    [Fact]
    public void Eigener_Text_geht_mit_Profil_raus()
    {
        var tom = Join("Tom", 2);
        var lea = Join();
        Advance(3);

        Assert.Equal(ChatResult.Ok, tom.SendText("Servus", _now));
        Settle();
        Assert.Equal(new ChatMemberProfile("Tom", 2), lea.ProfileOf(tom.Me.Fingerprint));
    }

    [Theory]
    [InlineData("(Admin) Tom")]       // Klammern
    [InlineData("Tom😀")]             // Emoji
    [InlineData("Tom​")]         // unsichtbares Zeichen
    [InlineData("Admin")]             // vorbehalten
    [InlineData("Floppy Hub")]        // sieht aus wie eine App-Meldung
    [InlineData("Zwischen zwanzig und mehr")]   // 25 Zeichen: die Nachricht ist gueltig, der Name zu lang
    public void Boese_Namen_von_anderen_werden_ignoriert(string alias)
    {
        var lea = Join("Lea");
        var mallory = ChatIdentity.CreateNew();

        Inject(mallory, new ChatPayload { Kind = ChatKinds.Text, Text = "Ich bin's", Alias = alias, Color = 2 });

        Assert.Null(lea.ProfileOf(mallory.Fingerprint).Alias);       // Name wird nicht uebernommen
        Assert.Equal(2, lea.ProfileOf(mallory.Fingerprint).Color);   // die Farbe ist harmlos
        Assert.Contains(lea.Lines, l => l.Kind == ChatLineKind.Theirs && l.Text == "Ich bin's");
    }

    [Fact]
    public void Steuerzeichen_im_Namen_machen_die_ganze_Nachricht_ungueltig()
    {
        var lea = Join("Lea");
        var mallory = ChatIdentity.CreateNew();

        Inject(mallory, new ChatPayload { Kind = ChatKinds.Text, Text = "boese", Alias = "Tom[31m" });
        Inject(mallory, new ChatPayload { Kind = ChatKinds.Text, Text = "zu lang", Alias = new string('x', 33) });

        Assert.DoesNotContain(lea.Lines, l => l.Kind == ChatLineKind.Theirs);
    }

    [Fact]
    public void Admins_duerfen_Admin_im_Namen_tragen_alle_anderen_nicht()
    {
        var lea = Join("Lea");
        var me = Join("Admin", 1);                                   // normaler Nutzer: Name wird verworfen
        var boss = Join("Admin Mael", 4, _admin);                    // echter Admin (Fingerabdruck stimmt)
        Advance(3);

        Assert.Null(me.MyProfile.Alias);
        Assert.Null(lea.ProfileOf(me.Me.Fingerprint).Alias);
        Assert.Equal("Admin Mael", boss.MyProfile.Alias);
        Assert.Equal(new ChatMemberProfile("Admin Mael", 4), lea.ProfileOf(boss.Me.Fingerprint));
    }

    [Fact]
    public void Farben_ausserhalb_der_acht_werden_zu_automatisch()
    {
        var lea = Join("Lea");
        var x = ChatIdentity.CreateNew();
        Inject(x, new ChatPayload { Kind = ChatKinds.Here, Alias = "X", Color = 99 });
        Assert.Equal(new ChatMemberProfile("X", 0), lea.ProfileOf(x.Fingerprint));

        Inject(x, new ChatPayload { Kind = ChatKinds.Here, Alias = "X", Color = -4 });
        Assert.Equal(0, lea.ProfileOf(x.Fingerprint).Color);
    }

    [Fact]
    public void Aeltere_Versionen_ohne_Profil_loeschen_den_Namen_wieder()
    {
        var lea = Join("Lea");
        var x = ChatIdentity.CreateNew();
        Inject(x, new ChatPayload { Kind = ChatKinds.Text, Text = "neu", Alias = "Xena", Color = 3 });
        Assert.Equal("Xena", lea.ProfileOf(x.Fingerprint).Alias);

        Inject(x, new ChatPayload { Kind = ChatKinds.Text, Text = "alt" });   // Absender ohne Profil-Felder
        Assert.Equal(ChatMemberProfile.None, lea.ProfileOf(x.Fingerprint));
    }

    [Fact]
    public void Der_Name_bleibt_erhalten_auch_wenn_jemand_den_Raum_verlassen_hat()
    {
        var tom = Join("Tom", 3);
        var lea = Join("Lea");
        Advance(3);
        Assert.Equal("Tom", lea.ProfileOf(tom.Me.Fingerprint).Alias);

        tom.Leave(_now);
        Settle(() => lea.Members.All(m => m.Fingerprint != tom.Me.Fingerprint));

        Assert.Equal("Tom", lea.ProfileOf(tom.Me.Fingerprint).Alias);   // alte Zeilen behalten ihren Namen
    }

    [Fact]
    public void Neue_Zeilen_melden_sich_ueber_das_Ereignis()
    {
        var tom = Join("Tom");
        var lea = Join("Lea");
        Advance(3);

        var seen = new List<ChatLine>();
        lea.LineAdded += seen.Add;
        tom.SendText("Hallo Lea!", _now);
        Settle(() => seen.Any(l => l.Kind == ChatLineKind.Theirs));

        Assert.Contains(seen, l => l.Kind == ChatLineKind.Theirs && l.Text == "Hallo Lea!");
        Assert.True(ChatProfile.Mentions(seen.First(l => l.Kind == ChatLineKind.Theirs).Text, lea.MyProfile.Alias));
    }

    [Fact]
    public void Der_laengste_Name_und_der_laengste_Text_passen_zusammen_in_eine_Nachricht()
    {
        var tom = Join(new string('T', ChatProfile.MaxAliasLength), 8);
        var lea = Join();
        Advance(3);

        Assert.Equal(ChatResult.Ok, tom.SendText(new string('x', ChatPayload.MaxTextLength), _now));
        Settle();
        Assert.Equal(new string('T', ChatProfile.MaxAliasLength), lea.ProfileOf(tom.Me.Fingerprint).Alias);
    }
}
