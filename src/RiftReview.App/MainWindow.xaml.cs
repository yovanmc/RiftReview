using System.Windows;
using RiftReview.App.ViewModels;
using Wpf.Ui.Controls;

namespace RiftReview.App;

public partial class MainWindow : FluentWindow
{
    public MainWindow(MainViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
        Loaded += async (_, _) => await vm.InitializeAsync();
    }
}
