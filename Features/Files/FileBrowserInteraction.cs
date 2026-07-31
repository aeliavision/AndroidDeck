using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using VcfEditor.Models.DTOs;
using VcfEditor.Services;
using VcfEditor.ViewModels;

namespace VcfEditor.Features.Files;

public interface IFileBrowserInteraction
{
    IAsyncRelayCommand RefreshCommand { get; }
    IAsyncRelayCommand BackCommand { get; }
    IAsyncRelayCommand UploadCommand { get; }
    IAsyncRelayCommand DownloadCommand { get; }
    IAsyncRelayCommand DeleteCommand { get; }
    IAsyncRelayCommand CreateFolderCommand { get; }
    IAsyncRelayCommand RenameCommand { get; }
    IAsyncRelayCommand MoveCommand { get; }
    IRelayCommand CancelTransferCommand { get; }
    IAsyncRelayCommand RetryTransferCommand { get; }

    void UpdateSelection(IReadOnlyList<FileEntryDto> entries);
    Task RefreshAsync();
    Task NavigateUpAsync();
    Task NavigateToAsync(string path);
    Task OpenEntryAsync(FileEntryDto? entry);
    Task DownloadAsync(IReadOnlyList<FileEntryDto> entries);
    Task UploadAsync();
    Task UploadAsync(string[] paths);
    Task DeleteAsync(IReadOnlyList<FileEntryDto> entries);
    Task CreateFolderAsync();
    Task RenameAsync(IReadOnlyList<FileEntryDto> entries);
    Task MoveAsync(IReadOnlyList<FileEntryDto> entries);
    void CancelTransfer();
}

public sealed class FileBrowserInteraction : IFileBrowserInteraction, IDisposable
{
    private readonly FileBrowserViewModel _viewModel;
    private readonly IDialogService _dialogService;
    private FileEntryDto[] _selectedEntries = Array.Empty<FileEntryDto>();
    private bool _disposed;

    public FileBrowserInteraction(FileBrowserViewModel viewModel, IDialogService dialogService)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(dialogService);
        _viewModel = viewModel;
        _dialogService = dialogService;

        RefreshCommand = new AsyncRelayCommand(RefreshAsync, CanRun);
        BackCommand = new AsyncRelayCommand(NavigateUpAsync, CanRun);
        UploadCommand = new AsyncRelayCommand(UploadAsync, CanRun);
        DownloadCommand = new AsyncRelayCommand(
            () => DownloadAsync(_selectedEntries),
            CanDownload);
        DeleteCommand = new AsyncRelayCommand(
            () => DeleteAsync(_selectedEntries),
            CanUseSelection);
        CreateFolderCommand = new AsyncRelayCommand(CreateFolderAsync, CanRun);
        RenameCommand = new AsyncRelayCommand(
            () => RenameAsync(_selectedEntries),
            CanRename);
        MoveCommand = new AsyncRelayCommand(
            () => MoveAsync(_selectedEntries),
            CanUseSelection);
        CancelTransferCommand = new RelayCommand(CancelTransfer, CanCancelTransfer);
        RetryTransferCommand = new AsyncRelayCommand(_viewModel.RetryLastTransferAsync, CanRetryTransfer);

        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    public IAsyncRelayCommand RefreshCommand { get; }
    public IAsyncRelayCommand BackCommand { get; }
    public IAsyncRelayCommand UploadCommand { get; }
    public IAsyncRelayCommand DownloadCommand { get; }
    public IAsyncRelayCommand DeleteCommand { get; }
    public IAsyncRelayCommand CreateFolderCommand { get; }
    public IAsyncRelayCommand RenameCommand { get; }
    public IAsyncRelayCommand MoveCommand { get; }
    public IRelayCommand CancelTransferCommand { get; }
    public IAsyncRelayCommand RetryTransferCommand { get; }

    public void UpdateSelection(IReadOnlyList<FileEntryDto> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        _selectedEntries = entries.ToArray();
        NotifyCanExecuteChanged();
    }

    public Task RefreshAsync() => _viewModel.RefreshAsync();
    public Task NavigateUpAsync() => _viewModel.NavigateUpAsync();
    public Task NavigateToAsync(string path) => _viewModel.NavigateToAsync(path);
    public Task OpenEntryAsync(FileEntryDto? entry)
        => entry?.IsDirectory == true && !string.IsNullOrWhiteSpace(entry.Path)
            ? _viewModel.NavigateToAsync(entry.Path)
            : Task.CompletedTask;

    public async Task DownloadAsync(IReadOnlyList<FileEntryDto> entries)
    {
        var files = entries.Where(entry => !entry.IsDirectory).ToList();
        if (files.Count == 0)
        {
            _dialogService.ShowInformation("Select one or more files to download.", "No Selection");
            return;
        }

        var folder = _dialogService.ShowDownloadFolderDialog();
        if (string.IsNullOrWhiteSpace(folder)) return;
        foreach (var file in files)
            await _viewModel.DownloadAsync(file, folder);
    }

