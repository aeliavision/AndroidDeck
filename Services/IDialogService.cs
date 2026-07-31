using System.Collections.Generic;
using VcfEditor.Core;
using VcfEditor.Models;

namespace VcfEditor.Services;

public sealed record PhoneConnectionResult(
    PhoneContactsClient ContactsClient,
    PhoneApiClient ApiClient);

public enum ConflictChoice
{
    Replace,
    KeepBoth,
    Skip
}

public sealed record GalleryMetadataResult(bool? Favorite, string? Description);

public interface IDialogService
{
    void ShowInformation(string message, string title);
    void ShowWarning(string message, string title);
    void ShowError(string message, string title);
    bool Confirm(string message, string title);

    PhoneConnectionResult? ShowConnectPhoneDialog();

    string? ShowOpenVcfDialog();
    string? ShowSaveVcfDialog(string? currentPath = null);
    Contact? ShowCreateContactDialog(ContactSource source);
    bool ShowEditContactDialog(Contact contact);
    PhoneNumber? ShowCreatePhoneNumberDialog();
    bool ShowEditPhoneNumberDialog(PhoneNumber phoneNumber);

    string? ShowDownloadFolderDialog();
    string[] ShowUploadFilesDialog();
    ConflictChoice ShowConflictDialog(string title, string message);
    string? ShowNewFolderDialog();
    string? ShowRenameDialog(string currentName);
    string? ShowMoveDialog(string defaultDestinationPath);
    GalleryMetadataResult? ShowGalleryMetadataDialog(
        string title,
        bool? favoriteInitial = null,
        string? descriptionInitial = null);

    string? ShowSaveBackupArchiveDialog();
    string? ShowOpenBackupArchiveDialog();
    string? ShowEncryptBackupPasswordDialog();
    string? ShowDecryptBackupPasswordDialog();

    bool ShowBackupSummaryDialog(string title, List<(string Label, string Value)> rows);
    bool ShowRestorePreviewDialog(
        string title,
        string message,
        List<(string Label, string Value)> rows,
        string primaryActionText = "Start restore");
    void ShowBackupCompletionDialog(
        string title,
        string message,
        List<(string Label, string Value)> rows);
}
