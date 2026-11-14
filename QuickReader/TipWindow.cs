using System.ComponentModel;

namespace QuickReader;

/// <summary>
/// A small dark note shown under the toolbar (button hints and messages).
/// It never takes the keyboard focus away from the program you are reading from.
/// </summary>
[DesignerCategory("Code")]
internal sealed class TipWindow : Form
{
    private const TextFormatFlags Flags = TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;

    private readonly System.Windows.Forms.Timer _timer = new();
    private string _text = string.Empty;
    private Font? _font;
    private int _dpi;
    private int _padX, _padY;

    public TipWindow()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.None;
        BackColor = Color.FromArgb(45, 45, 48);
        ForeColor = Color.FromArgb(240, 240, 240);
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
        _timer.Tick += (_, _) => HideTip();
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            CreateParams cp = base.CreateParams;
            cp.ExStyle |= NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_TOPMOST;
            return cp;
        }
    }

    /// <summary>Shows <paramref name="text"/> just below (or above) <paramref name="anchor"/>, in screen pixels.</summary>
    public void ShowTip(string text, Rectangle anchor, int dpi, int milliseconds)
    {
        if (string.IsNullOrEmpty(text))
        {
            HideTip();
            return;
        }

        if (_font is null || dpi != _dpi)
        {
            _font?.Dispose();
            _dpi = dpi;
            _font = new Font("Segoe UI", 12f * dpi / 96f, GraphicsUnit.Pixel);
            _padX = Scale(10);
            _padY = Scale(6);
        }

        _text = text;
        Rectangle work = Screen.FromRectangle(anchor).WorkingArea;
        int maxTextWidth = Math.Min(Scale(380), work.Width - 2 * _padX);
        Size textSize = TextRenderer.MeasureText(text, _font, new Size(maxTextWidth, int.MaxValue), Flags);
        var size = new Size(textSize.Width + 2 * _padX, textSize.Height + 2 * _padY);

        int gap = Scale(6);
        int x = anchor.Left + (anchor.Width - size.Width) / 2;
        x = Math.Max(work.Left, Math.Min(x, work.Right - size.Width));
        int y = anchor.Bottom + gap;
        if (y + size.Height > work.Bottom)
            y = anchor.Top - gap - size.Height;

        Bounds = new Rectangle(new Point(x, y), size);
        if (!Visible)
            Show();
        Invalidate();

        _timer.Stop();
        if (milliseconds > 0)
        {
            _timer.Interval = milliseconds;
            _timer.Start();
        }
    }

    public void HideTip()
    {
        _timer.Stop();
        if (Visible)
            Hide();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        NativeMethods.SetRoundedCorners(Handle, small: true);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(BackColor);
        using (var border = new Pen(Color.FromArgb(85, 85, 90)))
            e.Graphics.DrawRectangle(border, 0, 0, Width - 1, Height - 1);
        if (_font is null)
            return;
        var textArea = new Rectangle(_padX, _padY, Width - 2 * _padX, Height - 2 * _padY);
        TextRenderer.DrawText(e.Graphics, _text, _font, textArea, ForeColor, Flags);
    }

    protected override void OnMouseDown(MouseEventArgs e) => HideTip();

    protected override void WndProc(ref Message m)
    {
        switch (m.Msg)
        {
            case NativeMethods.WM_MOUSEACTIVATE:
                m.Result = (IntPtr)NativeMethods.MA_NOACTIVATE;
                return;
            case NativeMethods.WM_DPICHANGED:
                // The size is worked out in ShowTip for the toolbar's DPI, so don't let Windows rescale it.
                m.Result = IntPtr.Zero;
                return;
        }
        base.WndProc(ref m);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _timer.Dispose();
            _font?.Dispose();
        }
        base.Dispose(disposing);
    }

    private int Scale(int value) => (int)Math.Round(value * _dpi / 96.0);
}
