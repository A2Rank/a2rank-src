using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Aion2Meter.Core;

namespace Aion2Meter.App;

/// <summary>One counted period of "내 기록" as the window and its charts use it.</summary>
internal sealed record Period(string Character, long Start, long End, ulong T0, ulong T1, long Gain, long Loss, int Events, long Unmatched,
    string Flags, List<(int Size, long Gain, long Loss, int Events)> Parties, List<PeriodKill> Kills, bool Complete, long First, long Last,
    bool IsDay = false, int Count = 1)
{
    /// <summary>"10-14 12:18 ~ 13:10" for a period, "10-14 (수)" for a day (the periods of one Korean-time day added up).</summary>
    public string Label => IsDay ? $"{StartTime:MM-dd} ({StartTime:ddd})" : $"{StartTime:MM-dd HH:mm} ~ {EndTime:HH:mm}";

    public long Net => Gain - Loss;
    public DateTimeOffset StartTime => Kst.Of(DateTimeOffset.FromUnixTimeSeconds(Start));
    public DateTimeOffset EndTime => Kst.Of(DateTimeOffset.FromUnixTimeSeconds(End));
}

internal sealed record PeriodKill(long Time, int Kind, long Delta, int Server, string Name);

/// <summary>
/// A chart drawn straight onto the window (no library): one slot per period, oldest on the left. Clicking a slot picks that period;
/// the picked one is highlighted. Hovering shows what the slot holds.
/// </summary>
internal abstract class PeriodChart : FrameworkElement
{
    protected const double LeftMargin = 62, RightMargin = 8, TopMargin = 8, BottomMargin = 20;

    private IReadOnlyList<Period> _data = [];
    private int _picked = -1;

    protected IReadOnlyList<Period> Data => _data;
    protected int Picked => _picked;

    /// <summary>The user clicked slot <c>index</c> of the data given to <see cref="Set"/>.</summary>
    public event Action<int>? SlotClicked;

    public void Set(IReadOnlyList<Period> data, int picked)
    {
        _data = data;
        _picked = picked;
        InvalidateVisual();
    }

    public void Pick(int picked)
    {
        _picked = picked;
        InvalidateVisual();
    }

    protected double PlotWidth => Math.Max(1, ActualWidth - LeftMargin - RightMargin);
    protected double PlotHeight => Math.Max(1, ActualHeight - TopMargin - BottomMargin);
    protected double Slot => PlotWidth / Math.Max(1, _data.Count);

    protected Brush Res(string key) => (Brush)Application.Current.FindResource(key);

    protected void Text(DrawingContext dc, string text, double x, double y, Brush brush, double size, TextAlignment align = TextAlignment.Left)
    {
        var t = new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, new Typeface("Malgun Gothic"), size, brush,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);
        double left = align switch { TextAlignment.Right => x - t.Width, TextAlignment.Center => x - t.Width / 2, _ => x };
        dc.DrawText(t, new Point(left, y));
    }

    /// <summary>The time labels under the slots: the hour, with the date where the day changes; as many as fit.</summary>
    protected void TimeLabels(DrawingContext dc)
    {
        if (_data.Count == 0) return;
        var faint = Res("FaintBrush");
        int every = Math.Max(1, (int)Math.Ceiling(54 / Math.Max(1, Slot)));
        for (int i = 0; i < _data.Count; i += every)
        {
            var t = _data[i].StartTime;
            bool dayChange = i == 0 || _data[Math.Max(0, i - every)].StartTime.Date != t.Date;
            Text(dc, _data[i].IsDay ? $"{t:MM-dd}" : dayChange ? $"{t:MM-dd HH:mm}" : $"{t:HH:mm}", LeftMargin + Slot * (i + 0.5), ActualHeight - BottomMargin + 4, faint, 9.5, TextAlignment.Center);
        }
    }

    protected void Background(DrawingContext dc) => dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, ActualWidth, ActualHeight));

    protected void PickedBand(DrawingContext dc)
    {
        if (_picked < 0 || _picked >= _data.Count) return;
        var accent = ((SolidColorBrush)Res("AccentBrush")).Color;
        var band = new SolidColorBrush(Color.FromArgb(0x26, accent.R, accent.G, accent.B));
        dc.DrawRoundedRectangle(band, null, new Rect(LeftMargin + Slot * _picked, TopMargin, Slot, PlotHeight), 3, 3);
    }

    private int SlotAt(Point p) => p.X < LeftMargin || p.X > ActualWidth - RightMargin || _data.Count == 0
        ? -1 : Math.Clamp((int)((p.X - LeftMargin) / Slot), 0, _data.Count - 1);

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        int i = SlotAt(e.GetPosition(this));
        if (i >= 0) SlotClicked?.Invoke(i);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        int i = SlotAt(e.GetPosition(this));
        ToolTip = i < 0 ? null : Describe(_data[i]);
    }

    protected static string Describe(Period p) =>
        p.Label + (p.IsDay ? $" · 구간 {p.Count}개" : p.Complete ? "" : " (일부 집계)") +
        $"\n순증 {Fmt.Signed(p.Net)}  (획득 {Fmt.Signed(p.Gain)} · 손실 {(p.Loss == 0 ? "0" : "-" + p.Loss.ToString("N0"))})" +
        $"\n총 {p.T0:N0} → {p.T1:N0}\n처치·사망 {p.Kills.Count(k => k.Kind is 0 or 1 or 3)}건";
}

