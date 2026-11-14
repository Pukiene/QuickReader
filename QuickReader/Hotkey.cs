namespace QuickReader;

/// <summary>A keyboard shortcut such as Ctrl+Alt+R.</summary>
internal readonly record struct Hotkey(Keys Key, bool Ctrl, bool Alt, bool Shift, bool Win)
{
    public static readonly Hotkey None = default;

    public bool IsEmpty => Key == Keys.None;

    public bool HasModifier => Ctrl || Alt || Shift || Win;

    /// <summary>Modifier flags in the form RegisterHotKey expects.</summary>
    public uint Modifiers =>
        (Ctrl ? NativeMethods.MOD_CONTROL : 0) |
        (Alt ? NativeMethods.MOD_ALT : 0) |
        (Shift ? NativeMethods.MOD_SHIFT : 0) |
        (Win ? NativeMethods.MOD_WIN : 0);

    public static bool IsFunctionKey(Keys key) => key is >= Keys.F1 and <= Keys.F24;

    public static bool IsModifierKey(Keys key) =>
        key is Keys.ControlKey or Keys.LControlKey or Keys.RControlKey
            or Keys.ShiftKey or Keys.LShiftKey or Keys.RShiftKey
            or Keys.Menu or Keys.LMenu or Keys.RMenu
            or Keys.LWin or Keys.RWin;

    public override string ToString()
    {
        if (IsEmpty)
            return "None";

        var parts = new List<string>(5);
        if (Ctrl) parts.Add("Ctrl");
        if (Alt) parts.Add("Alt");
        if (Shift) parts.Add("Shift");
        if (Win) parts.Add("Win");
        parts.Add(KeyName(Key));
        return string.Join("+", parts);
    }

    /// <summary>Reads text like "Ctrl+Alt+R". Returns <see cref="None"/> if it isn't a usable shortcut.</summary>
    public static Hotkey Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return None;

        bool ctrl = false, alt = false, shift = false, win = false;
        Keys key = Keys.None;
        foreach (string raw in text.Split('+'))
        {
            string part = raw.Trim();
            switch (part.ToLowerInvariant())
            {
                case "": break;
                case "ctrl": case "control": ctrl = true; break;
                case "alt": alt = true; break;
                case "shift": shift = true; break;
                case "win": case "windows": win = true; break;
                default: key = ParseKey(part); break;
            }
        }

        if (key == Keys.None || IsModifierKey(key))
            return None;
        return new Hotkey(key, ctrl, alt, shift, win);
    }

    private static string KeyName(Keys key) => key switch
    {
        >= Keys.D0 and <= Keys.D9 => ((char)('0' + (key - Keys.D0))).ToString(),
        >= Keys.NumPad0 and <= Keys.NumPad9 => "Num" + (key - Keys.NumPad0),
        Keys.Next => "PageDown",
        Keys.Prior => "PageUp",
        Keys.Return => "Enter",
        Keys.Back => "Backspace",
        Keys.Oemcomma => ",",
        Keys.OemPeriod => ".",
        Keys.OemMinus => "-",
        Keys.Oemplus => "=",
        _ => key.ToString(),
    };

    private static Keys ParseKey(string text)
    {
        if (text.Length == 1)
        {
            char c = char.ToUpperInvariant(text[0]);
            if (c is >= 'A' and <= 'Z') return (Keys)c;
            if (c is >= '0' and <= '9') return Keys.D0 + (c - '0');
            return c switch
            {
                ',' => Keys.Oemcomma,
                '.' => Keys.OemPeriod,
                '-' => Keys.OemMinus,
                '=' => Keys.Oemplus,
                _ => Keys.None,
            };
        }

        if (text.Length == 4 && text.StartsWith("Num", StringComparison.OrdinalIgnoreCase) && char.IsDigit(text[3]))
            return Keys.NumPad0 + (text[3] - '0');

        switch (text.ToLowerInvariant())
        {
            case "pagedown": return Keys.Next;
            case "pageup": return Keys.Prior;
            case "enter": return Keys.Return;
            case "backspace": return Keys.Back;
            case "esc": return Keys.Escape;
        }

        // Plain numbers would be read as raw key codes, so don't accept them here.
        if (int.TryParse(text, out _))
            return Keys.None;
        return Enum.TryParse(text, ignoreCase: true, out Keys key) ? key & Keys.KeyCode : Keys.None;
    }
}
