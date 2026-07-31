using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using VcfEditor.Features.Files;
using VcfEditor.Models.DTOs;
using VcfEditor.ViewModels;

namespace VcfEditor.Views;

public partial class FileBrowserView : UserControl, IDisposable
{
    private readonly FileBrowserViewModel _viewModel;
    private readonly IFileBrowserInteraction _interaction;
    private readonly FileBrowserPresentation _presentation;

    public FileBrowserView(
        FileBrowserViewModel viewModel,
        IFileBrowserInteraction interaction)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(interaction);
        _viewModel = viewModel;
        _interaction = interaction;
        InitializeComponent();
        _presentation = new FileBrowserPresentation(this, viewModel);
        DataContext = viewModel;
        Loaded += (_, _) => _presentation.UpdateResponsivePreview();
        SizeChanged += (_, _) => _presentation.UpdateResponsivePreview();
        Unloaded += (_, _) => _presentation.HideFlyout();
    }

    public IFileBrowserInteraction Actions => _interaction;

    private List<FileEntryDto> SelectedEntries()
    {
        var list = FileListView.SelectedItems.OfType<FileEntryDto>().ToList();
        return list.Count > 0 ? list : FileGrid.SelectedItems.OfType<FileEntryDto>().ToList();
    }

    private void FileSelectionChanged(object sender, SelectionChangedEventArgs e)
        => _interaction.UpdateSelection(SelectedEntries());

    private async void Breadcrumb_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string path })
            await _interaction.NavigateToAsync(path);
    }

    private async void FileList_DoubleClick(object sender, MouseButtonEventArgs e)
        => await _interaction.OpenEntryAsync(FileListView.SelectedItem as FileEntryDto);

    private async void FileGrid_DoubleClick(object sender, MouseButtonEventArgs e)
        => await _interaction.OpenEntryAsync(FileGrid.SelectedItem as FileEntryDto);

    private async void FileList_KeyDown(object sender, KeyEventArgs e)
        => await HandleNavigationKeyAsync(e, FileListView.SelectedItem as FileEntryDto);

    private async void FileGrid_KeyDown(object sender, KeyEventArgs e)
        => await HandleNavigationKeyAsync(e, FileGrid.SelectedItem as FileEntryDto);

    private async System.Threading.Tasks.Task HandleNavigationKeyAsync(
        KeyEventArgs e,
        FileEntryDto? entry)
    {
        if (e.Key == Key.Enter)
            await _interaction.OpenEntryAsync(entry);
        else if (e.Key == Key.Back)
            await _interaction.NavigateUpAsync();
        else
            return;
        e.Handled = true;
    }


    private void ListViewMode_Click(object sender, RoutedEventArgs e)
        => _viewModel.IsGridView = false;

    private void GridViewMode_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.IsGridView = true;
        Dispatcher.BeginInvoke(() => {
            FileGrid.UpdateLayout();
            if (FileGrid.Items.Count > 0)
            {
                FileGrid.ScrollIntoView(FileGrid.Items[FileGrid.Items.Count - 1]);
                FileGrid.ScrollIntoView(FileGrid.Items[0]);
            }
        }, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void PreviewFlyoutButton_Click(object sender, RoutedEventArgs e)
        => _presentation.ShowFlyout();

    private void ClosePreviewFlyoutButton_Click(object sender, RoutedEventArgs e)
        => _presentation.HideFlyout();

    private void View_DragOver(object sender, DragEventArgs e)
    {
        var valid = _viewModel.IsIdle && e.Data.GetDataPresent(DataFormats.FileDrop);
        e.Effects = valid ? DragDropEffects.Copy : DragDropEffects.None;
        DropHint.Visibility = valid ? Visibility.Visible : Visibility.Collapsed;
        e.Handled = true;
    }

    private async void View_Drop(object sender, DragEventArgs e)
    {
        DropHint.Visibility = Visibility.Collapsed;
        if (e.Data.GetData(DataFormats.FileDrop) is string[] paths)
            await _interaction.UploadAsync(paths);
        e.Handled = true;
    }

    public void Dispose()
    {
        _presentation.Dispose();
        GC.SuppressFinalize(this);
    }
}
