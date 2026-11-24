using Aion2Meter.Protocol;
using SharpPcap;
using SharpPcap.LibPcap;

namespace Aion2Meter.Capture;

/// <summary>
/// Captures game server traffic on every network adapter through Npcap (installed separately, not bundled)
/// and turns it into game events. <see cref="Events"/> is raised on capture threads, one call at a time.
/// </summary>
public sealed class CaptureService : IDisposable
{
    public const string Filter = "tcp src port 13328";

    private readonly object _lock = new();
    private readonly Pipeline _pipeline = new();
    private readonly List<LibPcapLiveDevice> _open = [];
    private PcapWriter? _recorder;
    private DateTimeOffset _lastFlush;
    private string? _recordFolder;
    private uint? _self;
    private long _lastCombat;
    private readonly Queue<Frame> _ring = new();
    private long _ringBytes;
    private readonly Dictionary<Type, Frame> _context = [];

    private readonly record struct Frame(DateTimeOffset Time, int LinkType, byte[] Data);

    /// <summary>The recording starts with this much of what came before the first blow of a fight (the approach, the buffs).</summary>
    public static readonly TimeSpan PreRoll = TimeSpan.FromSeconds(60);
    /// <summary>The recording goes on this long after the last blow of a fight (the points and the ranking arrive late).</summary>
    public static readonly TimeSpan Tail = TimeSpan.FromMinutes(5);
    private const long RingLimit = 4 * 1024 * 1024;

    public event Action<List<CaptureEvent>>? Events;

    public bool Running { get; private set; }
    public long Packets;
    /// <summary>The pcap file being written: only while a fight is going on (and for 5 minutes after it), otherwise null.</summary>
    public string? RecordingPath => _recordingPath;
    private string? _recordingPath;

    /// <summary>Recording is on: a file is started with the next fight.</summary>
    public bool RecordingArmed => _recordFolder is not null;

    /// <summary>Why capturing cannot work on this PC, or null when Npcap is usable.</summary>
    public static string? Problem()
    {
        try
        {
            return LibPcapLiveDeviceList.New().Count == 0 ? "네트워크 장치를 찾지 못했습니다." : null;
        }
        catch (Exception e) when (e is DllNotFoundException or TypeInitializationException or BadImageFormatException or PcapException)
        {
            return "Npcap이 설치되어 있지 않습니다.";
        }
    }

    /// <param name="recordDirectory">When set, the raw game packets of every fight (from 60 s before its first blow to 5 minutes after its last) are written to pcap files there.</param>
    /// <returns>Number of adapters capturing.</returns>
    public int Start(string? recordDirectory = null)
    {
        Stop();
        lock (_lock)
        {
            _recordingPath = null;
            _recordFolder = recordDirectory;
            _ring.Clear();
            _ringBytes = 0;
            _context.Clear();
            _self = null;
            if (recordDirectory is not null) Directory.CreateDirectory(recordDirectory);
        }
        foreach (var device in LibPcapLiveDeviceList.New())
        {
            if (device.Loopback || device.Addresses.Count == 0) continue;
            try
            {
                device.Open(new DeviceConfiguration { Mode = DeviceModes.MaxResponsiveness, ReadTimeout = 100 });
                device.Filter = Filter;
                device.OnPacketArrival += OnPacket;
                device.StartCapture();
                _open.Add(device);
            }
            catch (PcapException)
            {
                device.Close(); // adapters that are down or not capturable
            }
        }
        Running = _open.Count > 0;
        return _open.Count;
    }

    public void Stop()
    {
        foreach (var device in _open)
        {
            device.OnPacketArrival -= OnPacket;
            try
            {
                device.StopCapture();
            }
            catch (PcapException) { }
            device.Close();
        }
        _open.Clear();
        Running = false;
        lock (_lock)
        {
            CloseRecording();
            _recordFolder = null;
            _ring.Clear();
            _ringBytes = 0;
        }
    }

    /// <summary>
    /// Ends the fight's pcap now (all of it is on disk then); the next fight starts a new file. The "send data" window uses it so that
    /// the fight that just happened is a finished file. Capturing and measuring are not interrupted.
    /// </summary>
    /// <returns>The closed file, or null when no file was open.</returns>
    public string? CutRecording()
    {
        lock (_lock)
        {
            var closed = _recordingPath;
            CloseRecording();
            return closed;
        }
    }

    /// <summary>Called about once a second: a fight's file is closed 5 minutes after its last blow even when no more packets come.</summary>
    public void Tick()
    {
        lock (_lock)
        {
            if (_recorder is not null && Environment.TickCount64 - _lastCombat > Tail.TotalMilliseconds) CloseRecording();
        }
    }

