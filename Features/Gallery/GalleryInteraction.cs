using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using VcfEditor.Services;
using VcfEditor.ViewModels;

namespace VcfEditor.Features.Gallery;

public interface IGalleryInteraction
{
    IAsyncRelayCommand RefreshCommand { get; }
    IRelayCommand SelectAllCommand { get; }
    IRelayCommand ClearSelectionCommand { get; }
    IRelayCommand ClosePreviewCommand { get; }
    IAsyncRelayCommand PreviousCommand { get; }
    IAsyncRelayCommand NextCommand { get; }
    IAsyncRelayCommand DownloadCommand { get; }
    IAsyncRelayCommand DeleteCommand { get; }
    IAsyncRelayCommand RenameCommand { get; }
    IAsyncRelayCommand MoveCommand { get; }
    IAsyncRelayCommand EditMetadataCommand { get; }
    IRelayCommand CancelTransferCommand { get; }
    IAsyncRelayCommand LoadMoreCommand { get; }

    Task RefreshAsync();
    void ToggleSelectAll();
    void ClearSelection();
    Task OpenPreviewAsync(GalleryMediaItem? item);
    void ClosePreview();
    Task OpenPreviousAsync();
    Task OpenNextAsync();
    Task DownloadAsync(IReadOnlyList<GalleryMediaItem> selected);
    Task DeleteAsync(IReadOnlyList<GalleryMediaItem> selected);
    Task RenameAsync(IReadOnlyList<GalleryMediaItem> selected);
    Task MoveAsync(IReadOnlyList<GalleryMediaItem> selected);
    Task EditMetadataAsync(IReadOnlyList<GalleryMediaItem> selected);
    void CancelTransfer();
}

public sealed class GalleryInteraction : IGalleryInteraction, IDisposable
{
    private readonly GalleryViewModel _viewModel;
    private readonly IDialogService _dialogService;
    private bool _disposed;

    public GalleryInteraction(GalleryViewModel viewModel, IDialogService dialogService)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(dialogService);
        _viewModel = viewModel;
        _dialogService = dialogService;

        RefreshCommand = new AsyncRelayCommand(RefreshAsync, CanRun);
        SelectAllCommand = new RelayCommand(ToggleSelectAll, CanRun);
        ClearSelectionCommand = new RelayCommand(ClearSelection, CanUseSelection);
        ClosePreviewCommand = new RelayCommand(ClosePreview, CanClosePreview);
        PreviousCommand = new AsyncRelayCommand(OpenPreviousAsync, CanOpenPrevious);
        NextCommand = new AsyncRelayCommand(OpenNextAsync, CanOpenNext);
        DownloadCommand = new AsyncRelayCommand(
            () => DownloadAsync(SelectedItems()),
            CanUseSelection);
        DeleteCommand = new AsyncRelayCommand(
            () => DeleteAsync(SelectedItems()),
            CanUseSelection);
        RenameCommand = new AsyncRelayCommand(
            () => RenameAsync(SelectedItems()),
            CanUseSingleSelection);
        MoveCommand = new AsyncRelayCommand(
            () => MoveAsync(SelectedItems()),
            CanUseSelection);
        EditMetadataCommand = new AsyncRelayCommand(
            () => EditMetadataAsync(SelectedItems()),
            CanUseSingleSelection);
        CancelTransferCommand = new RelayCommand(CancelTransfer, CanCancelTransfer);
        LoadMoreCommand = new AsyncRelayCommand(_viewModel.LoadMoreAsync, CanLoadMore);

        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    public IAsyncRelayCommand RefreshCommand { get; }
    public IRelayCommand SelectAllCommand { get; }
    public IRelayCommand ClearSelectionCommand { get; }
    public IRelayCommand ClosePreviewCommand { get; }
    public IAsyncRelayCommand PreviousCommand { get; }
    public IAsyncRelayCommand NextCommand { get; }
    public IAsyncRelayCommand DownloadCommand { get; }
    public IAsyncRelayCommand DeleteCommand { get; }
    public IAsyncRelayCommand RenameCommand { get; }
    public IAsyncRelayCommand MoveCommand { get; }
    public IAsyncRelayCommand EditMetadataCommand { get; }
    public IRelayCommand CancelTransferCommand { get; }
    public IAsyncRelayCommand LoadMoreCommand { get; }

    public Task RefreshAsync() => _viewModel.RefreshAsync();

    public void ToggleSelectAll()
    {
        var select = !_viewModel.MediaItems.All(item => item.IsSelected);
        foreach (var item in _viewModel.MediaItems)
            item.IsSelected = select;
    }

    public void ClearSelection()
    {
        foreach (var item in _viewModel.MediaItems)
            item.IsSelected = false;
    }

    public Task OpenPreviewAsync(GalleryMediaItem? item)
        => item is null ? Task.CompletedTask : _viewModel.OpenPreviewAsync(item);

    public void ClosePreview() => _viewModel.ClosePreview();

    public Task OpenPreviousAsync() => OpenRelativeAsync(-1);
    public Task OpenNextAsync() => OpenRelativeAsync(1);

