using Avalonia;
using Avalonia.Diagnostics;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace GlossaAI.Desktop;

sealed class Program
{
    private static Mutex? _mutex;

    [STAThread]
    public static void Main(string[] args)
    {
        AppDomain.CurrentDomain.UnhandledException += (_, ex) =>
            WriteCrashLog(ex.ExceptionObject as Exception);

        TaskScheduler.UnobservedTaskException += (_, ex) =>
        {
            WriteCrashLog(ex.Exception);
            ex.SetObserved();
        };

        const string mutexName = @"Global\GlossaAI.Desktop.UniqueMutex";
        _mutex = new Mutex(true, mutexName, out bool createdNew);
        if (!createdNew)
        {
            _mutex.Dispose();
            return;
        }

        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        finally
        {
            try
            {
                _mutex.ReleaseMutex();
            }
            catch (ObjectDisposedException) { }
            catch (ApplicationException) { }
            _mutex.Dispose();
        }
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();

    private static void WriteCrashLog(Exception? ex)
    {
        try
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "GlossaAI");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "crash.log");
            File.AppendAllText(path,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {ex}\n\n");
        }
        catch { }
    }
}
