using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using VcfEditor.Features.Gallery;
using VcfEditor.ViewModels;

namespace VcfEditor.Views;

public partial class GalleryView : UserControl, IDisposable
{
    private readonly GalleryViewModel _viewModel;
    private readonly IGalleryInteraction _interaction;
    private readonly GalleryViewPresentation _presentation;

    public GalleryView(GalleryViewModel viewModel, IGalleryInteraction interaction)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(interaction);
        _viewModel = viewModel;
        _interaction = interaction;
        InitializeComponent();
        _presentation = new GalleryViewPresentation(this, viewModel);
        DataContext = viewModel;
        Loaded += OnLoaded;
        IsVisibleChanged += OnIsVisibleChanged;
        SizeChanged += (_, _) => _presentation.UpdateResponsiveSidebar();
        Unloaded += (_, _) => _presentation.ResetZoom();
    }

    public IGalleryInteraction Actions => _interaction;

    private async void OnLoaded(object sender, RoutedEventArgs e)
        => await _presentation.RefreshVisibleThumbnailsAsync();

    private async void OnIsVisibleChanged(
        object sender,
        DependencyPropertyChangedEventArgs e)
    {
        if (IsVisible)
            await _presentation.RefreshVisibleThumbnailsAsync();
    }

    private void ItemCheckbox_Click(object sender, RoutedEventArgs e) => e.Handled = true;

    private async void MediaItem_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is Border { Tag: GalleryMediaItem item })
            await _interaction.OpenPreviewAsync(item);
    }

    private async void MediaGrid_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (e.VerticalChange != 0 || e.HorizontalChange != 0 || e.ExtentHeightChange != 0)
            await _presentation.RefreshVisibleThumbnailsAsync();
    }

    private async void MediaGrid_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        await _interaction.OpenPreviewAsync(MediaGrid.SelectedItem as GalleryMediaItem);
        e.Handled = true;
    }

    private void Preview_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) != ModifierKeys.Control) return;
        _presentation.ZoomBy(e.Delta > 0 ? 0.1 : -0.1);
        e.Handled = true;
    }

    private void ZoomIn_Click(object sender, RoutedEventArgs e) => _presentation.ZoomBy(0.25);
    private void ZoomOut_Click(object sender, RoutedEventArgs e) => _presentation.ZoomBy(-0.25);
    private void ZoomReset_Click(object sender, RoutedEventArgs e) => _presentation.ResetZoom();

    private async void Preview_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
            _interaction.ClosePreviewCommand.Execute(null);
        else if (e.Key == Key.Left)
            await _interaction.PreviousCommand.ExecuteAsync(null);
        else if (e.Key == Key.Right)
            await _interaction.NextCommand.ExecuteAsync(null);
        else
            return;
        e.Handled = true;
    }

    public void Dispose()
    {
        _presentation.Dispose();
        GC.SuppressFinalize(this);
    }
}
