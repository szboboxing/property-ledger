namespace PropertyLedger.Core.Services;

/// <summary>账期日期工具。</summary>
public static class PeriodCalendar
{
    public static DateTime FirstDayOfMonth(DateTime d) => new(d.Year, d.Month, 1);

    public static DateTime LastDayOfMonth(DateTime d) =>
        new(d.Year, d.Month, DateTime.DaysInMonth(d.Year, d.Month));

    public static DateTime FirstDayOfMonth(int year, int month) => new(year, month, 1);

    /// <summary>把付租日（1-31）夹到目标月的合法范围内（如 2 月 31 → 28/29）。</summary>
    public static DateTime DueDate(int year, int month, int paymentDay)
    {
        var day = Math.Clamp(paymentDay, 1, DateTime.DaysInMonth(year, month));
        return new DateTime(year, month, day);
    }

    /// <summary>从 0001-01 起的月份序号，用于季付对齐。</summary>
    public static int MonthIndex(DateTime d) => (d.Year * 12) + (d.Month - 1);
}