    public async Task DownloadAsync(IReadOnlyList<GalleryMediaItem> selected)
    {
        if (selected.Count == 0)
        {
            _dialogService.ShowInformation("Select one or more items to download.", "No Selection");
            return;
        }
        var folder = _dialogService.ShowDownloadFolderDialog();
        if (!string.IsNullOrWhiteSpace(folder))
            await _viewModel.DownloadSelectedAsync(selected, folder);
    }

    public async Task DeleteAsync(IReadOnlyList<GalleryMediaItem> selected)
    {
        if (selected.Count == 0) return;
        if (_dialogService.Confirm(
                $"Delete {selected.Count} item(s) from the phone gallery?",
                "Confirm Delete"))
        {
            await _viewModel.DeleteSelectedAsync(selected);
        }
    }

    public async Task RenameAsync(IReadOnlyList<GalleryMediaItem> selected)
    {
        if (!RequireSingle(selected, "Rename")) return;
        var item = selected[0];
        var name = _dialogService.ShowRenameDialog(item.Media.Name ?? string.Empty);
        if (!string.IsNullOrWhiteSpace(name))
            await _viewModel.RenameAsync(item, name);
    }

    public async Task MoveAsync(IReadOnlyList<GalleryMediaItem> selected)
    {
        if (selected.Count == 0) return;
        var path = _dialogService.ShowMoveDialog("Pictures/");
        if (!string.IsNullOrWhiteSpace(path))
            await _viewModel.MoveSelectedAsync(selected, path);
    }

    public async Task EditMetadataAsync(IReadOnlyList<GalleryMediaItem> selected)
    {
        if (!RequireSingle(selected, "Metadata")) return;
        var result = _dialogService.ShowGalleryMetadataDialog("Edit Metadata");
        if (result is not null)
            await _viewModel.UpdateMetadataAsync(selected[0], result.Favorite, result.Description);
    }

    public void CancelTransfer() => _viewModel.CancelTransfer();

    private GalleryMediaItem[] SelectedItems()
        => _viewModel.MediaItems.Where(item => item.IsSelected).ToArray();

    private bool CanRun() => !_viewModel.IsBusy && !_viewModel.IsTransferring;
    private bool CanUseSelection() => CanRun() && _viewModel.SelectedCount > 0;
    private bool CanUseSingleSelection() => CanRun() && _viewModel.SelectedCount == 1;
    private bool CanClosePreview() => _viewModel.HasPreview;
    private bool CanCancelTransfer() => _viewModel.IsTransferring;
    private bool CanLoadMore() => !_viewModel.IsBusy && _viewModel.HasMoreItems;

    private bool CanOpenPrevious()
    {
        var current = _viewModel.PreviewItem;
        return current is not null && _viewModel.MediaItems.IndexOf(current) > 0;
    }

    private bool CanOpenNext()
    {
        var current = _viewModel.PreviewItem;
        if (current is null) return false;
        var index = _viewModel.MediaItems.IndexOf(current);
        return index >= 0 && index < _viewModel.MediaItems.Count - 1;
    }

    private async Task OpenRelativeAsync(int delta)
    {
        var current = _viewModel.PreviewItem;
        if (current is null) return;
        var index = _viewModel.MediaItems.IndexOf(current) + delta;
        if (index >= 0 && index < _viewModel.MediaItems.Count)
            await _viewModel.OpenPreviewAsync(_viewModel.MediaItems[index]);
    }

    private bool RequireSingle(IReadOnlyList<GalleryMediaItem> selected, string title)
    {
        if (selected.Count == 1) return true;
        if (selected.Count > 1)
            _dialogService.ShowInformation($"{title} supports only one selected item.", title);
        return false;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(GalleryViewModel.IsBusy)
            or nameof(GalleryViewModel.IsTransferring)
            or nameof(GalleryViewModel.SelectedCount)
            or nameof(GalleryViewModel.HasSelection)
            or nameof(GalleryViewModel.PreviewItem)
            or nameof(GalleryViewModel.HasPreview)
            or nameof(GalleryViewModel.MediaItems)
            or nameof(GalleryViewModel.HasMoreItems))
        {
            NotifyCanExecuteChanged();
        }
    }

    private void NotifyCanExecuteChanged()
    {
        RefreshCommand.NotifyCanExecuteChanged();
        SelectAllCommand.NotifyCanExecuteChanged();
        ClearSelectionCommand.NotifyCanExecuteChanged();
        ClosePreviewCommand.NotifyCanExecuteChanged();
        PreviousCommand.NotifyCanExecuteChanged();
        NextCommand.NotifyCanExecuteChanged();
        DownloadCommand.NotifyCanExecuteChanged();
        DeleteCommand.NotifyCanExecuteChanged();
        RenameCommand.NotifyCanExecuteChanged();
        MoveCommand.NotifyCanExecuteChanged();
        EditMetadataCommand.NotifyCanExecuteChanged();
        CancelTransferCommand.NotifyCanExecuteChanged();
        LoadMoreCommand.NotifyCanExecuteChanged();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        GC.SuppressFinalize(this);
    }
}
