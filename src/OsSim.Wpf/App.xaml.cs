using System.Windows;
using OsSim.Application;
using OsSim.Wpf.ViewModels;

namespace OsSim.Wpf;

public partial class App : System.Windows.Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var catalog = new SimulationCatalog();
        var window = new MainWindow(new MainWindowViewModel(catalog));
        MainWindow = window;
        window.Show();
    }
}