    public Task UploadAsync() => UploadAsync(_dialogService.ShowUploadFilesDialog());

    public Task UploadAsync(string[] paths)
    {
        if (paths.Length == 0) return Task.CompletedTask;
        return _viewModel.UploadFilesWithConflictsAsync(
            paths,
            (name, target) => Task.FromResult(MapConflict(
                _dialogService.ShowConflictDialog(
                    "File exists",
                    $"'{name}' already exists in the destination.\n\nTarget: {target}\n\nChoose what to do:"))));
    }

    public async Task DeleteAsync(IReadOnlyList<FileEntryDto> entries)
    {
        if (entries.Count == 0) return;
        var names = string.Join("\n", entries.Select(entry => entry.Name));
        if (!_dialogService.Confirm($"Delete the following from the phone?\n\n{names}", "Confirm Delete"))
            return;
        foreach (var entry in entries)
            await _viewModel.DeleteAsync(entry, recursive: entry.IsDirectory);
    }

    public async Task CreateFolderAsync()
    {
        var name = _dialogService.ShowNewFolderDialog();
        if (!string.IsNullOrWhiteSpace(name))
            await _viewModel.MkdirAsync(name);
    }

    public async Task RenameAsync(IReadOnlyList<FileEntryDto> entries)
    {
        if (entries.Count == 0) return;
        if (entries.Count > 1)
        {
            _dialogService.ShowInformation(
                "Rename supports only one selected item.",
                "Rename");
            return;
        }

        var entry = entries[0];
        var name = _dialogService.ShowRenameDialog(entry.Name ?? string.Empty);
        if (!string.IsNullOrWhiteSpace(name))
            await _viewModel.RenameAsync(entry, name);
    }

    public async Task MoveAsync(IReadOnlyList<FileEntryDto> entries)
    {
        if (entries.Count == 0) return;
        if (entries.Count == 1)
        {
            var entry = entries[0];
            var suggested = _viewModel.CurrentPath.TrimEnd('/') + "/" + (entry.Name ?? "destination");
            var path = _dialogService.ShowMoveDialog(suggested);
            if (!string.IsNullOrWhiteSpace(path))
                await _viewModel.MoveAsync(entry, path);
            return;
        }

        var destination = _dialogService.ShowMoveDialog(_viewModel.CurrentPath.TrimEnd('/'));
        if (string.IsNullOrWhiteSpace(destination)) return;
        await _viewModel.MoveManyAsync(
            entries,
            destination,
            (entry, target) => Task.FromResult(MapConflict(
                _dialogService.ShowConflictDialog(
                    "File exists",
                    $"'{entry.Name}' already exists in the destination.\n\nTarget: {target}\n\nChoose what to do:"))));
    }

    public void CancelTransfer() => _viewModel.CancelTransfer();

    private bool CanRun() => _viewModel.IsIdle && !_viewModel.IsTransferring;
    private bool CanUseSelection() => CanRun() && _selectedEntries.Length > 0;
    private bool CanDownload()
        => CanRun() && _selectedEntries.Any(entry => !entry.IsDirectory);
    private bool CanRename() => CanRun() && _selectedEntries.Length == 1;
    private bool CanCancelTransfer() => _viewModel.IsTransferring;
    private bool CanRetryTransfer() => _viewModel.CanRetryTransfer;

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(FileBrowserViewModel.IsBusy)
            or nameof(FileBrowserViewModel.IsIdle)
            or nameof(FileBrowserViewModel.IsTransferring))
        {
            NotifyCanExecuteChanged();
        }
    }

    private void NotifyCanExecuteChanged()
    {
        RefreshCommand.NotifyCanExecuteChanged();
        BackCommand.NotifyCanExecuteChanged();
        UploadCommand.NotifyCanExecuteChanged();
        DownloadCommand.NotifyCanExecuteChanged();
        DeleteCommand.NotifyCanExecuteChanged();
        CreateFolderCommand.NotifyCanExecuteChanged();
        RenameCommand.NotifyCanExecuteChanged();
        MoveCommand.NotifyCanExecuteChanged();
        CancelTransferCommand.NotifyCanExecuteChanged();
        RetryTransferCommand.NotifyCanExecuteChanged();
    }

    private static FileBrowserViewModel.ConflictResolution MapConflict(ConflictChoice choice)
        => choice switch
        {
            ConflictChoice.Replace => FileBrowserViewModel.ConflictResolution.Replace,
            ConflictChoice.KeepBoth => FileBrowserViewModel.ConflictResolution.KeepBoth,
            _ => FileBrowserViewModel.ConflictResolution.Skip
        };

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        GC.SuppressFinalize(this);
    }
}
