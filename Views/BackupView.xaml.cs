using System;
using System.Windows;
using System.Windows.Controls;
using VcfEditor.ViewModels;

namespace VcfEditor.Views;

public partial class BackupView : UserControl
{
    private readonly BackupViewModel _viewModel;

    public BackupView(BackupViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
        => await _viewModel.InitializeAsync();
}
