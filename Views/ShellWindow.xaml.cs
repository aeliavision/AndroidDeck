using System;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using VcfEditor.Services;
using VcfEditor.ViewModels;

namespace VcfEditor.Views;

public partial class ShellWindow : Window, IDisposable
{
    private readonly ShellWindowViewModel _viewModel;
    private readonly IShellConnectionCoordinator _connectionCoordinator;
    private bool _disposed;

    public ShellWindow(
        ShellWindowViewModel viewModel,
        IShellConnectionCoordinator connectionCoordinator)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(connectionCoordinator);

        _viewModel = viewModel;
        _connectionCoordinator = connectionCoordinator;

        InitializeComponent();
        DataContext = viewModel;
        Loaded += OnLoaded;
        SizeChanged += OnSizeChanged;
        Closed += OnClosed;
        viewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        _connectionCoordinator.Start();
        _viewModel.UpdateWindowWidth(ActualWidth);
        await _viewModel.InitializeAsync();
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
        => _viewModel.UpdateWindowWidth(e.NewSize.Width);

    private void OnClosed(object? sender, EventArgs e)
        => Dispose();

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ShellWindowViewModel.IsOverlayOpen)) return;

        _ = Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
        {
            if (_viewModel.IsOverlayOpen)
                OverlaySidebar.FocusSelectedItem();
            else if (_viewModel.IsOverlayMode && OverlayOpenButton.IsVisible)
                OverlayOpenButton.Focus();
            else
                DesktopSidebar.FocusSelectedItem();
        }));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        Loaded -= OnLoaded;
        SizeChanged -= OnSizeChanged;
        Closed -= OnClosed;
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _viewModel.Dispose();
        _connectionCoordinator.Dispose();
        GC.SuppressFinalize(this);
    }
}