/// <summary>Points gained (up, green) and lost (down, red) in each period; periods that were cut short are paler.</summary>
internal sealed class GainLossChart : PeriodChart
{
    protected override void OnRender(DrawingContext dc)
    {
        Background(dc);
        var data = Data;
        var faint = Res("FaintBrush");
        var line = new Pen(new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)), 1);
        if (data.Count == 0) return;
        long maxGain = Math.Max(1, data.Max(d => d.Gain)), maxLoss = data.Max(d => d.Loss);
        double range = maxGain + maxLoss, plotH = PlotHeight, zeroY = TopMargin + maxGain / range * plotH;

        PickedBand(dc);
        dc.DrawLine(line, new Point(LeftMargin, zeroY), new Point(ActualWidth - RightMargin, zeroY));
        Text(dc, "+" + maxGain.ToString("N0"), LeftMargin - 6, TopMargin - 2, faint, 9.5, TextAlignment.Right);
        Text(dc, "0", LeftMargin - 6, zeroY - 7, faint, 9.5, TextAlignment.Right);
        if (maxLoss > 0) Text(dc, "-" + maxLoss.ToString("N0"), LeftMargin - 6, TopMargin + plotH - 12, faint, 9.5, TextAlignment.Right);

        double bw = Math.Min(44, Slot * 0.66);
        for (int i = 0; i < data.Count; i++)
        {
            var d = data[i];
            double x = LeftMargin + Slot * i + (Slot - bw) / 2;
            dc.PushOpacity(d.Complete ? 1 : 0.55);
            double gh = d.Gain / range * plotH, lh = d.Loss / range * plotH;
            if (gh > 0) dc.DrawRoundedRectangle(Res("GainBrush"), null, new Rect(x, zeroY - gh, bw, Math.Max(1, gh)), 2, 2);
            if (lh > 0) dc.DrawRoundedRectangle(Res("LossBrush"), null, new Rect(x, zeroY, bw, Math.Max(1, lh)), 2, 2);
            dc.Pop();
        }
        TimeLabels(dc);
    }
}

/// <summary>
/// My total rank points over the periods as a hill: every period is a slope from the total at its start (left edge of its slot) to the total
/// at its end (right edge), so periods that follow each other join into one climbing line; the area under it is filled. A dot marks the end of each.
/// </summary>
internal sealed class TotalChart : PeriodChart
{
    protected override void OnRender(DrawingContext dc)
    {
        Background(dc);
        var data = Data;
        var faint = Res("FaintBrush");
        if (data.Count == 0) return;
        ulong lo = Math.Min(data.Min(d => d.T0), data.Min(d => d.T1)), hi = Math.Max(data.Max(d => d.T0), data.Max(d => d.T1));
        double span = Math.Max(1, hi - lo), plotH = PlotHeight;
        double bottom = TopMargin + plotH;
        double Y(ulong v) => TopMargin + (1 - (v - lo) / span) * plotH;
        double Left(int i) => LeftMargin + Slot * i;          // where period i starts
        double Right(int i) => LeftMargin + Slot * (i + 1);   // where it ends

        PickedBand(dc);
        var grid = new Pen(new SolidColorBrush(Color.FromArgb(0x24, 0xFF, 0xFF, 0xFF)), 1);
        dc.DrawLine(grid, new Point(LeftMargin, TopMargin), new Point(ActualWidth - RightMargin, TopMargin));
        dc.DrawLine(grid, new Point(LeftMargin, bottom), new Point(ActualWidth - RightMargin, bottom));
        Text(dc, hi.ToString("N0"), LeftMargin - 6, TopMargin - 2, faint, 9.5, TextAlignment.Right);
        Text(dc, lo.ToString("N0"), LeftMargin - 6, bottom - 12, faint, 9.5, TextAlignment.Right);

        // the points of the hill: (start, total at start), (end, total at end) of each period, in order
        var points = new List<Point>();
        for (int i = 0; i < data.Count; i++)
        {
            points.Add(new Point(Left(i), Y(data[i].T0)));
            points.Add(new Point(Right(i), Y(data[i].T1)));
        }

        var accent = Res("AccentBrush");
        var color = ((SolidColorBrush)accent).Color;
        var line = new StreamGeometry();
        using (var ctx = line.Open())
        {
            ctx.BeginFigure(points[0], false, false);
            foreach (var p in points.Skip(1)) ctx.LineTo(p, true, false);
        }
        line.Freeze();
        var area = new StreamGeometry();
        using (var ctx = area.Open())
        {
            ctx.BeginFigure(new Point(points[0].X, bottom), true, true);
            foreach (var p in points) ctx.LineTo(p, true, false);
            ctx.LineTo(new Point(points[^1].X, bottom), true, false);
        }
        area.Freeze();
        var fill = new LinearGradientBrush(Color.FromArgb(0x55, color.R, color.G, color.B), Color.FromArgb(0x05, color.R, color.G, color.B), 90);
        dc.DrawGeometry(fill, null, area);
        dc.DrawGeometry(null, new Pen(accent, 2) { LineJoin = PenLineJoin.Round, StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, line);
        for (int i = 0; i < data.Count; i++)
            dc.DrawEllipse(i == Picked ? Res("TextBrush") : accent, null, new Point(Right(i), Y(data[i].T1)), i == Picked ? 4.5 : 2.8, i == Picked ? 4.5 : 2.8);
        TimeLabels(dc);
    }
}
