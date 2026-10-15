using GlucoseTray.Enums;
using GlucoseTray.Read;
using Microsoft.Extensions.Options;
using System.Globalization;

namespace GlucoseTray.Display;

public interface IGlucoseDisplayMapper
{
    GlucoseDisplay Map(GlucoseReading reading);
}

public class GlucoseDisplayMapper(IOptionsMonitor<AppSettings> options) : IGlucoseDisplayMapper
{
    public GlucoseDisplay Map(GlucoseReading reading)
    {
        return new GlucoseDisplay
        {
            ConnectionError = reading.ConnectionError,
            DisplayValue = GetDisplayValue(reading),
            WidgetValue = options.CurrentValue.DisplayUnitType == GlucoseUnitType.Mg ?
                reading.MgValue.ToString(CultureInfo.InvariantCulture) : reading.MmolValue.ToString("0.0", CultureInfo.InvariantCulture),
            Color = options.CurrentValue.DisplayUnitType == GlucoseUnitType.Mg ? GetColor(reading.MgValue) : GetColor(reading.MmolValue),
            FontSize = GetFontSize(reading),
            TimestampUtc = reading.TimestampUtc,
            Trend = reading.Trend,
            IsStale = reading.TimestampUtc < DateTime.UtcNow.AddMinutes(-options.CurrentValue.MinutesUntilStale),
            RangeLabel = GetRangeLabel(reading),
            UnitLabel = options.CurrentValue.DisplayUnitType == GlucoseUnitType.Mg ? "mg/dL" : "mmol/L",
            MinutesUntilStale = options.CurrentValue.MinutesUntilStale,
        };
    }

    private string GetRangeLabel(GlucoseReading reading)
    {
        var s = options.CurrentValue;
        var mg = s.DisplayUnitType == GlucoseUnitType.Mg;
        var value = mg ? reading.MgValue : reading.MmolValue;
        if (value <= (mg ? s.CriticalLowMgThreshold : s.CriticalLowMmolThreshold)) return "Critical low";
        if (value <= (mg ? s.LowMgThreshold : s.LowMmolThreshold)) return "Low";
        if (value >= (mg ? s.CriticalHighMgThreshold : s.CriticalHighMmolThreshold)) return "Critical high";
        if (value >= (mg ? s.HighMgThreshold : s.HighMmolThreshold)) return "High";
        return "In range";
    }

    private int GetFontSize(GlucoseReading reading)
    {
        var defaultFontSize = 40;
        var smallerFontSize = 38;

        return options.CurrentValue.DisplayUnitType == GlucoseUnitType.Mmol && reading.MmolValue >= 10 ? smallerFontSize : defaultFontSize;
    }

    private string GetDisplayValue(GlucoseReading reading)
    {
        var displayUnitType = options.CurrentValue.DisplayUnitType;

        if (displayUnitType == GlucoseUnitType.Mg)
        {
            if (reading.MgValue == 0)
                return "NUL";
            if (IsCriticalLow(reading.MgValue))
                return "DAN";
            return reading.MgValue.ToString();
        }
        else
        {
            if (reading.MmolValue == 0)
                return "NUL";
            if (IsCriticalLow(reading.MmolValue))
                return "DAN";
            return reading.MmolValue.ToString("0.0", CultureInfo.InvariantCulture);
        }
    }

    private bool IsCriticalLow(int mgValue) => mgValue <= options.CurrentValue.CriticalLowMgThreshold;
    private bool IsCriticalLow(float mmolValue) => mmolValue <= options.CurrentValue.CriticalLowMmolThreshold;

    private IconTextColor GetColor(int mgValue)
    {
        var settings = options.CurrentValue;
        return GetColor(mgValue, settings.CriticalLowMgThreshold, settings.LowMgThreshold, settings.HighMgThreshold, settings.CriticalHighMgThreshold);
    }

    private IconTextColor GetColor(float mmolValue)
    {
        var settings = options.CurrentValue;
        return GetColor(mmolValue, settings.CriticalLowMmolThreshold, settings.LowMmolThreshold, settings.HighMmolThreshold, settings.CriticalHighMmolThreshold);
    }

    private IconTextColor GetColor(float value, float criticalLow, float low, float high, float criticalHigh)
    {
        if (value <= criticalLow)
            return IconTextColor.Red;
        else if (value <= low)
            return options.CurrentValue.IsDarkMode ? IconTextColor.Yellow : IconTextColor.Gold;
        else if (value >= criticalHigh)
            return IconTextColor.Red;
        else if (value >= high)
            return options.CurrentValue.IsDarkMode ? IconTextColor.Yellow : IconTextColor.Gold;
        else
            return options.CurrentValue.IsDarkMode ? IconTextColor.White : IconTextColor.Black;
    }
}
