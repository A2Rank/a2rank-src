namespace Aion2Meter.Core;

/// <summary>Korea Standard Time (UTC+9, no daylight saving): days and clock hours are counted in KST.</summary>
public static class Kst
{
    public static readonly TimeSpan Offset = TimeSpan.FromHours(9);

    public static DateTimeOffset Of(DateTimeOffset t) => t.ToOffset(Offset);

    public static DateOnly Day(DateTimeOffset t) => DateOnly.FromDateTime(Of(t).DateTime);

    public static DateTimeOffset DayStart(DateOnly day) => new(day.ToDateTime(TimeOnly.MinValue), Offset);

    public static DateTimeOffset HourStart(DateTimeOffset t)
    {
        var k = Of(t);
        return new DateTimeOffset(k.Year, k.Month, k.Day, k.Hour, 0, 0, Offset);
    }
}
