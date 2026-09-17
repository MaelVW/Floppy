using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Channels;
using Floppy.Core.Chat;

namespace Floppy.Core.Tests;

public class LanTransportTests
{
    private static LanOptions Options() => new()
    {
        LoopbackOnly = true,
        TcpPort = 0,
        DiscoveryPort = FreeUdpPort(),
        ProbeInterval = TimeSpan.FromMilliseconds(300),
    };

    private static int FreeUdpPort()
    {
        using var probe = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        return ((IPEndPoint)probe.Client.LocalEndPoint!).Port;
    }

    private static void WaitFor(Func<bool> condition, int ms = 5000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(ms);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) Assert.Fail("Zeit abgelaufen.");
            Thread.Sleep(10);
        }
    }

    [Fact]
    public async Task Two_pcs_exchange_frames_directly()
    {
        using var a = new LanTransport(ChatCryptoTests.RoomA, Options());
        using var b = new LanTransport(ChatCryptoTests.RoomA, Options());
        var atA = new ConcurrentQueue<byte[]>();
        var atB = new ConcurrentQueue<byte[]>();
        a.FrameReceived += (f, _) => atA.Enqueue(f);
        b.FrameReceived += (f, _) => atB.Enqueue(f);
        a.Start();
        b.Start();

        Assert.Single(a.Endpoints);
        Assert.StartsWith("127.0.0.1:", a.Endpoints[0]);
        Assert.True(await b.ConnectAsync(a.Endpoints, TimeSpan.FromSeconds(3)));
        WaitFor(() => a.PeerCount == 1 && b.PeerCount == 1);

        a.Send([1, 2, 3]);
        b.Send([4, 5]);
        WaitFor(() => atA.Count == 1 && atB.Count == 1);
        Assert.Equal([1, 2, 3], atB.Single());
        Assert.Equal([4, 5], atA.Single());
    }

    [Fact]
    public async Task Other_room_cannot_connect()
    {
        using var a = new LanTransport(ChatCryptoTests.RoomA, Options());
        using var stranger = new LanTransport(ChatCryptoTests.RoomB, Options());
        a.Start();
        stranger.Start();
        Assert.False(await stranger.ConnectAsync(a.Endpoints, TimeSpan.FromSeconds(1.5)));
        Thread.Sleep(200);
        Assert.Equal(0, a.PeerCount);
    }

    [Fact]
    public async Task Third_member_learns_the_others_from_peer_list()
    {
        using var a = new LanTransport(ChatCryptoTests.RoomA, Options());
        using var b = new LanTransport(ChatCryptoTests.RoomA, Options());
        using var c = new LanTransport(ChatCryptoTests.RoomA, Options());
        a.Start();
        b.Start();
        c.Start();
        Assert.True(await b.ConnectAsync(a.Endpoints, TimeSpan.FromSeconds(3)));
        WaitFor(() => a.PeerCount == 1);
        Assert.True(await c.ConnectAsync(b.Endpoints, TimeSpan.FromSeconds(3)));

        // C kennt A nur aus der Peer-Liste von B
        WaitFor(() => a.PeerCount == 2 && b.PeerCount == 2 && c.PeerCount == 2);

        var atC = new ConcurrentQueue<byte[]>();
        c.FrameReceived += (f, _) => atC.Enqueue(f);
        a.Send([9]);
        WaitFor(() => atC.Count >= 1);
    }

    [Fact]
    public async Task Probe_finds_room_on_this_pc_only_with_right_key()
    {
        var options = Options();
        using var a = new LanTransport(ChatCryptoTests.RoomA, options);
        a.Start();

        var found = await LanTransport.ProbeAsync(ChatCryptoTests.RoomA, TimeSpan.FromSeconds(1), options);
        Assert.Single(found);
        Assert.Equal(a.Endpoints[0], found[0].Endpoint);

        var other = await LanTransport.ProbeAsync(ChatCryptoTests.RoomB, TimeSpan.FromMilliseconds(500), options);
        Assert.Empty(other);
    }

    [Fact]
    public async Task Closing_the_last_peer_reports_waiting()
    {
        var a = new LanTransport(ChatCryptoTests.RoomA, Options());
        using var b = new LanTransport(ChatCryptoTests.RoomA, Options());
        var states = new ConcurrentQueue<ChatLinkState>();
        b.StateChanged += (s, _) => states.Enqueue(s);
        a.Start();
        b.Start();
        Assert.True(await b.ConnectAsync(a.Endpoints, TimeSpan.FromSeconds(3)));
        WaitFor(() => b.PeerCount == 1);
        a.Close(TimeSpan.FromMilliseconds(200));
        WaitFor(() => b.PeerCount == 0);
        WaitFor(() => states.LastOrDefault() == ChatLinkState.Connecting);
    }

    [Theory]
    [InlineData("192.168.1.20:45817", true)]
    [InlineData("10.4.0.9:50000", true)]
    [InlineData("172.20.1.1:45817", true)]
    [InlineData("172.40.1.1:45817", false)]
    [InlineData("8.8.8.8:45817", false)]
    [InlineData("192.168.1.20:80", false)]
    [InlineData("192.168.1.20", false)]
    [InlineData("[::1]:45817", false)]
    [InlineData("localhost:45817", false)]
    public void Only_private_endpoints_are_accepted(string endpoint, bool ok) =>
        Assert.Equal(ok, LanTransport.TryParseEndpoint(endpoint, out _, out _));
}

