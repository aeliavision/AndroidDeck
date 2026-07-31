using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using VcfEditor.ViewModels;
using VcfEditor.Views;

namespace VcfEditor.Features.Gallery;

public sealed class GalleryViewPresentation : IDisposable
{
    private readonly GalleryView _view;
    private readonly GalleryViewModel _viewModel;
    private CancellationTokenSource? _visibleThumbnailCts;
    private double _zoom = 1;

    public GalleryViewPresentation(GalleryView view, GalleryViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(viewModel);
        _view = view;
        _viewModel = viewModel;
        viewModel.PropertyChanged += OnPropertyChanged;
    }

    public void UpdateResponsiveSidebar()
    {
        var width = _view.ActualWidth;
        if (double.IsNaN(width) || width <= 0) return;
        var expanded = width >= 1200;
        _view.AlbumColumn.Width = expanded ? new GridLength(220) : new GridLength(0);
        _view.AlbumSidebar.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
        _view.AlbumsComboBox.Visibility = expanded ? Visibility.Collapsed : Visibility.Visible;
        UpdatePreviewMode();
    }

    private void UpdatePreviewMode()
    {
        var expanded = _view.ActualWidth >= 1200;
        var hasPreview = _viewModel.HasPreview;
        _view.PreviewColumn.Width = expanded && hasPreview ? new GridLength(360) : new GridLength(0);
        _view.PreviewPane.Visibility = expanded && hasPreview ? Visibility.Visible : Visibility.Collapsed;
        _view.PreviewDrawer.Visibility = !expanded && hasPreview ? Visibility.Visible : Visibility.Collapsed;
        if (_view.PreviewDrawer.Visibility == Visibility.Visible)
            _view.PreviewDrawer.Focus();
    }

    public async Task RefreshVisibleThumbnailsAsync()
    {
        _visibleThumbnailCts?.Cancel();
        _visibleThumbnailCts?.Dispose();
        _visibleThumbnailCts = new CancellationTokenSource();
        var token = _visibleThumbnailCts.Token;

        try
        {
            await _view.Dispatcher.InvokeAsync(() =>
            {
                UpdateResponsiveSidebar();
                _view.MediaGrid.UpdateLayout();
            }, DispatcherPriority.Loaded, token);

            var visibleItems = GetVisibleMediaItems();
            if (visibleItems.Length == 0)
                visibleItems = _viewModel.MediaItems.Take(24).ToArray();

            await _viewModel.LoadThumbnailsForItemsAsync(visibleItems, token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
    }

    private GalleryMediaItem[] GetVisibleMediaItems()
        => _view.MediaGrid.Items
            .Cast<GalleryMediaItem>()
            .Where(item => _view.MediaGrid.ItemContainerGenerator.ContainerFromItem(item)
                is FrameworkElement { IsVisible: true })
            .ToArray();

    public void ZoomBy(double delta)
    {
        _zoom = Math.Clamp(_zoom + delta, 0.5, 5.0);
        ApplyZoom();
    }

    public void ResetZoom()
    {
        _zoom = 1;
        ApplyZoom();
    }

    private void ApplyZoom()
    {
        _view.PreviewScale.ScaleX = _zoom;
        _view.PreviewScale.ScaleY = _zoom;
        var mode = _zoom > 1 ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled;
        _view.PreviewScrollViewer.HorizontalScrollBarVisibility = mode;
        _view.PreviewScrollViewer.VerticalScrollBarVisibility = mode;
    }

    private void OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(GalleryViewModel.PreviewItem) or nameof(GalleryViewModel.HasPreview))
        {
            _ = _view.Dispatcher.BeginInvoke(new Action(() =>
            {
                ResetZoom();
                UpdatePreviewMode();
            }));
        }
        else if (e.PropertyName is nameof(GalleryViewModel.HasMedia))
        {
            _ = _view.Dispatcher.BeginInvoke(
                new Action(() => _ = RefreshVisibleThumbnailsAsync()),
                DispatcherPriority.Loaded);
        }
    }

    public void Dispose()
    {
        _viewModel.PropertyChanged -= OnPropertyChanged;
        _visibleThumbnailCts?.Cancel();
        _visibleThumbnailCts?.Dispose();
        _visibleThumbnailCts = null;
        GC.SuppressFinalize(this);
    }
}
