using System.Text.Json;

namespace GlucoseTray.Display;

public interface IWidgetPositionStore
{
    Point? Load();
    void Save(Point position);
}

/// <summary>Stores only window coordinates, independently of account settings.</summary>
public sealed class WidgetPositionStore(string path) : IWidgetPositionStore
{
    public const string FileName = "widget-position.json";

    public Point? Load()
    {
        try
        {
            if (!File.Exists(path)) return null;
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var root = document.RootElement;
            return root.ValueKind == JsonValueKind.Object &&
                root.TryGetProperty("X", out var x) && x.ValueKind == JsonValueKind.Number && x.TryGetInt32(out var left) &&
                root.TryGetProperty("Y", out var y) && y.ValueKind == JsonValueKind.Number && y.TryGetInt32(out var top) ? new Point(left, top) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null; // Preferences must not prevent the glucose display from opening.
        }
    }

    public void Save(Point position)
    {
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(new { position.X, position.Y }));
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}

public static class WidgetPlacement
{
    public static Point KeepVisible(Point requested, Size widgetSize, IReadOnlyList<Rectangle> workingAreas)
    {
        if (workingAreas.Count == 0) return requested;
        var area = workingAreas.OrderBy(a => Distance(requested, a)).First();
        return new Point(
            Math.Clamp(requested.X, area.Left, Math.Max(area.Left, area.Right - widgetSize.Width)),
            Math.Clamp(requested.Y, area.Top, Math.Max(area.Top, area.Bottom - widgetSize.Height)));
    }

    private static double Distance(Point point, Rectangle area)
    {
        if (area.Contains(point)) return -1;
        var dx = Math.Max(Math.Max((double)area.Left - point.X, 0), (double)point.X - area.Right);
        var dy = Math.Max(Math.Max((double)area.Top - point.Y, 0), (double)point.Y - area.Bottom);
        return dx * dx + dy * dy;
    }
}
