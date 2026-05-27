using SmwController.ViewModels;
using System.Windows;

namespace SmwController;

public partial class MainWindow : Window
{
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
