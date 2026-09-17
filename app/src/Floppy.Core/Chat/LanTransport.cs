using System.Buffers.Binary;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace Floppy.Core.Chat;

public sealed record LanOptions
{
    public int TcpPort { get; init; } = LanTransport.DefaultTcpPort;
    public int DiscoveryPort { get; init; } = LanTransport.DefaultDiscoveryPort;

    /// <summary>Nur 127.0.0.1 (Tests, zwei Instanzen auf einem PC).</summary>
    public bool LoopbackOnly { get; init; }

    /// <summary>So oft wird im Netz nach weiteren Teilnehmern desselben Raums gesucht.</summary>
    public TimeSpan ProbeInterval { get; init; } = TimeSpan.FromSeconds(20);
}

/// <summary>Antwort auf eine Suche im lokalen Netz.</summary>
public sealed record LanProbeReply(string Endpoint, byte[] Instance);

/// <summary>
/// Chat direkt von PC zu PC im selben Netzwerk, ohne Dienst.
///
/// Jeder Teilnehmer lauscht auf TCP (bevorzugt Port 45817) und verbindet sich mit den anderen
/// (Adressen aus dem Wechsel-Vorschlag, aus der Suche per UDP-Broadcast auf Port 45816 und
/// aus den Peer-Listen der anderen). Nachrichten werden an alle weitergereicht, die sie noch
/// nicht haben - faellt einer aus, laeuft der Rest weiter.
///
/// Suche und Handschlag sind mit einem eigenen Raum-Schluessel abgesichert (HMAC): Fremde
/// sehen nicht, welcher Raum gesucht wird, und koennen keine gefaelschten Peer-Listen
/// schicken. Die Nachrichten selbst sind wie online verschluesselt und unterschrieben.
/// Verbindungen gibt es nur zu privaten Adressen (10.x, 172.16-31.x, 192.168.x, ...).
/// </summary>
public sealed class LanTransport : ILocalChatTransport
{
    public const int DefaultTcpPort = 45817;
    public const int DefaultDiscoveryPort = 45816;
    private const int MaxMessageBytes = 64 * 1024;
    private const int MaxPeers = 16;
    private const int MacBytes = 16;
    private const byte TypeHello = 0;
    private const byte TypeFrame = 1;
    private const byte TypePeers = 2;
    private static readonly byte[] ProbeMagic = "FHP1"u8.ToArray();
    private static readonly byte[] ReplyMagic = "FHR1"u8.ToArray();
    private static readonly byte[] HelloMagic = "FHL1"u8.ToArray();

    private readonly byte[] _lanKey;
    private readonly LanOptions _options;
    private readonly byte[] _instance = RandomNumberGenerator.GetBytes(8);
    private readonly object _lock = new();
    private readonly List<Peer> _peers = [];
    private readonly HashSet<string> _connecting = [];
    private readonly CancellationTokenSource _stop = new();
    private TcpListener? _listener;
    private UdpClient? _discovery;
    private IReadOnlyList<string> _endpoints = [];
    private int _port;
    private int _lastCount = -1;
    private bool _disposed;

    public LanTransport(ChatRoomKey room, LanOptions? options = null) : this(room.LanKey, options) { }

    internal LanTransport(byte[] lanKey, LanOptions? options)
    {
        _lanKey = lanKey;
        _options = options ?? new LanOptions();
    }

    private sealed class Peer(TcpClient client, bool outbound)
    {
        public TcpClient Client { get; } = client;
        public NetworkStream Stream { get; } = client.GetStream();
        public bool Outbound { get; } = outbound;
        public string RemoteIp { get; } = ((IPEndPoint)client.Client.RemoteEndPoint!).Address.MapToIPv4().ToString();
        public SemaphoreSlim WriteLock { get; } = new(1, 1);
        public byte[]? RemoteInstance { get; set; }
        public string? ListenEndpoint { get; set; }
        public bool Verified { get; set; }
        public bool Closed { get; set; }
        public Action? OnVerified { get; set; }
        public int Pending;
    }

    public ChatMode Mode => ChatMode.Local;
    public IReadOnlyList<string> Endpoints => _endpoints;

