using System.Windows.Controls;
using RiftReview.App.ViewModels;

namespace RiftReview.App.Views;

public partial class MatchupsView : UserControl
{
    public MatchupsView(MatchupsViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
        Loaded += async (_, _) => await vm.InitializeAsync();
    }
}
