using System;
using System.Collections.Generic;
using VcfEditor.Core;
using VcfEditor.Models;

namespace VcfEditor.Services;

/// <summary>
/// Safe, non-interactive compatibility service used only by obsolete constructors
/// retained for legacy tests and migration tooling. Production code must inject
/// <see cref="IDialogService"/> through the Generic Host.
/// </summary>
internal sealed class NonInteractiveDialogService : IDialogService
{
    public static NonInteractiveDialogService Instance { get; } = new();

    private NonInteractiveDialogService()
    {
    }

    public void ShowInformation(string message, string title) { }
    public void ShowWarning(string message, string title) { }
    public void ShowError(string message, string title) { }
    public bool Confirm(string message, string title) => false;
    public PhoneConnectionResult? ShowConnectPhoneDialog() => null;
    public string? ShowOpenVcfDialog() => null;
    public string? ShowSaveVcfDialog(string? currentPath = null) => null;
    public Contact? ShowCreateContactDialog(ContactSource source) => null;
    public bool ShowEditContactDialog(Contact contact) => false;
    public PhoneNumber? ShowCreatePhoneNumberDialog() => null;
    public bool ShowEditPhoneNumberDialog(PhoneNumber phoneNumber) => false;
    public string? ShowDownloadFolderDialog() => null;
    public string[] ShowUploadFilesDialog() => Array.Empty<string>();
    public ConflictChoice ShowConflictDialog(string title, string message) => ConflictChoice.Skip;
    public string? ShowNewFolderDialog() => null;
    public string? ShowRenameDialog(string currentName) => null;
    public string? ShowMoveDialog(string defaultDestinationPath) => null;
    public GalleryMetadataResult? ShowGalleryMetadataDialog(
        string title,
        bool? favoriteInitial = null,
        string? descriptionInitial = null) => null;
    public string? ShowSaveBackupArchiveDialog() => null;
    public string? ShowOpenBackupArchiveDialog() => null;
    public string? ShowEncryptBackupPasswordDialog() => null;
    public string? ShowDecryptBackupPasswordDialog() => null;
    public bool ShowBackupSummaryDialog(string title, List<(string Label, string Value)> rows) => false;
    public bool ShowRestorePreviewDialog(
        string title,
        string message,
        List<(string Label, string Value)> rows,
        string primaryActionText = "Start restore") => false;
    public void ShowBackupCompletionDialog(
        string title,
        string message,
        List<(string Label, string Value)> rows) { }
}