    private void CloseRecording()
    {
        _recorder?.Dispose();
        _recorder = null;
        _recordingPath = null;
    }

    private void OnPacket(object sender, PacketCapture e) =>
        Handle(e.Data, (int)e.Device.LinkType, new DateTimeOffset(e.Header.Timeval.Date, TimeSpan.Zero));

    /// <summary>One captured frame: counted, recorded when a fight is on, turned into events. Also for tests and tools that play a recording through the same path.</summary>
    /// <param name="now">What "now" is for the closing of a fight's file (milliseconds, <see cref="Environment.TickCount64"/> by default).</param>
    public void Handle(ReadOnlySpan<byte> data, int linkType, DateTimeOffset time, long? now = null)
    {
        List<CaptureEvent> events;
        lock (_lock)
        {
            Interlocked.Increment(ref Packets);
            events = _pipeline.Feed(data, linkType, time);
            if (_recordFolder is not null) Record(new Frame(time, linkType, data.ToArray()), events, now ?? Environment.TickCount64);
        }
        if (events.Count > 0) Events?.Invoke(events);
    }

    /// <summary>Turns recording on without opening a network adapter (tests and tools that play a recording with <see cref="Handle"/>).</summary>
    public void ArmRecording(string folder)
    {
        lock (_lock)
        {
            Directory.CreateDirectory(folder);
            _recordFolder = folder;
        }
    }

    /// <summary>
    /// Idle: the last minute of frames is kept in memory, together with the newest frame of what a recording needs to be understood on its own
    /// (who "me" is, the points, the ranking, the boss list). The first blow of a fight that involves me opens a file that starts with those and
    /// the minute before; the file is closed 5 minutes after the last such blow.
    /// </summary>
    private void Record(Frame frame, List<CaptureEvent> events, long now)
    {
        bool combat = false;
        foreach (var ev in events)
        {
            if (ev is not MessageEvent { Message: var m }) continue;
            switch (m)
            {
                case EnterWorldMessage w:
                    _self = w.Entity;
                    _context[typeof(EnterWorldMessage)] = frame;
                    break;
                case PointMessage or RankingMessage or FieldBossMessage:
                    _context[m.GetType()] = frame;
                    break;
            }
            combat |= IsMyFight(m);
        }
        if (combat) _lastCombat = now;
        if (_recorder is null)
        {
            _ring.Enqueue(frame);
            _ringBytes += frame.Data.Length;
            while (_ring.Count > 0 && (frame.Time - _ring.Peek().Time > PreRoll || _ringBytes > RingLimit)) _ringBytes -= _ring.Dequeue().Data.Length;
            if (combat) OpenRecording();
            return;
        }
        Write(frame);
        if (!combat && now - _lastCombat > Tail.TotalMilliseconds) CloseRecording();
    }

    /// <summary>A blow, a dodge or a death in which I am the one who hits or is hit (anything, as long as I am not known yet).</summary>
    private bool IsMyFight(GameMessage m) => m switch
    {
        DamageMessage d => (d.Flags & 0x02) != 0 && Involves(d.Source, d.Target),
        DodgeMessage d => Involves(d.Source, d.Target),
        PeriodicMessage { Damage: true } p => Involves(p.Source, p.Target),
        DeathMessage d => Involves(d.Killer, d.Victim),
        _ => false,
    };

    private bool Involves(uint a, uint b) => _self is not { } me || a == me || b == me;

    private void OpenRecording()
    {
        var folder = _recordFolder!;
        var first = _ring.Peek().Time;
        string path = Path.Combine(folder, $"capture-{DateTime.Now:yyyyMMdd-HHmmss}.pcap");
        for (int n = 2; File.Exists(path); n++) path = Path.Combine(folder, $"capture-{DateTime.Now:yyyyMMdd-HHmmss}-{n}.pcap");
        try
        {
            _recorder = new PcapWriter(path, _ring.Peek().LinkType);
        }
        catch (IOException)
        {
            return;   // the folder cannot be written now: the next blow tries again
        }
        _recordingPath = path;
        // what was seen before the minute (who I am ...), oldest first; the minute follows
        foreach (var f in _context.Values.Where(f => f.Time < first).Distinct().OrderBy(f => f.Time)) Write(f);
        while (_ring.Count > 0) Write(_ring.Dequeue());
        _ringBytes = 0;
    }

    private void Write(Frame frame)
    {
        if (_recorder is null || _recorder.LinkType != frame.LinkType) return;   // one link type per file; game traffic uses one adapter
        _recorder.Write(frame.Time, frame.Data);
        if (frame.Time - _lastFlush > TimeSpan.FromSeconds(2))
        {
            _recorder.Flush();
            _lastFlush = frame.Time;
        }
    }

    public void Dispose() => Stop();
}
