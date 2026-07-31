using System;
using System.Windows.Controls;
using VcfEditor.ViewModels;

namespace VcfEditor.Views;

public partial class DashboardView : UserControl
{
    public DashboardView(DashboardViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        InitializeComponent();
        DataContext = viewModel;
    }
}
