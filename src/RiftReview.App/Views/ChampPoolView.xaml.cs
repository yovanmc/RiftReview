using System.Windows.Controls;
using RiftReview.App.ViewModels;

namespace RiftReview.App.Views;

public partial class ChampPoolView : UserControl
{
    public ChampPoolView(ChampPoolViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
        Loaded += async (_, _) => await vm.InitializeAsync();
    }
}
