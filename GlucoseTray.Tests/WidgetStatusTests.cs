using GlucoseTray;
using GlucoseTray.Display;
using GlucoseTray.Enums;
using GlucoseTray.Read;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace GlucoseTray.Tests;

public class WidgetStatusTests
{
    [TestCase(3.0f, "Critical low")]
    [TestCase(3.8f, "Low")]
    [TestCase(8.1f, "In range")]
    [TestCase(13.8f, "High")]
    [TestCase(16.6f, "Critical high")]
    public void WidgetRangeShouldMatchConfiguredThresholds(float value, string expected)
    {
        var options = Substitute.For<IOptionsMonitor<AppSettings>>();
        options.CurrentValue.Returns(new AppSettings { DisplayUnitType = GlucoseUnitType.Mmol });
        var display = new GlucoseDisplayMapper(options).Map(new GlucoseReading
        {
            MmolValue = value, MgValue = (int)(value * 18.0182), TimestampUtc = DateTime.UtcNow,
        });
        Assert.That(display.GetWidgetStatus(DateTime.UtcNow), Is.EqualTo(expected));
        Assert.That(display.WidgetValue, Is.EqualTo(value.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)));
    }

    [Test]
    public void ReadingShouldBecomeStaleWithoutAnotherPoll()
    {
        var now = DateTime.UtcNow;
        var display = new GlucoseDisplay { TimestampUtc = now, MinutesUntilStale = 15 };
        Assert.That(display.GetWidgetStatus(now), Is.EqualTo("In range"));
        Assert.That(display.GetWidgetStatus(now.AddMinutes(16)), Is.EqualTo("Stale reading"));
    }

    [Test]
    public void ConnectionErrorMustOverrideRangeLabelOnCachedReading()
    {
        var display = new GlucoseDisplay { TimestampUtc = DateTime.UtcNow, RangeLabel = "In range", ConnectionError = "Offline" };
        Assert.That(display.GetWidgetStatus(DateTime.UtcNow), Is.EqualTo("Connection problem"));
    }

    [Test]
    public void MissingReadingMustNeverShowInRange()
    {
        Assert.That(new GlucoseDisplay().GetWidgetStatus(DateTime.UtcNow), Is.EqualTo("Waiting for data"));
    }
}
