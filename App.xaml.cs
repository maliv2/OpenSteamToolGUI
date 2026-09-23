using System.Windows;
using OpenSteamToolGUI.Core;
using System.Windows.Media.Imaging;

namespace OpenSteamToolGUI;
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (e.Args.Length == 2 && e.Args[0] == "--elevated-apply")
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            ElevatedFileTransaction.Execute(e.Args[1]);
            Shutdown();
            return;
        }
        var window = new MainWindow();
        window.Icon = BitmapFrame.Create(new Uri("pack://application:,,,/Assets/app.ico"));
        window.Show();
    }
}
