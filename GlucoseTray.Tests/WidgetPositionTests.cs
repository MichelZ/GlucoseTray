using System.Drawing;
using System.Text.Json;
using GlucoseTray.Display;

namespace GlucoseTray.Tests;

public class WidgetPositionTests
{
    private string _directory = null!;
    private string _path = null!;

    [SetUp]
    public void SetUp()
    {
        _directory = Path.Combine(Path.GetTempPath(), "GlucoseTray-position-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        _path = Path.Combine(_directory, WidgetPositionStore.FileName);
    }

    [TearDown]
    public void TearDown()
    {
        File.Delete(_path);
        Directory.Delete(_directory);
    }

    [Test]
    public void PositionShouldSurviveASeparateStoreInstanceWithOnlyCoordinatesSaved()
    {
        new WidgetPositionStore(_path).Save(new Point(-640, 230));
        Assert.That(new WidgetPositionStore(_path).Load(), Is.EqualTo(new Point(-640, 230)));
        using var json = JsonDocument.Parse(File.ReadAllText(_path));
        Assert.That(json.RootElement.EnumerateObject().Select(p => p.Name), Is.EquivalentTo(new[] { "X", "Y" }));
        Assert.That(Directory.GetFiles(_directory), Has.Length.EqualTo(1), "Atomic save should leave no temporary copy.");
    }

    [Test]
    public void MissingPositionShouldUseDefaultPlacement() => Assert.That(new WidgetPositionStore(_path).Load(), Is.Null);

    [TestCase("not-json")]
    [TestCase("{}")]
    [TestCase("[]")]
    [TestCase("{\"X\":\"wrong-type\",\"Y\":10}")]
    [TestCase("{\"X\":10,\"Y\":null}")]
    [TestCase("{\"X\":1e50,\"Y\":10}")]
    public void BrokenPreferencesMustNotPreventWidgetOpening(string json)
    {
        File.WriteAllText(_path, json);
        Assert.That(new WidgetPositionStore(_path).Load(), Is.Null);
    }

    [Test]
    public void PositionOnLeftMonitorShouldRemainUnchanged()
    {
        Assert.That(WidgetPlacement.KeepVisible(new Point(-640, 230), new Size(204, 124),
            [new(-1920, 0, 1920, 1080), new(0, 0, 1920, 1040)]), Is.EqualTo(new Point(-640, 230)));
    }

    [Test]
    public void RemovedMonitorShouldBringWidgetBackIntoAvailableWorkingArea()
    {
        Assert.That(WidgetPlacement.KeepVisible(new Point(2200, 1000), new Size(204, 124),
            [new(0, 0, 1920, 1040)]), Is.EqualTo(new Point(1716, 916)));
    }

    [Test]
    public void ExtremeSavedCoordinatesShouldBeClampedWithoutOverflow()
    {
        Assert.That(WidgetPlacement.KeepVisible(new Point(int.MinValue, int.MaxValue), new Size(204, 124),
            [new(0, 0, 1920, 1040)]), Is.EqualTo(new Point(0, 916)));
    }

    [Test]
    public void MonitorGapShouldChooseNearestMonitor()
    {
        Assert.That(WidgetPlacement.KeepVisible(new Point(2200, 200), new Size(204, 124),
            [new(0, 0, 1920, 1040), new(2560, 0, 1920, 1040)]), Is.EqualTo(new Point(1716, 200)));
    }
}
