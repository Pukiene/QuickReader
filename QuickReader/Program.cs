namespace QuickReader;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => Log.Error(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log.Write("Fatal error: " + e.ExceptionObject);
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Log.Error(e.Exception);
            e.SetObserved();
        };

        // Only one copy of the reader at a time.
        using var singleInstance = new Mutex(true, @"Local\QuickReader.SingleInstance", out bool isFirstInstance);
        if (!isFirstInstance)
        {
            MessageBox.Show("Quick Reader is already running. Look for its toolbar on your screen.",
                "Quick Reader", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        try
        {
            Application.Run(new ToolbarForm());
        }
        catch (Exception ex)
        {
            Log.Error(ex);
            MessageBox.Show("Quick Reader couldn't start:\n\n" + ex.Message,
                "Quick Reader", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
