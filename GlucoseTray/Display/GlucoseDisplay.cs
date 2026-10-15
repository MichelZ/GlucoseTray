using GlucoseTray.Enums;
using System.Globalization;

namespace GlucoseTray.Display;

public class GlucoseDisplay
{
    public string? ConnectionError { get; set; }
    public string DisplayValue { get; set; } = string.Empty;
    public string? WidgetValue { get; set; }
    public bool IsStale { get; set; }
    public DateTime TimestampUtc { get; set; }
    public Trend Trend { get; set; }
    public int FontSize { get; set; }
    public IconTextColor Color { get; set; } = IconTextColor.White;
    public string RangeLabel { get; set; } = "In range";
    public string UnitLabel { get; set; } = "mmol/L";
    public int MinutesUntilStale { get; set; } = 15;

    public bool IsStaleAt(DateTime utcNow) => IsStale ||
        (TimestampUtc != DateTime.MinValue && TimestampUtc < utcNow.AddMinutes(-MinutesUntilStale));

    public string GetWidgetStatus(DateTime utcNow) => TimestampUtc == DateTime.MinValue ? "Waiting for data" :
        ConnectionError is not null ? "Connection problem" : IsStaleAt(utcNow) ? "Stale reading" : RangeLabel;

    public string GetDisplayMessage(DateTime utcNow)
    {
        if (TimestampUtc == DateTime.MinValue)
            return ConnectionError ?? "No glucose reading available yet.";
        var staleMessage = IsStale ? $"\r\n{Math.Abs((utcNow - TimestampUtc).TotalMinutes).ToString("#", CultureInfo.InvariantCulture)} minutes ago" : string.Empty;
        var message = $"{DisplayValue} {TimestampUtc.ToLocalTime().ToLongTimeString()} {Trend.GetTrendArrow()} {staleMessage}".Trim();
        return ConnectionError is null ? message : $"{ConnectionError}\r\nLast reading: {message}";
    }
}
