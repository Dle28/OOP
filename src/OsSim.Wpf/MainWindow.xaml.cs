using System.Windows;
using OsSim.Wpf.ViewModels;

namespace OsSim.Wpf;

public partial class MainWindow : Window
{
    public MainWindow(MainWindowViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