    public int PeerCount
    {
        get { lock (_lock) return _peers.Count(p => p.Verified); }
    }

    public event Action<byte[], object?>? FrameReceived;
    public event Action<ChatLinkState, string?>? StateChanged;

    public void Start()
    {
        if (_listener is not null) return;
        var bind = _options.LoopbackOnly ? IPAddress.Loopback : IPAddress.Any;
        try
        {
            _listener = new TcpListener(bind, _options.TcpPort);
            _listener.Start();
        }
        catch (SocketException)
        {
            _listener = new TcpListener(bind, 0);   // Port belegt (z. B. zweite Instanz): irgendeiner
            _listener.Start();
        }
        _port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        List<IPAddress> addresses = _options.LoopbackOnly ? [IPAddress.Loopback] : [.. LocalAddresses().Select(a => a.Address)];
        _endpoints = addresses.Select(a => $"{a}:{_port}").ToList();

        try
        {
            var udp = new UdpClient { ExclusiveAddressUse = false };
            udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            udp.Client.Bind(new IPEndPoint(bind, _options.DiscoveryPort));
            _discovery = udp;
            _ = Task.Run(() => AnswerProbes(udp, _stop.Token));
        }
        catch (SocketException)
        {
            _discovery = null;   // ohne Suche: erreichbar bleibt man ueber die Adressen im Vorschlag
        }

        _ = Task.Run(() => AcceptLoop(_listener, _stop.Token));
        _ = Task.Run(() => ProbeLoop(_stop.Token));
        UpdateState();
    }

    public void Send(byte[] frame)
    {
        foreach (var peer in VerifiedPeers()) _ = Write(peer, TypeFrame, frame);
    }

    public void Relay(byte[] frame, object? origin)
    {
        foreach (var peer in VerifiedPeers())
            if (!ReferenceEquals(peer, origin)) _ = Write(peer, TypeFrame, frame);
    }

    public void Reject(object? origin)
    {
        if (origin is Peer peer) ClosePeer(peer);
    }

    public async Task<bool> ConnectAsync(IReadOnlyList<string> endpoints, TimeSpan timeout)
    {
        var done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_lock)
        {
            if (_peers.Any(p => p.Verified && endpoints.Contains(p.ListenEndpoint))) return true;
        }
        foreach (var endpoint in endpoints.Take(ChatPayload.MaxEndpoints))
            _ = ConnectOne(endpoint, timeout, () => done.TrySetResult(true));

