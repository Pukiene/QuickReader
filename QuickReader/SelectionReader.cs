using System.Diagnostics;
using System.Runtime.InteropServices;

namespace QuickReader;

/// <summary>
/// Gets the text selected in whatever program is in front. It works in almost any program:
/// it presses Ctrl+C for you, reads the copied text, and then puts back whatever was on
/// your clipboard before.
/// </summary>
internal static class SelectionReader
{
    // Clipboard formats that are saved and put back. Only common ones: asking a program
    // for every format it can offer (Excel, for example) can be very slow.
    private static readonly string[] FormatsToKeep =
    {
        DataFormats.UnicodeText, DataFormats.Text, DataFormats.Rtf, DataFormats.Html,
        DataFormats.FileDrop, "FileNameW", "Shell IDList Array", "Preferred DropEffect",
        "PNG", DataFormats.Bitmap,
    };

    private static readonly int[] ModifierKeys =
    {
        NativeMethods.VK_MENU, NativeMethods.VK_SHIFT, NativeMethods.VK_LWIN, NativeMethods.VK_RWIN,
    };

    /// <summary>Returns the selected text, or an empty string if nothing is selected.</summary>
    public static async Task<string> GetSelectedTextAsync()
    {
        await LetGoOfShortcutKeysAsync();

        DataObject? backup = BackupClipboard(out bool clipboardWasEmpty);
        uint before = NativeMethods.GetClipboardSequenceNumber();

        NativeMethods.SendCtrlC();

        // Wait for the program to copy. Nothing changes if nothing is selected.
        var timer = Stopwatch.StartNew();
        while (NativeMethods.GetClipboardSequenceNumber() == before && timer.ElapsedMilliseconds < 1200)
            await Task.Delay(15);

        if (NativeMethods.GetClipboardSequenceNumber() == before)
            return string.Empty;

        await Task.Delay(40); // let the program finish writing to the clipboard
        string text = ReadClipboardText();
        RestoreClipboard(backup, clipboardWasEmpty);
        return text;
    }

    /// <summary>
    /// If the reader was started with a shortcut, Alt/Shift/Win may still be held down,
    /// and Ctrl+C would become e.g. Ctrl+Alt+C. Wait a moment for them to be released.
    /// </summary>
    private static async Task LetGoOfShortcutKeysAsync()
    {
        var timer = Stopwatch.StartNew();
        while (ModifierKeys.Any(NativeMethods.IsKeyDown) && timer.ElapsedMilliseconds < 1000)
            await Task.Delay(15);

        int[] stillDown = ModifierKeys.Where(NativeMethods.IsKeyDown).ToArray();
        if (stillDown.Length > 0)
            NativeMethods.ReleaseKeys(stillDown);
    }

    private static DataObject? BackupClipboard(out bool wasEmpty)
    {
        wasEmpty = false;
        try
        {
            IDataObject? current = Clipboard.GetDataObject();
            string[] available = current?.GetFormats(false) ?? Array.Empty<string>();
            if (current is null || available.Length == 0)
            {
                wasEmpty = true;
                return null;
            }

            var copy = new DataObject();
            foreach (string format in FormatsToKeep)
            {
                if (Array.IndexOf(available, format) < 0)
                    continue;
                try
                {
                    object? value = current.GetData(format, format == DataFormats.Bitmap);
                    if (value is MemoryStream stream && stream.Length > 64L * 1024 * 1024)
                        continue;
                    if (value is not null)
                        copy.SetData(format, false, value);
                }
                catch
                {
                    // This format can't be read: skip it.
                }
            }
            return copy;
        }
        catch (Exception ex)
        {
            Log.Error(ex);
            return null;
        }
    }

    private static string ReadClipboardText()
    {
        for (int attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                return Clipboard.ContainsText() ? Clipboard.GetText() : string.Empty;
            }
            catch (ExternalException)
            {
                Thread.Sleep(50); // another program has the clipboard open
            }
        }
        return string.Empty;
    }

    private static void RestoreClipboard(DataObject? backup, bool wasEmpty)
    {
        try
        {
            if (wasEmpty)
            {
                Clipboard.Clear();
                return;
            }
            if (backup is null || backup.GetFormats().Length == 0)
                return;

            // Keep the restored copy out of Windows clipboard history (Win+V), so it isn't listed twice.
            backup.SetData("CanIncludeInClipboardHistory", false, new MemoryStream(BitConverter.GetBytes(0)));
            backup.SetData("ExcludeClipboardContentFromMonitorProcessing", false, new MemoryStream(BitConverter.GetBytes(0)));
            Clipboard.SetDataObject(backup, true, 5, 100);
        }
        catch (Exception ex)
        {
            Log.Error(ex);
        }
    }
}
