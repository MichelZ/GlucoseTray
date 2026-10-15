using GlucoseTray.Enums;
using System.Drawing.Drawing2D;

namespace GlucoseTray.Display;

/// <summary>A passive, draggable view of the same reading used by the tray.</summary>
public sealed class GlucoseWidget : Form
{
    private GlucoseDisplay _display = new();
    private readonly System.Windows.Forms.Timer _clock = new() { Interval = 1000 };
    private Point? _dragOrigin;
    public event EventHandler? HideRequested;

    public GlucoseWidget()
    {
        Text = "GlucoseTray widget";
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(204, 124);
        BackColor = Color.FromArgb(25, 30, 40);
        ForeColor = Color.White;
        DoubleBuffered = true;
        StartPosition = FormStartPosition.Manual;
        var area = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1024, 768);
        Location = new Point(area.Right - Width - 20, area.Bottom - Height - 20);
        _clock.Tick += (_, _) => Invalidate();
        _clock.Start();
        AccessibleName = "Glucose reading";
    }

    protected override bool ShowWithoutActivation => true;

    public void UpdateReading(GlucoseDisplay display)
    {
        _display = display;
        AccessibleDescription = display.GetDisplayMessage(DateTime.UtcNow);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.ScaleTransform(ClientSize.Width / 204f, ClientSize.Height / 124f);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var now = DateTime.UtcNow;
        var unavailable = _display.TimestampUtc == DateTime.MinValue;
        var stale = _display.IsStaleAt(now);
        var color = unavailable || stale || _display.ConnectionError is not null ? Color.FromArgb(164, 174, 190) :
            _display.Color == IconTextColor.Red ? Color.FromArgb(255, 106, 117) :
            _display.Color is IconTextColor.Yellow or IconTextColor.Gold ? Color.FromArgb(255, 207, 86) :
            Color.FromArgb(91, 223, 171);
        using var accent = new SolidBrush(color);
        g.FillRectangle(accent, 0, 0, 4, 124);
        using var border = new Pen(Color.FromArgb(65, 74, 92));
        g.DrawRectangle(border, 0, 0, 203, 123);
        using var caption = new Font("Segoe UI", 12, FontStyle.Bold, GraphicsUnit.Pixel);
        using var number = new Font("Segoe UI", 39, FontStyle.Bold, GraphicsUnit.Pixel);
        using var detail = new Font("Segoe UI", 12, FontStyle.Regular, GraphicsUnit.Pixel);
        using var arrow = new Font("Segoe UI Symbol", 29, FontStyle.Regular, GraphicsUnit.Pixel);
        using var muted = new SolidBrush(Color.FromArgb(183, 192, 207));
        g.DrawString(_display.GetWidgetStatus(now), caption, accent, 12, 9);
        g.DrawString("×", caption, muted, 179, 8);
        g.DrawString(unavailable ? "—" : _display.WidgetValue ?? _display.DisplayValue, number, accent, 9, 30);
        if (!unavailable && !stale && _display.ConnectionError is null && _display.Trend != Trend.Unknown)
            g.DrawString(_display.Trend.GetTrendArrow(), arrow, accent, 161, 39);
        var age = unavailable ? "Waiting for first reading" :
            _display.ConnectionError is not null || stale ? $"Last reading · {Age(now)}" : Age(now);
        g.DrawString(_display.UnitLabel, detail, muted, 13, 83);
        g.DrawString(age, detail, muted, 13, 102);
    }

    private string Age(DateTime now)
    {
        var minutes = Math.Max(0, (int)(now - _display.TimestampUtc).TotalMinutes);
        return minutes == 0 ? "Updated just now" : $"{minutes} min ago";
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;
        if (e.X >= ClientSize.Width * 174 / 204 && e.Y <= ClientSize.Height * 30 / 124) { HideRequested?.Invoke(this, EventArgs.Empty); return; }
        _dragOrigin = e.Location;
        Capture = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_dragOrigin is not Point origin) return;
        var cursor = PointToScreen(e.Location);
        var area = Screen.FromPoint(cursor).WorkingArea;
        Location = new Point(Math.Clamp(cursor.X - origin.X, area.Left, Math.Max(area.Left, area.Right - Width)),
            Math.Clamp(cursor.Y - origin.Y, area.Top, Math.Max(area.Top, area.Bottom - Height)));
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        _dragOrigin = null;
        Capture = false;
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            HideRequested?.Invoke(this, EventArgs.Empty);
        }
        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _clock.Dispose();
        base.Dispose(disposing);
    }
}
