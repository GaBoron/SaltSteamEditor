using System.Windows;

namespace SaltSteamEditor;

public partial class App : Application
{
    private void OnStartup(object sender, StartupEventArgs e)
    {
        var window = new MainWindow();
        MainWindow = window;
        window.Show();
        if (e.Args.Length > 0)
            window.OpenPath(e.Args[0]);
    }
}
