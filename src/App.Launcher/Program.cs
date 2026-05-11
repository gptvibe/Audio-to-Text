using System.Diagnostics;
using System.Runtime.InteropServices;

internal static class Program
{
    public static int Main()
    {
        var baseDirectory = AppContext.BaseDirectory;
        var appExecutable = Path.Combine(baseDirectory, "App", "QuietScribe.exe");

        if (!File.Exists(appExecutable))
        {
            ShowMessage(
                "QuietScribe could not find its app files.\n\nKeep QuietScribe.exe next to the App folder from the portable zip.",
                "QuietScribe");
            return 1;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = appExecutable,
                WorkingDirectory = Path.GetDirectoryName(appExecutable)!,
                UseShellExecute = false
            });
            return 0;
        }
        catch (Exception ex)
        {
            ShowMessage($"QuietScribe could not start.\n\n{ex.Message}", "QuietScribe");
            return 1;
        }
    }

    private static void ShowMessage(string message, string title)
    {
        MessageBoxW(IntPtr.Zero, message, title, 0x00000010);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int MessageBoxW(IntPtr owner, string text, string caption, uint type);
}
