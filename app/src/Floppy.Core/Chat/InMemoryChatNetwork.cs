namespace Floppy.Core.Chat;

/// <summary>
/// Schein-Netz im Speicher: mehrere Chat-Sitzungen in einem Prozess, ganz ohne Netzwerk.
/// Fuer Tests und die Vorschau in der App (Bildschirmfotos). "Online" verhaelt sich wie ntfy
/// (jeder im Thema bekommt alles, auch der Absender), "lokal" ist ein voll verbundener Raum.
/// </summary>
public sealed class InMemoryChatHub
{
    private int _nextHost;
    internal readonly object Lock = new();
    internal List<InMemoryOnline> Online { get; } = [];
    internal List<InMemoryLocal> Local { get; } = [];

    /// <summary>Wie viele Nachrichten ueber den "Dienst" gingen.</summary>
    public int OnlineFrames { get; internal set; }

    public int LocalRooms
    {
        get { lock (Lock) return Local.Count; }
    }

    internal string NextEndpoint() => $"10.0.0.{Interlocked.Increment(ref _nextHost)}:45817";

    /// <summary>Rohes Paket an alle online im Thema schicken (Tests: Unsinn einschleusen).</summary>
    public void Inject(string topic, byte[] frame)
    {
        List<InMemoryOnline> targets;
        lock (Lock) targets = Online.Where(o => o.Topic == topic).ToList();
        foreach (var t in targets) t.Deliver(frame);
    }
}

/// <summary>Das Netz aus Sicht eines Teilnehmers.</summary>
public sealed class InMemoryChatNetwork(InMemoryChatHub hub) : IChatNetwork
{
    /// <summary>false = anderes Netzwerk: lokal nicht erreichbar.</summary>
    public bool SameLan { get; set; } = true;

    public string ServiceName { get; init; } = "ntfy.sh";

    public IChatTransport CreateOnline(ChatRoomKey room) => new InMemoryOnline(hub, room.Topic);

    public ILocalChatTransport CreateLocal(ChatRoomKey room) => new InMemoryLocal(hub, room.Topic, this);

    public Task<IReadOnlyList<string>> ProbeLocalAsync(ChatRoomKey room, TimeSpan timeout)
    {
        if (!SameLan) return Task.FromResult<IReadOnlyList<string>>([]);
        lock (hub.Lock)
            return Task.FromResult<IReadOnlyList<string>>(hub.Local.Where(l => l.Topic == room.Topic && l.Network.SameLan).SelectMany(l => l.Endpoints).ToList());
    }
}

internal sealed class InMemoryOnline(InMemoryChatHub hub, string topic) : IChatTransport
{
    public string Topic { get; } = topic;
    public ChatMode Mode => ChatMode.Online;
    public event Action<byte[], object?>? FrameReceived;
    public event Action<ChatLinkState, string?>? StateChanged;

    public void Start()
    {
        lock (hub.Lock) hub.Online.Add(this);
        StateChanged?.Invoke(ChatLinkState.Connected, null);
    }

    public void Send(byte[] frame)
    {
        lock (hub.Lock) hub.OnlineFrames++;
        hub.Inject(Topic, frame);   // wie ntfy: auch an sich selbst
    }

    internal void Deliver(byte[] frame) => FrameReceived?.Invoke(frame, null);

    public void Relay(byte[] frame, object? origin) { }
    public void Reject(object? origin) { }
    public void Close(TimeSpan flush) => Dispose();

    public void Dispose()
    {
        lock (hub.Lock) hub.Online.Remove(this);
    }
}

internal sealed class InMemoryLocal(InMemoryChatHub hub, string topic, InMemoryChatNetwork network) : ILocalChatTransport
{
    private HashSet<InMemoryLocal> _group = [];
    private bool _started;

    public string Topic { get; } = topic;
    public InMemoryChatNetwork Network { get; } = network;
    public ChatMode Mode => ChatMode.Local;
    public IReadOnlyList<string> Endpoints { get; private set; } = [];

    public int PeerCount
    {
        get { lock (hub.Lock) return Math.Max(0, _group.Count - 1); }
    }

    public event Action<byte[], object?>? FrameReceived;
    public event Action<ChatLinkState, string?>? StateChanged;

    public void Start()
    {
        if (_started) return;
        _started = true;
        Endpoints = [hub.NextEndpoint()];
        lock (hub.Lock)
        {
            _group = [this];
            hub.Local.Add(this);
        }
    }

    public Task<bool> ConnectAsync(IReadOnlyList<string> endpoints, TimeSpan timeout)
    {
        List<InMemoryLocal> newlyConnected;
        lock (hub.Lock)
        {
            var target = hub.Local.FirstOrDefault(l => l != this && l.Topic == Topic && l.Endpoints.Any(endpoints.Contains));
            if (target is null || !Network.SameLan || !target.Network.SameLan) return Task.FromResult(false);
            if (target._group == _group) return Task.FromResult(true);

            var merged = new HashSet<InMemoryLocal>(_group.Concat(target._group));
            newlyConnected = merged.Where(l => l._group.Count == 1).ToList();
            foreach (var l in merged) l._group = merged;
        }
        foreach (var l in newlyConnected) l.StateChanged?.Invoke(ChatLinkState.Connected, null);
        return Task.FromResult(true);
    }

    public void Send(byte[] frame)
    {
        List<InMemoryLocal> targets;
        lock (hub.Lock) targets = _group.Where(l => l != this).ToList();
        foreach (var t in targets) t.FrameReceived?.Invoke(frame, this);
    }

    public void Relay(byte[] frame, object? origin) { }
    public void Reject(object? origin) { }
    public void Close(TimeSpan flush) => Dispose();

    public void Dispose()
    {
        List<InMemoryLocal> alone;
        lock (hub.Lock)
        {
            hub.Local.Remove(this);
            _group.Remove(this);
            alone = _group.Count == 1 ? [.. _group] : [];
            _group = [this];
        }
        foreach (var l in alone) l.StateChanged?.Invoke(ChatLinkState.Connecting, "waiting");
    }
}