        var finished = await Task.WhenAny(done.Task, Task.Delay(timeout)).ConfigureAwait(false);
        return finished == done.Task;
    }

    public void Close(TimeSpan flush)
    {
        // letzte Nachrichten (z. B. "tschuess") noch rausgehen lassen
        var deadline = DateTime.UtcNow + flush;
        while (DateTime.UtcNow < deadline && VerifiedPeers().Any(p => Volatile.Read(ref p.Pending) > 0))
            Thread.Sleep(10);
        Dispose();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _stop.Cancel();
        try { _listener?.Stop(); } catch { }
        _discovery?.Dispose();
        List<Peer> all;
        lock (_lock) all = [.. _peers];
        foreach (var peer in all) ClosePeer(peer);
        StateChanged?.Invoke(ChatLinkState.Closed, null);
    }

    // ------------------------------------------------------------------
    // Suche im lokalen Netz
    // ------------------------------------------------------------------

    /// <summary>Per UDP-Broadcast nach Teilnehmern eines Raums fragen.</summary>
    public static Task<IReadOnlyList<LanProbeReply>> ProbeAsync(ChatRoomKey room, TimeSpan timeout, LanOptions? options = null) =>
        ProbeAsync(room.LanKey, options ?? new LanOptions(), timeout, null);

    private static async Task<IReadOnlyList<LanProbeReply>> ProbeAsync(byte[] lanKey, LanOptions options, TimeSpan timeout, byte[]? ownInstance)
    {
        var replies = new List<LanProbeReply>();
        try
        {
            using var udp = new UdpClient(new IPEndPoint(options.LoopbackOnly ? IPAddress.Loopback : IPAddress.Any, 0));
            udp.EnableBroadcast = !options.LoopbackOnly;

            var nonce = RandomNumberGenerator.GetBytes(8);
            var probe = Concat(ProbeMagic, nonce, Mac(lanKey, "probe", nonce));
            var targets = new List<IPAddress> { IPAddress.Loopback };
            if (!options.LoopbackOnly)
            {
                targets.Add(IPAddress.Broadcast);
                targets.AddRange(LocalAddresses().Select(a => a.Broadcast).OfType<IPAddress>());
            }
            foreach (var target in targets.Distinct())
            {
                try { await udp.SendAsync(probe, new IPEndPoint(target, options.DiscoveryPort)).ConfigureAwait(false); }
                catch (SocketException) { }
            }

            using var wait = new CancellationTokenSource(timeout);
            while (!wait.IsCancellationRequested)
            {
                UdpReceiveResult result;
                try { result = await udp.ReceiveAsync(wait.Token).ConfigureAwait(false); }
                catch (OperationCanceledException) { break; }
                catch (SocketException) { continue; }   // z. B. ICMP "Port nicht erreichbar"

                var d = result.Buffer;
                if (d.Length != 4 + 8 + 8 + 2 + MacBytes || !d.AsSpan(0, 4).SequenceEqual(ReplyMagic)) continue;
                if (!d.AsSpan(4, 8).SequenceEqual(nonce)) continue;
                if (!CryptographicOperations.FixedTimeEquals(d.AsSpan(22, MacBytes), Mac(lanKey, "reply", d.AsSpan(4, 18)))) continue;
                var instance = d.AsSpan(12, 8).ToArray();
                if (ownInstance is not null && instance.AsSpan().SequenceEqual(ownInstance)) continue;
                if (!IsAllowedAddress(result.RemoteEndPoint.Address)) continue;
                if (replies.Any(r => r.Instance.AsSpan().SequenceEqual(instance))) continue;
                var port = BinaryPrimitives.ReadUInt16BigEndian(d.AsSpan(20, 2));
                replies.Add(new LanProbeReply($"{result.RemoteEndPoint.Address.MapToIPv4()}:{port}", instance));
            }
        }
        catch (SocketException)
        {
            // kein Netz / kein UDP: dann eben nichts gefunden
        }
        return replies;
    }

    private async Task AnswerProbes(UdpClient udp, CancellationToken stop)
    {
        while (!stop.IsCancellationRequested)
        {
            UdpReceiveResult result;
            try { result = await udp.ReceiveAsync(stop).ConfigureAwait(false); }
            catch (OperationCanceledException) { return; }
            catch (ObjectDisposedException) { return; }
            catch (SocketException) { continue; }

            var d = result.Buffer;
            if (d.Length != 4 + 8 + MacBytes || !d.AsSpan(0, 4).SequenceEqual(ProbeMagic)) continue;
            var nonce = d.AsSpan(4, 8);
            if (!CryptographicOperations.FixedTimeEquals(d.AsSpan(12, MacBytes), Mac(_lanKey, "probe", nonce))) continue;
            if (!IsAllowedAddress(result.RemoteEndPoint.Address)) continue;

            var body = new byte[18];
            nonce.CopyTo(body);
            _instance.CopyTo(body, 8);
            BinaryPrimitives.WriteUInt16BigEndian(body.AsSpan(16), (ushort)_port);
            var reply = Concat(ReplyMagic, body, Mac(_lanKey, "reply", body));
            try { await udp.SendAsync(reply, result.RemoteEndPoint).ConfigureAwait(false); }
            catch (Exception ex) when (ex is SocketException or ObjectDisposedException) { }
        }
    }

    private async Task ProbeLoop(CancellationToken stop)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(1), stop).ConfigureAwait(false);
            while (!stop.IsCancellationRequested)
            {
                var replies = await ProbeAsync(_lanKey, _options, TimeSpan.FromSeconds(1.5), _instance).ConfigureAwait(false);
                foreach (var reply in replies)
                {
                    bool known;
                    lock (_lock) known = _peers.Any(p => p.RemoteInstance is { } i && i.AsSpan().SequenceEqual(reply.Instance));
                    if (!known) _ = ConnectOne(reply.Endpoint, TimeSpan.FromSeconds(4), null);
                }
                await Task.Delay(_options.ProbeInterval, stop).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // beendet
        }
    }

    // ------------------------------------------------------------------
    // Verbindungen
    // ------------------------------------------------------------------

    private async Task AcceptLoop(TcpListener listener, CancellationToken stop)
    {
        while (!stop.IsCancellationRequested)
        {
            TcpClient client;
            try { client = await listener.AcceptTcpClientAsync(stop).ConfigureAwait(false); }
            catch (OperationCanceledException) { return; }
            catch (ObjectDisposedException) { return; }
            catch (SocketException) { continue; }

            var remote = ((IPEndPoint)client.Client.RemoteEndPoint!).Address;
            bool full;
            lock (_lock) full = _peers.Count >= MaxPeers;
            if (full || !IsAllowedAddress(remote))
            {
                client.Dispose();
                continue;
            }
            StartPeer(client, outbound: false, null);
        }
    }

    private async Task ConnectOne(string endpoint, TimeSpan timeout, Action? onVerified)
    {
        if (!TryParseEndpoint(endpoint, out var address, out var port) || _endpoints.Contains(endpoint)) return;
        lock (_lock)
        {
            if (_disposed || _peers.Count >= MaxPeers || _peers.Any(p => p.Verified && p.ListenEndpoint == endpoint)) return;
            if (!_connecting.Add(endpoint)) return;
        }

        var client = new TcpClient(AddressFamily.InterNetwork);
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
            cts.CancelAfter(timeout);
            await client.ConnectAsync(address, port, cts.Token).ConfigureAwait(false);
            StartPeer(client, outbound: true, onVerified);
        }
        catch
        {
            client.Dispose();
        }
        finally
        {
            lock (_lock) _connecting.Remove(endpoint);
        }
    }

    private void StartPeer(TcpClient client, bool outbound, Action? onVerified)
    {
        client.NoDelay = true;
        var peer = new Peer(client, outbound) { OnVerified = onVerified };
        lock (_lock) _peers.Add(peer);
        _ = Task.Run(() => RunPeer(peer, _stop.Token));
    }

    private async Task RunPeer(Peer peer, CancellationToken stop)
    {
        try
        {
            var hello = new byte[4 + 8 + 2];
            HelloMagic.CopyTo(hello, 0);
            _instance.CopyTo(hello, 4);
            BinaryPrimitives.WriteUInt16BigEndian(hello.AsSpan(12), (ushort)_port);
            await Write(peer, TypeHello, Concat(hello, Mac(_lanKey, "hello", hello))).ConfigureAwait(false);

            (byte Type, byte[] Body) first;
            using (var helloTimeout = CancellationTokenSource.CreateLinkedTokenSource(stop))
            {
                helloTimeout.CancelAfter(TimeSpan.FromSeconds(5));
                first = await ReadMessage(peer, helloTimeout.Token).ConfigureAwait(false);
            }
            if (first.Type != TypeHello || first.Body.Length != 14 + MacBytes || !first.Body.AsSpan(0, 4).SequenceEqual(HelloMagic) ||
                !CryptographicOperations.FixedTimeEquals(first.Body.AsSpan(14, MacBytes), Mac(_lanKey, "hello", first.Body.AsSpan(0, 14))))
                return;   // anderer Raum oder kein Floppy Hub

            var remoteInstance = first.Body.AsSpan(4, 8).ToArray();
            if (remoteInstance.AsSpan().SequenceEqual(_instance)) return;   // mit sich selbst verbunden
            var listenPort = BinaryPrimitives.ReadUInt16BigEndian(first.Body.AsSpan(12, 2));

            lock (_lock)
            {
                if (peer.Closed) return;
                peer.RemoteInstance = remoteInstance;
                peer.ListenEndpoint = $"{peer.RemoteIp}:{listenPort}";
                var duplicate = _peers.FirstOrDefault(p => p != peer && p.Verified && p.RemoteInstance!.AsSpan().SequenceEqual(remoteInstance));
                if (duplicate is not null)
                {
                    // Beide Seiten entscheiden gleich: es bleibt die Verbindung, deren Aufbauer die kleinere Kennung hat.
                    var keepNew = Compare(Initiator(peer), Initiator(duplicate)) < 0;
                    if (!keepNew) return;
                    duplicate.Closed = true;
                    Task.Run(() => ClosePeer(duplicate));
                }
                peer.Verified = true;
            }

            peer.OnVerified?.Invoke();
            UpdateState();
            SharePeers();

            while (!stop.IsCancellationRequested)
            {
                var (type, body) = await ReadMessage(peer, stop).ConfigureAwait(false);
                switch (type)
                {
                    case TypeFrame when body.Length <= ChatFrame.MaxFrameBytes:
                        FrameReceived?.Invoke(body, peer);
                        break;
                    case TypePeers:
                        foreach (var endpoint in ReadPeers(body)) _ = ConnectOne(endpoint, TimeSpan.FromSeconds(4), null);
                        break;
                }
            }
        }
        catch
        {
            // Verbindung weg
        }
        finally
        {
            ClosePeer(peer);
        }
    }

    private byte[] Initiator(Peer p) => p.Outbound ? _instance : p.RemoteInstance!;

    private static int Compare(byte[] a, byte[] b) => a.AsSpan().SequenceCompareTo(b);

    private void SharePeers()
    {
        List<string> endpoints;
        lock (_lock) endpoints = _peers.Where(p => p.Verified && p.ListenEndpoint is not null).Select(p => p.ListenEndpoint!).Take(MaxPeers).ToList();
        var sb = new List<byte> { (byte)endpoints.Count };
        foreach (var e in endpoints)
        {
            var bytes = Encoding.ASCII.GetBytes(e);
            sb.Add((byte)bytes.Length);
            sb.AddRange(bytes);
        }
        var content = sb.ToArray();
        var message = Concat(content, Mac(_lanKey, "peers", content));
        foreach (var peer in VerifiedPeers()) _ = Write(peer, TypePeers, message);
    }

    private IEnumerable<string> ReadPeers(byte[] body)
    {
        if (body.Length < 1 + MacBytes) return [];
        var content = body.AsSpan(0, body.Length - MacBytes);
        if (!CryptographicOperations.FixedTimeEquals(body.AsSpan(body.Length - MacBytes), Mac(_lanKey, "peers", content))) return [];

        var result = new List<string>();
        var count = content[0];
        var pos = 1;
        for (var i = 0; i < count && i < MaxPeers && pos < content.Length; i++)
        {
            var length = content[pos++];
            if (length > 32 || pos + length > content.Length) break;
            result.Add(Encoding.ASCII.GetString(content.Slice(pos, length)));
            pos += length;
        }
        return result;
    }

    private async Task Write(Peer peer, byte type, byte[] body)
    {
        var buffer = new byte[5 + body.Length];
        BinaryPrimitives.WriteInt32BigEndian(buffer, body.Length + 1);
        buffer[4] = type;
        body.CopyTo(buffer, 5);
        Interlocked.Increment(ref peer.Pending);
        try
        {
            await peer.WriteLock.WaitAsync(_stop.Token).ConfigureAwait(false);
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
                timeout.CancelAfter(TimeSpan.FromSeconds(10));
                await peer.Stream.WriteAsync(buffer, timeout.Token).ConfigureAwait(false);
            }
            finally
            {
                peer.WriteLock.Release();
            }
        }
        catch
        {
            ClosePeer(peer);
        }
        finally
        {
            Interlocked.Decrement(ref peer.Pending);
        }
    }

    private static async Task<(byte Type, byte[] Body)> ReadMessage(Peer peer, CancellationToken stop)
    {
        var header = new byte[4];
        await peer.Stream.ReadExactlyAsync(header, stop).ConfigureAwait(false);
        var length = BinaryPrimitives.ReadInt32BigEndian(header);
        if (length is < 1 or > MaxMessageBytes + 1) throw new InvalidDataException("Paketlaenge ungueltig.");
        var data = new byte[length];
        await peer.Stream.ReadExactlyAsync(data, stop).ConfigureAwait(false);
        return (data[0], data[1..]);
    }

    private void ClosePeer(Peer peer)
    {
        bool removed;
        lock (_lock)
        {
            peer.Closed = true;
            removed = _peers.Remove(peer);
        }
        try { peer.Client.Dispose(); } catch { }
        if (removed) UpdateState();
    }

    private List<Peer> VerifiedPeers()
    {
        lock (_lock) return _peers.Where(p => p.Verified && !p.Closed).ToList();
    }

    private void UpdateState()
    {
        if (_disposed) return;
        var count = PeerCount;
        if (Interlocked.Exchange(ref _lastCount, count) == count) return;
        StateChanged?.Invoke(count > 0 ? ChatLinkState.Connected : ChatLinkState.Connecting, count > 0 ? null : "waiting");
    }

    // ------------------------------------------------------------------
    // Adressen
    // ------------------------------------------------------------------

    /// <summary>Eigene IPv4-Adressen im lokalen Netz (Adapter mit Gateway zuerst) + Broadcast-Adresse.</summary>
    public static IReadOnlyList<(IPAddress Address, IPAddress? Broadcast)> LocalAddresses()
    {
        try
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up &&
                            n.NetworkInterfaceType is not (NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel))
                .Select(n => n.GetIPProperties())
                .OrderByDescending(p => p.GatewayAddresses.Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork && !g.Address.Equals(IPAddress.Any)))
                .SelectMany(p => p.UnicastAddresses)
                .Where(u => u.Address.AddressFamily == AddressFamily.InterNetwork && IsAllowedAddress(u.Address) && !IPAddress.IsLoopback(u.Address))
                .Select(u => (u.Address, Broadcast: BroadcastOf(u.Address, u.IPv4Mask)))
                .DistinctBy(a => a.Address)
                .Take(6)
                .ToList();
        }
        catch (NetworkInformationException)
        {
            return [];
        }
    }

    private static IPAddress? BroadcastOf(IPAddress address, IPAddress? mask)
    {
        if (mask is null || mask.Equals(IPAddress.Any)) return null;
        var a = address.GetAddressBytes();
        var m = mask.GetAddressBytes();
        var b = new byte[4];
        for (var i = 0; i < 4; i++) b[i] = (byte)(a[i] | ~m[i]);
        return new IPAddress(b);
    }

    /// <summary>Nur private Netze (und der eigene PC) - nie Adressen im Internet.</summary>
    public static bool IsAllowedAddress(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        if (address.AddressFamily != AddressFamily.InterNetwork) return false;
        var b = address.GetAddressBytes();
        return b[0] == 10 ||
               b[0] == 127 ||
               (b[0] == 172 && b[1] is >= 16 and <= 31) ||
               (b[0] == 192 && b[1] == 168) ||
               (b[0] == 169 && b[1] == 254) ||
               (b[0] == 100 && b[1] is >= 64 and <= 127);
    }

    public static bool TryParseEndpoint(string? text, out IPAddress address, out int port)
    {
        address = IPAddress.None;
        port = 0;
        if (string.IsNullOrWhiteSpace(text) || text.Length > 32) return false;
        var colon = text.LastIndexOf(':');
        if (colon <= 0 || !int.TryParse(text.AsSpan(colon + 1), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out port)) return false;
        if (port is < 1024 or > 65535) return false;
        if (!IPAddress.TryParse(text.AsSpan(0, colon), out var parsed) || parsed.AddressFamily != AddressFamily.InterNetwork) return false;
        address = parsed;
        return IsAllowedAddress(parsed);
    }

    private static byte[] Mac(byte[] key, string label, ReadOnlySpan<byte> data)
    {
        var input = new byte[label.Length + data.Length];
        Encoding.ASCII.GetBytes(label, input);
        data.CopyTo(input.AsSpan(label.Length));
        return HMACSHA256.HashData(key, input)[..MacBytes];
    }

    private static byte[] Concat(params byte[][] parts)
    {
        var result = new byte[parts.Sum(p => p.Length)];
        var pos = 0;
        foreach (var p in parts)
        {
            p.CopyTo(result, pos);
            pos += p.Length;
        }
        return result;
    }
}
