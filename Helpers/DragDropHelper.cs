using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Extensions.Logging;
using VcfEditor.Services;

namespace VcfEditor.Helpers;

public sealed class DragDropHelper
{
    private readonly Func<string, Task> _onFileDropped;
    private readonly ILogger<DragDropHelper> _logger;
    private readonly IDialogService _dialogService;

    public DragDropHelper(
        Func<string, Task> onFileDropped,
        ILogger<DragDropHelper>? logger = null)
        : this(onFileDropped, NonInteractiveDialogService.Instance, logger)
    {
    }

    public DragDropHelper(
        Func<string, Task> onFileDropped,
        IDialogService dialogService,
        ILogger<DragDropHelper>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(onFileDropped);
        ArgumentNullException.ThrowIfNull(dialogService);
        _onFileDropped = onFileDropped;
        _dialogService = dialogService;
        _logger = logger ?? AppLoggerFactory.CreateLogger<DragDropHelper>();
    }

    public void EnableDragDrop(UIElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.AllowDrop = true;
        element.PreviewDragOver += OnDragOver;
        element.Drop += OnDrop;
    }

    public void DisableDragDrop(UIElement element)
    {
        if (element is null)
            return;

        element.AllowDrop = false;
        element.PreviewDragOver -= OnDragOver;
        element.Drop -= OnDrop;
    }

    private static bool TryGetSingleVcfFile(
        DragEventArgs e,
        [NotNullWhen(true)] out string? vcfPath)
    {
        vcfPath = null;
        if (!e.Data.GetDataPresent(DataFormats.FileDrop))
            return false;

        var files = e.Data.GetData(DataFormats.FileDrop) as string[];
        if (files is null || files.Length == 0)
            return false;

        var vcfFiles = files
            .Where(file => Path.GetExtension(file).Equals(
                ".vcf",
                StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (vcfFiles.Count != 1)
            return false;

        vcfPath = vcfFiles[0];
        return true;
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = TryGetSingleVcfFile(e, out _)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private async void OnDrop(object sender, DragEventArgs e)
    {
        if (!TryGetSingleVcfFile(e, out var filePath))
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                var files = e.Data.GetData(DataFormats.FileDrop) as string[];
                if (files is { Length: > 0 })
                {
                    var vcfCount = files.Count(file => Path.GetExtension(file).Equals(
                        ".vcf",
                        StringComparison.OrdinalIgnoreCase));
                    ShowDropError(vcfCount == 0
                        ? "Please drop a valid .vcf file."
                        : "Please drop only one VCF file at a time.");
                }
            }

            e.Handled = true;
            return;
        }

        try
        {
            LogMessages.VcfFileDropped(_logger, filePath);
            await _onFileDropped(filePath);
        }
        catch (Exception ex)
        {
            LogMessages.DroppedFileProcessingFailed(_logger, ex);
            ShowDropError("Error processing dropped file. Please try again.");
        }

        e.Handled = true;
    }

    private void ShowDropError(string message)
        => _dialogService.ShowWarning(message, "Invalid File");
}