public class NtfyTransportTests
{
    /// <summary>Nachgebauter ntfy-Dienst: Abo als Zeilen-Stream, POST wird mitgeschrieben.</summary>
    private sealed class FakeNtfy : HttpMessageHandler
    {
        public Channel<string> Lines { get; } = Channel.CreateUnbounded<string>();
        public ConcurrentQueue<(HttpRequestMessage Request, string Body)> Posts { get; } = new();
        public HttpStatusCode PostStatus { get; set; } = HttpStatusCode.OK;
        public int Subscriptions;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Method == HttpMethod.Post)
            {
                Posts.Enqueue((request, await request.Content!.ReadAsStringAsync(cancellationToken)));
                return new HttpResponseMessage(PostStatus);
            }
            Interlocked.Increment(ref Subscriptions);
            Assert.EndsWith("/floppyhub-test/json", request.RequestUri!.AbsoluteUri);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new LineStream(Lines.Reader)) };
        }
    }

    private sealed class LineStream(ChannelReader<string> lines) : Stream
    {
        private byte[] _pending = [];
        private int _offset;

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_offset >= _pending.Length)
            {
                if (!await lines.WaitToReadAsync(cancellationToken)) return 0;
                if (!lines.TryRead(out var line)) return 0;
                if (line == "<close>") return 0;
                _pending = Encoding.UTF8.GetBytes(line + "\n");
                _offset = 0;
            }
            var n = Math.Min(buffer.Length, _pending.Length - _offset);
            _pending.AsMemory(_offset, n).CopyTo(buffer);
            _offset += n;
            return n;
        }

        public override int Read(byte[] buffer, int offset, int count) => ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => 0; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private static void WaitFor(Func<bool> condition, int ms = 5000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(ms);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) Assert.Fail("Zeit abgelaufen.");
            Thread.Sleep(10);
        }
    }

    [Fact]
    public void Receives_messages_from_json_stream()
    {
        var fake = new FakeNtfy();
        using var t = new NtfyTransport(new Uri("https://ntfy.test"), "floppyhub-test", fake);
        var frames = new ConcurrentQueue<byte[]>();
        var states = new ConcurrentQueue<ChatLinkState>();
        t.FrameReceived += (f, _) => frames.Enqueue(f);
        t.StateChanged += (s, _) => states.Enqueue(s);
        t.Start();

        fake.Lines.Writer.TryWrite("""{"id":"a","time":1,"event":"open","topic":"floppyhub-test"}""");
        fake.Lines.Writer.TryWrite("""{"id":"b","time":2,"event":"keepalive","topic":"floppyhub-test"}""");
        fake.Lines.Writer.TryWrite("kein json");
        fake.Lines.Writer.TryWrite($$"""{"id":"c","time":3,"event":"message","topic":"floppyhub-test","message":"{{Convert.ToBase64String([7, 7, 7])}}"}""");
        fake.Lines.Writer.TryWrite("""{"id":"d","time":4,"event":"message","topic":"floppyhub-test","message":"%%% kein base64"}""");

        WaitFor(() => frames.Count == 1);
        Assert.Equal([7, 7, 7], frames.Single());
        Assert.Contains(ChatLinkState.Connected, states);
    }

    [Fact]
    public void Publishes_without_cache_and_firebase()
    {
        var fake = new FakeNtfy();
        using var t = new NtfyTransport(new Uri("https://ntfy.test/"), "floppyhub-test", fake);
        t.Start();
        t.Send([1, 2, 3, 4]);
        WaitFor(() => fake.Posts.Count == 1);

        var (request, body) = fake.Posts.Single();
        Assert.Equal("https://ntfy.test/floppyhub-test", request.RequestUri!.AbsoluteUri);
        Assert.Equal("no", request.Headers.GetValues("Cache").Single());
        Assert.Equal("no", request.Headers.GetValues("Firebase").Single());
        Assert.Equal(Convert.ToBase64String([1, 2, 3, 4]), body);
    }

    [Fact]
    public void Rate_limit_is_reported_and_not_retried()
    {
        var fake = new FakeNtfy { PostStatus = HttpStatusCode.TooManyRequests };
        using var t = new NtfyTransport(new Uri("https://ntfy.test/"), "floppyhub-test", fake);
        var states = new ConcurrentQueue<(ChatLinkState, string?)>();
        t.StateChanged += (s, d) => states.Enqueue((s, d));
        t.Start();
        t.Send([1]);
        WaitFor(() => states.Contains((ChatLinkState.Limited, "publish")));
        Thread.Sleep(300);
        Assert.Single(fake.Posts);
    }

    [Fact]
    public void Reconnects_when_stream_ends()
    {
        var fake = new FakeNtfy();
        using var t = new NtfyTransport(new Uri("https://ntfy.test/"), "floppyhub-test", fake) { MaxReconnectDelay = TimeSpan.FromMilliseconds(200) };
        var states = new ConcurrentQueue<ChatLinkState>();
        t.StateChanged += (s, _) => states.Enqueue(s);
        t.Start();
        fake.Lines.Writer.TryWrite("""{"event":"open"}""");
        WaitFor(() => states.Contains(ChatLinkState.Connected));
        fake.Lines.Writer.TryWrite("<close>");
        WaitFor(() => fake.Subscriptions >= 2 && states.Contains(ChatLinkState.Reconnecting));
    }

    [Theory]
    [InlineData("https://ntfy.sh", "https://ntfy.sh/")]
    [InlineData("https://chat.example.org/ntfy", "https://chat.example.org/ntfy/")]
    [InlineData("http://localhost:8080", "http://localhost:8080/")]
    [InlineData("http://ntfy.sh", "https://ntfy.sh/")]
    [InlineData("ftp://x", "https://ntfy.sh/")]
    [InlineData("", "https://ntfy.sh/")]
    public void Server_setting_is_checked(string text, string expected) =>
        Assert.Equal(expected, DefaultChatNetwork.ParseServer(text).AbsoluteUri);

    /// <summary>Echter Test gegen ntfy.sh - nur mit Umgebungsvariable FLOPPY_NET_TESTS=1 (zaehlt aufs Tageslimit).</summary>
    [Fact]
    public void Real_service_roundtrip_when_enabled()
    {
        if (Environment.GetEnvironmentVariable("FLOPPY_NET_TESTS") != "1") return;

        var room = ChatRoomKey.Derive("floppy-nettest-" + Guid.NewGuid().ToString("N"));
        var lan = new LanOptions { LoopbackOnly = true, TcpPort = 0, DiscoveryPort = 0 };
        var network = new DefaultChatNetwork(NtfyTransport.DefaultServer) { LanOptions = lan };
        using var alice = new ChatSession(ChatIdentity.CreateNew(), room, network);
        using var bob = new ChatSession(ChatIdentity.CreateNew(), room, network);

        void Pump(Func<bool> until, int seconds)
        {
            var deadline = DateTime.UtcNow.AddSeconds(seconds);
            while (!until())
            {
                if (DateTime.UtcNow > deadline) Assert.Fail("ntfy.sh hat nicht rechtzeitig geantwortet.");
                alice.Pump(DateTimeOffset.UtcNow);
                bob.Pump(DateTimeOffset.UtcNow);
                Thread.Sleep(50);
            }
        }

        alice.Start(DateTimeOffset.UtcNow);
        Pump(() => alice.State == ChatSessionState.Connected, 20);
        bob.Start(DateTimeOffset.UtcNow);
        Pump(() => bob.State == ChatSessionState.Connected, 20);
        Pump(() => alice.Members.Count == 2 && bob.Members.Count == 2, 20);

        Assert.Equal(ChatResult.Ok, alice.SendText("Hallo über ntfy.sh 💾", DateTimeOffset.UtcNow));
        Pump(() => bob.Lines.Any(l => l.Kind == ChatLineKind.Theirs && l.Text == "Hallo über ntfy.sh 💾"), 20);

        bob.Leave(DateTimeOffset.UtcNow, wait: true);
        Pump(() => alice.Members.Count == 1, 20);
        alice.Leave(DateTimeOffset.UtcNow, wait: true);
    }
}
