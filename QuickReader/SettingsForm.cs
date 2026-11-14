using System.ComponentModel;
using System.Diagnostics;
using Windows.Media.SpeechSynthesis;

namespace QuickReader;

/// <summary>Voice choice and keyboard shortcuts.</summary>
[DesignerCategory("Code")]
internal sealed class SettingsForm : Form
{
    private readonly AppSettings _settings;
    private readonly SpeechEngine _speech;
    private readonly ComboBox _voice = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 300 };
    private readonly HotkeyBox _readKey = new() { Width = 180 };
    private readonly HotkeyBox _snipKey = new() { Width = 180 };
    private readonly HotkeyBox _pauseKey = new() { Width = 180 };
    private readonly HotkeyBox _stopKey = new() { Width = 180 };

    public SettingsForm(AppSettings settings, SpeechEngine speech)
    {
        _settings = settings;
        _speech = speech;

        Text = "Quick Reader – Settings";
        Font = new Font("Segoe UI", 9f);
        AutoScaleDimensions = new SizeF(96f, 96f);
        AutoScaleMode = AutoScaleMode.Dpi;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        TopMost = true;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(14);
        Icon = AppIcon.Get();

        var table = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 3,
            Location = new Point(Padding.Left, Padding.Top),
        };
        for (int i = 0; i < 3; i++)
            table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var test = new Button { Text = "Test", AutoSize = true };
        test.Click += (_, _) => TestVoice();

        var moreVoices = new LinkLabel { Text = "Get more voices in Windows Settings", AutoSize = true, Margin = new Padding(3, 0, 3, 10) };
        moreVoices.LinkClicked += (_, _) => OpenWindowsSettings("ms-settings:speech");

        var heading = new Label
        {
            Text = "Keyboard shortcuts (work in any program)",
            AutoSize = true,
            Font = new Font(Font, FontStyle.Bold),
            Margin = new Padding(3, 8, 3, 6),
        };
        var help = new Label
        {
            Text = "Click a box, then press the keys you want. Backspace = no shortcut.",
            AutoSize = true,
            ForeColor = SystemColors.GrayText,
            Margin = new Padding(3, 4, 3, 12),
        };

        var ok = new Button { Text = "OK", AutoSize = true, MinimumSize = new Size(80, 0) };
        var cancel = new Button { Text = "Cancel", AutoSize = true, MinimumSize = new Size(80, 0) };
        ok.Click += (_, _) => SaveAndClose();
        cancel.Click += (_, _) => Close();
        AcceptButton = ok;
        CancelButton = cancel;
        var buttons = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            Anchor = AnchorStyles.Right,
            Margin = Padding.Empty,
        };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(ok);

        int row = 0;
        table.Controls.Add(NewLabel("Voice:"), 0, row);
        table.Controls.Add(_voice, 1, row);
        table.Controls.Add(test, 2, row);
        row++;
        table.Controls.Add(moreVoices, 1, row);
        table.SetColumnSpan(moreVoices, 2);
        row++;
        table.Controls.Add(heading, 0, row);
        table.SetColumnSpan(heading, 3);
        row++;
        AddShortcutRow(table, ref row, "Read selected text:", _readKey);
        AddShortcutRow(table, ref row, "Snip and read:", _snipKey);
        AddShortcutRow(table, ref row, "Pause / resume:", _pauseKey);
        AddShortcutRow(table, ref row, "Stop:", _stopKey);
        table.Controls.Add(help, 1, row);
        table.SetColumnSpan(help, 2);
        row++;
        table.Controls.Add(buttons, 0, row);
        table.SetColumnSpan(buttons, 3);
        Controls.Add(table);

        FillVoices();
        _readKey.Value = Hotkey.Parse(settings.ReadHotkey);
        _snipKey.Value = Hotkey.Parse(settings.SnipHotkey);
        _pauseKey.Value = Hotkey.Parse(settings.PauseHotkey);
        _stopKey.Value = Hotkey.Parse(settings.StopHotkey);
    }

    private static Label NewLabel(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Anchor = AnchorStyles.Left,
        UseMnemonic = false,
    };

    private static void AddShortcutRow(TableLayoutPanel table, ref int row, string label, HotkeyBox box)
    {
        table.Controls.Add(NewLabel(label), 0, row);
        table.Controls.Add(box, 1, row);
        box.Anchor = AnchorStyles.Left;
        row++;
    }

    private void FillVoices()
    {
        IReadOnlyList<VoiceInformation> voices;
        try
        {
            voices = SpeechEngine.AllVoices;
        }
        catch (Exception ex)
        {
            Log.Error(ex);
            voices = Array.Empty<VoiceInformation>();
        }

        VoiceItem[] items = voices
            .OrderBy(v => v.Language.StartsWith("en", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(v => v.Language)
            .ThenBy(v => v.DisplayName)
            .Select(v => new VoiceItem(v.Id, v.DisplayName, $"{v.DisplayName}  ({v.Language})"))
            .ToArray();

        _voice.Items.AddRange(items);
        _voice.SelectedItem = items.FirstOrDefault(i => i.Id == _speech.VoiceId) ?? items.FirstOrDefault();
        if (items.Length == 0)
        {
            _voice.Items.Add("No voices found – use the link below to add one");
            _voice.SelectedIndex = 0;
            _voice.Enabled = false;
        }
    }

    private void TestVoice()
    {
        if (_voice.SelectedItem is not VoiceItem item)
            return;
        _speech.SetVoice(item.Id);
        string name = item.Name.Replace("Microsoft ", string.Empty);
        _speech.Speak($"Hello, I'm {name}. This is how I sound at the current speed.");
    }

    private void SaveAndClose()
    {
        Hotkey[] chosen = new[] { _readKey.Value, _snipKey.Value, _pauseKey.Value, _stopKey.Value }
            .Where(k => !k.IsEmpty)
            .ToArray();
        if (chosen.Distinct().Count() != chosen.Length)
        {
            MessageBox.Show(this, "Two actions have the same shortcut. Please change one of them.",
                "Quick Reader", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (_voice.SelectedItem is VoiceItem voice)
            _settings.VoiceId = voice.Id;
        _settings.ReadHotkey = _readKey.Value.ToString();
        _settings.SnipHotkey = _snipKey.Value.ToString();
        _settings.PauseHotkey = _pauseKey.Value.ToString();
        _settings.StopHotkey = _stopKey.Value.ToString();

        DialogResult = DialogResult.OK;
        Close();
    }

    private static void OpenWindowsSettings(string page)
    {
        try
        {
            Process.Start(new ProcessStartInfo(page) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Error(ex);
        }
    }

    private sealed record VoiceItem(string Id, string Name, string Label)
    {
        public override string ToString() => Label;
    }
}

/// <summary>The app's icon, taken from the .exe so it isn't stored twice.</summary>
internal static class AppIcon
{
    private static Icon? _icon;
    private static bool _loaded;

    public static Icon? Get()
    {
        if (!_loaded)
        {
            _loaded = true;
            try { _icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
            catch (Exception ex) { Log.Error(ex); }
        }
        return _icon;
    }
}
