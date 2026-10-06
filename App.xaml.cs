using System.Windows;

namespace CodeSearch;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += (_, ev) =>
        {
            MessageBox.Show(ev.Exception.ToString(), "CodeSearch hata", MessageBoxButton.OK, MessageBoxImage.Error);
            ev.Handled = true;
        };
        base.OnStartup(e);
    }
}
