using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using QuickLook.Enhancements;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Startup += async (_, _) =>
        {
            try
            {
                SaveNotification.Started("test-0.png");
                var notice = app.Windows.OfType<Window>().Single();
                notice.Opacity = 0; // Exercise real WPF lifecycle without distracting the user.
                for (var i = 1; i < 11; i++) SaveNotification.Started("test-" + i + ".png");
                for (var i = 0; i < 11; i++) SaveNotification.Finished("test-" + i + ".png", false);
                Check(app.Windows.Count == 1 && notice.IsVisible, "Rapid saves must share one visible notification");
                await Task.Delay(1000);
                SaveNotification.Started("last.png"); SaveNotification.Finished("last.png", false);
                await Task.Delay(1100);
                Check(notice.IsVisible, "Latest update must reset dismissal time");
                await Task.Delay(1000);
                Check(!notice.IsVisible, "No queued notifications after final dismissal");
                Check(app.Windows.Count == 1, "Window must be reused, not queued");
                Console.WriteLine("PASS: 12 saves, one reused window, reset deadline, no delayed queue");
                app.Shutdown(0);
            }
            catch (Exception e) { Console.Error.WriteLine(e); app.Shutdown(1); }
        };
        Environment.ExitCode = app.Run();
    }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
