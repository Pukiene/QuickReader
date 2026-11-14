using System.ComponentModel;
using System.Drawing.Drawing2D;
using System.Globalization;

namespace QuickReader;

/// <summary>
/// The small floating toolbar. It is drawn by hand (no child controls) and never takes the
/// keyboard focus, so the text you selected in another program stays selected when you click it.
/// </summary>
[DesignerCategory("Code")]
internal sealed class ToolbarForm : Form
{
    private enum Item { None, Grip, Read, Snip, Pause, Stop, Slower, Speed, Faster, Settings, Close }

    private readonly record struct Slot(Item Item, Rectangle Bounds);

    // Dark theme colours.
    private static readonly Color BackColour = Color.FromArgb(32, 32, 32);
    private static readonly Color BorderColour = Color.FromArgb(70, 70, 70);
    private static readonly Color SeparatorColour = Color.FromArgb(62, 62, 62);
    private static readonly Color HoverColour = Color.FromArgb(55, 55, 55);
    private static readonly Color PressedColour = Color.FromArgb(75, 75, 75);
    private static readonly Color AccentColour = Color.FromArgb(0, 103, 192);
    private static readonly Color AccentHoverColour = Color.FromArgb(25, 125, 215);
    private static readonly Color IconColour = Color.FromArgb(242, 242, 242);
    private static readonly Color DisabledColour = Color.FromArgb(105, 105, 105);
    private static readonly Color GripColour = Color.FromArgb(120, 120, 120);

    // Symbols from the Windows icon font (Segoe MDL2 Assets / Segoe Fluent Icons).
    private const string GlyphRead = "";     // speaker
    private const string GlyphSnip = "";     // scissors
    private const string GlyphPause = "";
    private const string GlyphPlay = "";
    private const string GlyphStop = "";
    private const string GlyphSlower = "";   // minus
    private const string GlyphFaster = "";   // plus
    private const string GlyphSettings = ""; // gear
    private const string GlyphClose = "";

    private const int HotkeyRead = 1, HotkeySnip = 2, HotkeyPause = 3, HotkeyStop = 4;
    private const double MinSpeed = 0.5, MaxSpeed = 3.0, SpeedStep = 0.1;

    private static readonly string IconFontName = PickIconFont();

    private readonly AppSettings _settings;
    private readonly SpeechEngine _speech;
    private readonly TipWindow _hint = new();
    private readonly TipWindow _message = new();
    private readonly System.Windows.Forms.Timer _hintTimer = new() { Interval = 600 };
    private readonly List<Slot> _slots = new();
    private readonly List<int> _separators = new();

    private Font? _iconFont, _textFont;
    private Item _hover, _pressed;
    private Item _busy;       // Read or Snip while copying text / recognising a snip
    private Item _lastSource; // which button started the current reading
    private bool _dragging, _closing;
    private Point _dragCursorStart, _dragFormStart;
    private SnipForm? _snip;
    private SettingsForm? _settingsForm;

    public ToolbarForm()
    {
        _settings = AppSettings.Load();
        _settings.Speed = ClampSpeed(_settings.Speed);

        _speech = new SpeechEngine { Speed = _settings.Speed };
        _speech.SetVoice(_settings.VoiceId);
        _speech.StateChanged += (_, _) => Invalidate();
        _speech.Error += message => ShowMessage(message, 6000);

        Text = "Quick Reader";
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.None;
        BackColor = BackColour;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);

        if (_settings.ToolbarX is int x && _settings.ToolbarY is int y)
            Location = new Point(x, y);

