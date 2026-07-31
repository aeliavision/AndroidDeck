using System;
using System.ComponentModel;
using System.Windows;
using VcfEditor.ViewModels;
using VcfEditor.Views;

namespace VcfEditor.Features.Files;

public sealed class FileBrowserPresentation : IDisposable
{
    private const double PreviewThreshold = 900;
    private readonly FileBrowserView _view;
    private readonly FileBrowserViewModel _viewModel;
    private GridLength? _savedPreviewWidth;
    private bool _narrow;

    public FileBrowserPresentation(FileBrowserView view, FileBrowserViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(viewModel);
        _view = view;
        _viewModel = viewModel;
        viewModel.PropertyChanged += OnPropertyChanged;
    }

    public void UpdateResponsivePreview()
    {
        var narrow = _view.ActualWidth > 0 && _view.ActualWidth < PreviewThreshold;
        if (narrow != _narrow)
        {
            _narrow = narrow;
            if (narrow)
            {
                _savedPreviewWidth ??= _view.PreviewColumn.Width;
                _view.PreviewColumn.Width = new GridLength(0);
                _view.PreviewPanel.Visibility = Visibility.Collapsed;
                _view.PreviewFlyout.Visibility = Visibility.Collapsed;
            }
            else
            {
                _view.PreviewColumn.Width = _savedPreviewWidth ?? new GridLength(280);
                _view.PreviewPanel.Visibility = Visibility.Visible;
                _view.PreviewFlyout.Visibility = Visibility.Collapsed;
            }
        }
        _view.PreviewFlyoutButton.Visibility = _narrow && _viewModel.SelectedItem is not null
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    public void ShowFlyout()
    {
        if (_viewModel.SelectedItem is not null)
            _view.PreviewFlyout.Visibility = Visibility.Visible;
    }

    public void HideFlyout() => _view.PreviewFlyout.Visibility = Visibility.Collapsed;

    private void OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FileBrowserViewModel.SelectedItem))
            _view.Dispatcher.BeginInvoke(UpdateResponsivePreview);
    }

    public void Dispose()
    {
        _viewModel.PropertyChanged -= OnPropertyChanged;
        GC.SuppressFinalize(this);
    }
}
