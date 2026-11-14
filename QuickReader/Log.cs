namespace QuickReader;

/// <summary>Writes problems to %AppData%\QuickReader\error.log so they can be looked at later.</summary>
internal static class Log
{
    private const long MaxSize = 256 * 1024;
    private static readonly object Gate = new();

    public static void Error(Exception ex) => Write(ex.ToString());

    public static void Write(string message)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(AppSettings.Folder);
                string path = Path.Combine(AppSettings.Folder, "error.log");
                var file = new FileInfo(path);
                if (file.Exists && file.Length > MaxSize)
                    file.Delete();
                File.AppendAllText(path, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // Logging must never crash the app.
        }
    }
}
