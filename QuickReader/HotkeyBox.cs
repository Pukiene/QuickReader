using System.ComponentModel;

namespace QuickReader;

/// <summary>A box that records a shortcut: click it and press the keys (e.g. Ctrl+Alt+R).</summary>
[DesignerCategory("Code")]
internal sealed class HotkeyBox : TextBox
{
    private Hotkey _value;

    public HotkeyBox()
    {
        ReadOnly = true;
        ShortcutsEnabled = false;
        BackColor = SystemColors.Window;
        Cursor = Cursors.Arrow;
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Hotkey Value
    {
        get => _value;
        set
        {
            _value = value;
            Text = value.ToString();
        }
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        Keys key = keyData & Keys.KeyCode;
        Keys modifiers = keyData & Keys.Modifiers;
        bool win = NativeMethods.IsKeyDown(NativeMethods.VK_LWIN) || NativeMethods.IsKeyDown(NativeMethods.VK_RWIN);

        // Plain Tab, Esc and Enter keep their normal meaning in the window.
        if (modifiers == Keys.None && !win && key is Keys.Tab or Keys.Escape or Keys.Return)
            return base.ProcessCmdKey(ref msg, keyData);

        if (modifiers == Keys.None && !win && key is Keys.Back or Keys.Delete)
        {
            Value = Hotkey.None;
            return true;
        }

        bool ctrl = (modifiers & Keys.Control) != 0;
        bool alt = (modifiers & Keys.Alt) != 0;
        bool shift = (modifiers & Keys.Shift) != 0;

        if (Hotkey.IsModifierKey(key))
        {
            Text = Preview(ctrl, alt, shift, win);
            return true;
        }

        var hotkey = new Hotkey(key, ctrl, alt, shift, win);
        if (!hotkey.HasModifier && !Hotkey.IsFunctionKey(key))
        {
            Text = "Add Ctrl or Alt";
            return true;
        }

        Value = hotkey;
        return true;
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);
        if (!e.Control && !e.Alt && !e.Shift)
            Text = _value.ToString();
    }

    protected override void OnLeave(EventArgs e)
    {
        base.OnLeave(e);
        Text = _value.ToString();
    }

    private static string Preview(bool ctrl, bool alt, bool shift, bool win)
    {
        var parts = new List<string>(4);
        if (ctrl) parts.Add("Ctrl");
        if (alt) parts.Add("Alt");
        if (shift) parts.Add("Shift");
        if (win) parts.Add("Win");
        return string.Join("+", parts) + "+…";
    }
}
