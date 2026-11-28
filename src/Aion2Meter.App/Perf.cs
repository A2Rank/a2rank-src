using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Aion2Meter.App;

/// <summary>
/// Finds what slows a PC down in a crowd: a step that takes long (<see cref="Note"/>) and a stretch of high processor use (<see cref="Sample"/>, with the share of
/// the window thread, the capture thread and the rest) are written to app.log, at most a few lines a minute. Nothing is written while all is quiet.
/// </summary>
internal static class Perf
{
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();

    private static readonly Process Self = Process.GetCurrentProcess();
    private static long _lastLine;
    private static int _suppressed;
    private static TimeSpan _cpu;
    private static long _cpuAt = Stopwatch.GetTimestamp();
    private static int _ticks;
    private static uint _ui;
    private static readonly HashSet<uint> Capture = [];
    private static Dictionary<int, double> _threads = [];

    /// <summary>Writes a line when the step that began at <paramref name="began"/> took at least <paramref name="limitMs"/> ms.</summary>
    public static void Note(string what, long began, int limitMs, int count)
    {
        double ms = Stopwatch.GetElapsedTime(began).TotalMilliseconds;
        if (ms >= limitMs) Line($"slow: {what} {ms:0} ms" + (count > 0 ? $" ({count} events)" : ""));
    }

    /// <summary>Called from the thread that captures: its processor time is counted apart.</summary>
    public static void CaptureThread()
    {
        uint id = GetCurrentThreadId();
        lock (Capture) Capture.Add(id);
    }

    /// <summary>Called every second: processor use of the app over the last 10 seconds, when it is more than 20% of one core; plus where it went.</summary>
    public static void Sample()
    {
        if (_ui == 0) _ui = GetCurrentThreadId();
        if (++_ticks % 10 != 0) return;
        try
        {
            Self.Refresh();
            var cpu = Self.TotalProcessorTime;
            double wall = Stopwatch.GetElapsedTime(_cpuAt).TotalSeconds;
            double use = (cpu - _cpu).TotalSeconds / wall * 100;
            (_cpu, _cpuAt) = (cpu, Stopwatch.GetTimestamp());
            var now = new Dictionary<int, double>();
            double ui = 0, cap = 0, rest = 0;
            foreach (ProcessThread t in Self.Threads)
            {
                double ms = t.TotalProcessorTime.TotalMilliseconds;
                now[t.Id] = ms;
                double d = ms - _threads.GetValueOrDefault(t.Id);
                bool isCapture; lock (Capture) isCapture = Capture.Contains((uint)t.Id);
                if (t.Id == _ui) ui += d; else if (isCapture) cap += d; else rest += d;
            }
            _threads = now;
            double pct(double ms) => ms / 1000 / wall * 100;
            if (use >= 20) Line($"busy: {use:0}% of one core over {wall:0} s (window thread {pct(ui):0}%, capture {pct(cap):0}%, other threads {pct(rest):0}%), {Self.Threads.Count} threads, {Self.PrivateMemorySize64 / 1048576} MB");
        }
        catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception) { }
    }

    private static void Line(string text)
    {
        long now = Environment.TickCount64;
        if (now - _lastLine < 2000) { _suppressed++; return; }   // a crowd makes many of them: a line every 2 seconds is enough
        _lastLine = now;
        Log.Write("perf " + text + (_suppressed > 0 ? $" (+{_suppressed} more)" : ""));
        _suppressed = 0;
    }
}
