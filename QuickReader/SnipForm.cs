using System.ComponentModel;
using System.Drawing.Imaging;

namespace QuickReader;

/// <summary>
/// Darkens all screens and lets you drag a box around some text.
/// The box stays clear so you can see exactly what will be read.
/// No screenshot is kept in memory while you drag; only the chosen area is captured afterwards.
/// </summary>
[DesignerCategory("Code")]
internal sealed class SnipForm : Form
{
    // Pixels painted in this colour become fully see-through (the clear box).
    private static readonly Color HoleColour = Color.FromArgb(255, 1, 254, 2);
    private const string HelpText = "Drag a box around the text you want to hear      Esc or right-click to cancel";

    private Point _start;
    private Rectangle _selection; // in this window's pixels
    private bool _selecting;
    private bool _finished;
    private Font? _font;
    private Rectangle _selectedArea;

    public SnipForm()
    {
        Text = "Quick Reader snip";
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.None;
        KeyPreview = true;
        Cursor = Cursors.Cross;
        BackColor = Color.Black;
        Opacity = 0.45;
        TransparencyKey = HoleColour;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
        Bounds = SystemInformation.VirtualScreen; // all monitors
    }

    /// <summary>The chosen area in screen pixels, or empty if the snip was cancelled.</summary>
    public Rectangle SelectedArea => _selectedArea;

    /// <summary>Takes a picture of part of the screen.</summary>
    public static Bitmap CaptureScreen(Rectangle area)
    {
        var picture = new Bitmap(area.Width, area.Height, PixelFormat.Format32bppArgb);
        using Graphics g = Graphics.FromImage(picture);
        g.CopyFromScreen(area.Left, area.Top, 0, 0, area.Size, CopyPixelOperation.SourceCopy);
        return picture;
    }

    public void Cancel()
    {
        _selectedArea = Rectangle.Empty;
        Finish();
    }

    protected override CreateParams CreateParams
    {
        get
        {
            CreateParams cp = base.CreateParams;
            cp.ExStyle |= NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_TOPMOST;
            return cp;
        }
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        Bounds = SystemInformation.VirtualScreen; // in case Windows resized it for a different DPI
        _font = new Font("Segoe UI", 20f * DeviceDpi / 96f, GraphicsUnit.Pixel);
        Activate();
        NativeMethods.SetForegroundWindow(Handle);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.Clear(Color.Black);

        if (_font is not null)
        {
            // Help text across the top of the main screen.
            Rectangle primary = Screen.PrimaryScreen?.Bounds ?? SystemInformation.VirtualScreen;
            var area = new Rectangle(primary.Left - Left, primary.Top - Top + (int)(40 * DeviceDpi / 96f),
                primary.Width, _font.Height * 2);
            TextRenderer.DrawText(g, HelpText, _font, area, Color.White,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.Top | TextFormatFlags.NoPrefix);
        }

        if (_selection.Width > 0 && _selection.Height > 0)
        {
            int border = Math.Max(1, (int)Math.Round(DeviceDpi / 96.0 * 1.5));
            using var white = new SolidBrush(Color.White);
            using var hole = new SolidBrush(HoleColour);
            g.FillRectangle(white, Rectangle.Inflate(_selection, border, border));
            g.FillRectangle(hole, _selection);
        }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button == MouseButtons.Right)
        {
            Cancel();
            return;
        }
        if (e.Button != MouseButtons.Left)
            return;

        _selecting = true;
        _start = e.Location;
        _selection = Rectangle.Empty;
        Capture = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_selecting)
            return;

        Rectangle old = _selection;
        _selection = FromPoints(_start, e.Location);
        Rectangle changed = old.IsEmpty ? _selection : Rectangle.Union(old, _selection);
        int margin = (int)Math.Ceiling(4 * DeviceDpi / 96.0);
        Invalidate(Rectangle.Inflate(changed, margin, margin));
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (!_selecting || e.Button != MouseButtons.Left)
            return;

        _selecting = false;
        Capture = false;
        Rectangle chosen = FromPoints(_start, e.Location);
        int minimum = (int)Math.Ceiling(6 * DeviceDpi / 96.0);
        _selectedArea = chosen.Width >= minimum && chosen.Height >= minimum ? RectangleToScreen(chosen) : Rectangle.Empty;
        Finish();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode == Keys.Escape)
            Cancel();
    }

    protected override void OnDeactivate(EventArgs e)
    {
        base.OnDeactivate(e);
        if (!_finished && !_selecting)
            Cancel(); // user switched to another window
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == NativeMethods.WM_DPICHANGED)
        {
            // This window always covers every screen, so don't let Windows resize it.
            m.Result = IntPtr.Zero;
            return;
        }
        base.WndProc(ref m);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _font?.Dispose();
        base.Dispose(disposing);
    }

    private void Finish()
    {
        if (_finished)
            return;
        _finished = true;
        Hide(); // off the screen straight away, so it isn't in the captured picture
        Close();
    }

    private static Rectangle FromPoints(Point a, Point b) => Rectangle.FromLTRB(
        Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.X, b.X), Math.Max(a.Y, b.Y));
}