        _hintTimer.Tick += (_, _) =>
        {
            _hintTimer.Stop();
            ShowHint();
        };
    }

    // ---------------------------------------------------------------- window setup

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

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        NativeMethods.SetRoundedCorners(Handle, small: false);
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        UpdateLayoutForDpi();
        if (_settings.ToolbarX is null || _settings.ToolbarY is null)
        {
            Rectangle work = SystemInformation.WorkingArea; // main screen
            Location = new Point(work.Left + (work.Width - Width) / 2, work.Top + ScaleValue(12));
        }
        KeepOnScreen();
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        RegisterHotkeys(showErrors: true);
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        UpdateLayoutForDpi(); // moved to a screen with a different scale
    }

    protected override void WndProc(ref Message m)
    {
        switch (m.Msg)
        {
            case NativeMethods.WM_MOUSEACTIVATE:
                m.Result = (IntPtr)NativeMethods.MA_NOACTIVATE; // clicking doesn't steal the focus
                return;
            case NativeMethods.WM_HOTKEY:
                OnHotkey((int)m.WParam);
                return;
        }
        base.WndProc(ref m);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _closing = true;
        _snip?.Cancel();
        _settingsForm?.Close();
        UnregisterHotkeys();
        SavePosition();
        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _speech.Dispose();
            _hint.Dispose();
            _message.Dispose();
            _hintTimer.Dispose();
            _iconFont?.Dispose();
            _textFont?.Dispose();
        }
        base.Dispose(disposing);
    }

    // ---------------------------------------------------------------- layout and drawing

    private int ScaleValue(int value) => (int)Math.Round(value * DeviceDpi / 96.0);

    private void UpdateLayoutForDpi()
    {
        _iconFont?.Dispose();
        _textFont?.Dispose();
        _iconFont = new Font(IconFontName, ScaleValue(15), FontStyle.Regular, GraphicsUnit.Pixel);
        _textFont = new Font("Segoe UI", ScaleValue(12), FontStyle.Regular, GraphicsUnit.Pixel);

        int pad = ScaleValue(3);
        int height = ScaleValue(38);
        int button = ScaleValue(34);
        int small = ScaleValue(26);
        int gap = ScaleValue(9);
        int inner = height - 2 * pad;

        _slots.Clear();
        _separators.Clear();
        int x = pad;

        void Add(Item item, int width)
        {
            _slots.Add(new Slot(item, new Rectangle(x, pad, width, inner)));
            x += width;
        }

        void Separator()
        {
            _separators.Add(x + gap / 2);
            x += gap;
        }

        Add(Item.Grip, ScaleValue(16));
        Add(Item.Read, button);
        Add(Item.Snip, button);
        Separator();
        Add(Item.Pause, button);
        Add(Item.Stop, button);
        Separator();
        Add(Item.Slower, small);
        Add(Item.Speed, ScaleValue(40));
        Add(Item.Faster, small);
        Separator();
        Add(Item.Settings, button);
        Add(Item.Close, button);

        ClientSize = new Size(x + pad, height);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.Clear(BackColour);

        using (var border = new Pen(BorderColour))
            g.DrawRectangle(border, 0, 0, Width - 1, Height - 1);

        using (var separator = new Pen(SeparatorColour))
        {
            int top = ScaleValue(10), bottom = Height - ScaleValue(10);
            foreach (int sx in _separators)
                g.DrawLine(separator, sx, top, sx, bottom);
        }

        foreach (Slot slot in _slots)
            DrawSlot(g, slot);
    }

    private void DrawSlot(Graphics g, Slot slot)
    {
        Item item = slot.Item;
        if (item == Item.Grip)
        {
            DrawGrip(g, slot.Bounds);
            return;
        }

        bool enabled = IsEnabled(item);
        bool hovered = enabled && item == _hover;
        Color? fill = null;
        if (hovered && item == _pressed)
            fill = PressedColour;
        else if (IsActive(item))
            fill = hovered ? AccentHoverColour : AccentColour;
        else if (hovered)
            fill = HoverColour;

        if (fill is Color colour)
            FillRounded(g, Rectangle.Inflate(slot.Bounds, -1, 0), colour);

        string text = item == Item.Speed
            ? _settings.Speed.ToString("0.0", CultureInfo.InvariantCulture) + "×"
            : GlyphFor(item);
        Font font = item == Item.Speed ? _textFont! : _iconFont!;
        TextRenderer.DrawText(g, text, font, slot.Bounds, enabled ? IconColour : DisabledColour,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine |
            TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
    }

    private void DrawGrip(Graphics g, Rectangle bounds)
    {
        int dot = Math.Max(2, ScaleValue(2));
        int step = ScaleValue(5);
        int left = bounds.Left + (bounds.Width - (step + dot)) / 2;
        int top = bounds.Top + (bounds.Height - (2 * step + dot)) / 2;
        using var brush = new SolidBrush(GripColour);
        for (int column = 0; column < 2; column++)
            for (int row = 0; row < 3; row++)
                g.FillRectangle(brush, left + column * step, top + row * step, dot, dot);
    }

    private void FillRounded(Graphics g, Rectangle r, Color colour)
    {
        int d = ScaleValue(10);
        using var path = new GraphicsPath();
        path.AddArc(r.Left, r.Top, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Top, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();

        SmoothingMode oldMode = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var brush = new SolidBrush(colour))
            g.FillPath(brush, path);
        g.SmoothingMode = oldMode;
    }

    private string GlyphFor(Item item) => item switch
    {
        Item.Read => GlyphRead,
        Item.Snip => GlyphSnip,
        Item.Pause => _speech.State == SpeechState.Paused ? GlyphPlay : GlyphPause,
        Item.Stop => GlyphStop,
        Item.Slower => GlyphSlower,
        Item.Faster => GlyphFaster,
        Item.Settings => GlyphSettings,
        Item.Close => GlyphClose,
        _ => string.Empty,
    };

    private bool IsEnabled(Item item) => item switch
    {
        Item.Read or Item.Snip => _busy == Item.None || _busy == item,
        Item.Pause or Item.Stop => _speech.State != SpeechState.Idle,
        _ => true,
    };

    /// <summary>Buttons shown in blue: the one that is working right now.</summary>
    private bool IsActive(Item item) => item switch
    {
        Item.Read or Item.Snip => _busy == item || (_busy == Item.None && _speech.State != SpeechState.Idle && _lastSource == item),
        Item.Pause => _speech.State == SpeechState.Paused,
        _ => false,
    };

    private string HintFor(Item item) => item switch
    {
        Item.Grip => "Drag to move",
        Item.Read => "Read the selected text" + ShortcutText(_settings.ReadHotkey),
        Item.Snip => "Snip an area of the screen and read it" + ShortcutText(_settings.SnipHotkey),
        Item.Pause => (_speech.State == SpeechState.Paused ? "Resume" : "Pause") + ShortcutText(_settings.PauseHotkey),
        Item.Stop => "Stop" + ShortcutText(_settings.StopHotkey),
        Item.Slower => "Slower",
        Item.Faster => "Faster",
        Item.Speed => "Reading speed (click to reset to 1.0×)",
        Item.Settings => "Voice and shortcuts",
        Item.Close => "Close Quick Reader",
        _ => string.Empty,
    };

    private static string ShortcutText(string hotkey)
    {
        Hotkey parsed = Hotkey.Parse(hotkey);
        return parsed.IsEmpty ? string.Empty : $"   {parsed}";
    }

    private static string PickIconFont()
    {
        foreach (string name in new[] { "Segoe Fluent Icons", "Segoe MDL2 Assets" })
        {
            try
            {
                using var family = new FontFamily(name);
                return name;
            }
            catch (ArgumentException)
            {
                // not installed
            }
        }
        return "Segoe UI Symbol";
    }

    // ---------------------------------------------------------------- mouse

    private Item HitTest(Point point)
    {
        foreach (Slot slot in _slots)
            if (slot.Bounds.Contains(point))
                return slot.Item;
        return Item.None;
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left)
            return;

        _hintTimer.Stop();
        _hint.HideTip();
        Item item = HitTest(e.Location);
        Capture = true;
        if (item is Item.None or Item.Grip)
        {
            _dragging = true;
            _dragCursorStart = Cursor.Position;
            _dragFormStart = Location;
            return;
        }
        _pressed = item;
        Invalidate();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_dragging)
        {
            Point now = Cursor.Position;
            Location = new Point(_dragFormStart.X + now.X - _dragCursorStart.X, _dragFormStart.Y + now.Y - _dragCursorStart.Y);
            return;
        }

        Item item = HitTest(e.Location);
        if (item == _hover)
            return;
        _hover = item;
        Cursor = item is Item.Grip or Item.None ? Cursors.SizeAll : Cursors.Default;
        Invalidate();

        _hint.HideTip();
        _hintTimer.Stop();
        if (item != Item.None)
            _hintTimer.Start();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button != MouseButtons.Left)
            return;
        Capture = false;

        if (_dragging)
        {
            _dragging = false;
            KeepOnScreen();
            SavePosition();
            return;
        }

        Item item = _pressed;
        _pressed = Item.None;
        Invalidate();
        if (item != Item.None && item == HitTest(e.Location) && IsEnabled(item))
            OnButtonClick(item);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hover = Item.None;
        _hintTimer.Stop();
        _hint.HideTip();
        Invalidate();
    }

    private void ShowHint()
    {
        if (_hover == Item.None || _dragging || _pressed != Item.None || _message.Visible)
            return;
        foreach (Slot slot in _slots)
        {
            if (slot.Item == _hover)
            {
                _hint.ShowTip(HintFor(slot.Item), RectangleToScreen(slot.Bounds), DeviceDpi, 4000);
                return;
            }
        }
    }

    private void ShowMessage(string text, int milliseconds = 4000)
    {
        _hint.HideTip();
        _message.ShowTip(text, Bounds, DeviceDpi, milliseconds);
    }

    // ---------------------------------------------------------------- actions

    private void OnButtonClick(Item item)
    {
        switch (item)
        {
            case Item.Read: _ = ReadSelectionAsync(); break;
            case Item.Snip: StartSnip(); break;
            case Item.Pause: _speech.TogglePause(); break;
            case Item.Stop: _speech.Stop(); break;
            case Item.Slower: SetSpeed(_settings.Speed - SpeedStep); break;
            case Item.Faster: SetSpeed(_settings.Speed + SpeedStep); break;
            case Item.Speed: SetSpeed(1.0); break;
            case Item.Settings: OpenSettings(); break;
            case Item.Close: Close(); break;
        }
    }

    private void OnHotkey(int id)
    {
        switch (id)
        {
            case HotkeyRead: _ = ReadSelectionAsync(); break;
            case HotkeySnip: StartSnip(); break;
            case HotkeyPause: _speech.TogglePause(); break;
            case HotkeyStop:
                _snip?.Cancel();
                _speech.Stop();
                break;
        }
    }

    private async Task ReadSelectionAsync()
    {
        if (_busy != Item.None)
            return;
        SetBusy(Item.Read);
        try
        {
            string text = await SelectionReader.GetSelectedTextAsync();
            if (string.IsNullOrWhiteSpace(text))
            {
                ShowMessage("No text selected. Select some text first, then press Read.");
            }
            else
            {
                _lastSource = Item.Read;
                _speech.Speak(text);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex);
            ShowMessage("Sorry, the selected text couldn't be copied.");
        }
        finally
        {
            SetBusy(Item.None);
        }
    }

    private void StartSnip()
    {
        if (_busy != Item.None || _snip is not null)
            return;

        SetBusy(Item.Snip);
        _hintTimer.Stop();
        _hint.HideTip();
        _message.HideTip();
        Hide(); // keep the toolbar out of the picture

        try
        {
            var snip = new SnipForm();
            _snip = snip;
            snip.FormClosed += (_, _) =>
            {
                // Run after the overlay has fully closed.
                if (!_closing && IsHandleCreated)
                    BeginInvoke(new Action(() => _ = FinishSnipAsync(snip.SelectedArea)));
                else
                    _snip = null;
            };
            snip.Show();
        }
        catch (Exception ex)
        {
            Log.Error(ex);
            _snip = null;
            ShowToolbar();
            SetBusy(Item.None);
            ShowMessage("Sorry, snipping couldn't start.");
        }
    }

    private async Task FinishSnipAsync(Rectangle area)
    {
        _snip = null;
        try
        {
            if (_closing || area.IsEmpty)
                return;

            // Make sure the dark overlay has really gone from the screen before taking the picture.
            NativeMethods.WaitForScreenRefresh();
            await Task.Delay(40);
            NativeMethods.WaitForScreenRefresh();

            string text;
            using (Bitmap picture = SnipForm.CaptureScreen(area))
            {
                ShowToolbar();
                text = await OcrService.RecognizeAsync(picture);
            }

            if (string.IsNullOrWhiteSpace(text))
            {
                ShowMessage("No text found in that area.");
            }
            else
            {
                _lastSource = Item.Snip;
                _speech.Speak(text);
            }
        }
        catch (OcrUnavailableException ex)
        {
            ShowToolbar();
            ShowMessage(ex.Message, 12000);
        }
        catch (Exception ex)
        {
            Log.Error(ex);
            ShowToolbar();
            ShowMessage("Sorry, the text in that area couldn't be read.");
        }
        finally
        {
            ShowToolbar();
            SetBusy(Item.None);
        }
    }

    private void ShowToolbar()
    {
        if (!_closing && !Visible)
            Show();
    }

    private void SetBusy(Item item)
    {
        _busy = item;
        Invalidate();
    }

    private static double ClampSpeed(double value) =>
        Math.Round(Math.Clamp(double.IsFinite(value) ? value : 1.0, MinSpeed, MaxSpeed), 1);

    private void SetSpeed(double value)
    {
        _settings.Speed = ClampSpeed(value);
        _speech.Speed = _settings.Speed;
        _settings.Save();
        Invalidate();
    }

    private void OpenSettings()
    {
        if (_settingsForm is not null)
        {
            _settingsForm.Activate();
            return;
        }

        UnregisterHotkeys(); // so the shortcut boxes can record them
        var form = new SettingsForm(_settings, _speech);
        _settingsForm = form;
        form.FormClosed += (_, _) =>
        {
            _settingsForm = null;
            if (_closing)
                return;
            if (form.DialogResult == DialogResult.OK)
                _settings.Save();
            _speech.SetVoice(_settings.VoiceId); // undo a voice that was only tested
            RegisterHotkeys(showErrors: true);
            Invalidate();
        };
        form.Show();
        form.Activate();
    }

    // ---------------------------------------------------------------- hotkeys and position

    private void RegisterHotkeys(bool showErrors)
    {
        UnregisterHotkeys();
        var failed = new List<string>();
        Register(HotkeyRead, _settings.ReadHotkey, failed);
        Register(HotkeySnip, _settings.SnipHotkey, failed);
        Register(HotkeyPause, _settings.PauseHotkey, failed);
        Register(HotkeyStop, _settings.StopHotkey, failed);

        if (showErrors && failed.Count > 0)
            ShowMessage($"Another program is already using {string.Join(", ", failed)}. " +
                        "Choose a different shortcut in Settings (the gear button).", 9000);
    }

    private void Register(int id, string text, List<string> failed)
    {
        Hotkey hotkey = Hotkey.Parse(text);
        if (hotkey.IsEmpty)
            return;
        if (!NativeMethods.RegisterHotKey(Handle, id, hotkey.Modifiers | NativeMethods.MOD_NOREPEAT, (uint)hotkey.Key))
            failed.Add(hotkey.ToString());
    }

    private void UnregisterHotkeys()
    {
        if (!IsHandleCreated)
            return;
        for (int id = HotkeyRead; id <= HotkeyStop; id++)
            NativeMethods.UnregisterHotKey(Handle, id);
    }

    private void KeepOnScreen()
    {
        Rectangle work = Screen.FromPoint(new Point(Left + Width / 2, Top + Height / 2)).WorkingArea;
        int x = Math.Max(work.Left, Math.Min(Left, work.Right - Width));
        int y = Math.Max(work.Top, Math.Min(Top, work.Bottom - Height));
        Location = new Point(x, y);
    }

    private void SavePosition()
    {
        _settings.ToolbarX = Left;
        _settings.ToolbarY = Top;
        _settings.Save();
    }
}
