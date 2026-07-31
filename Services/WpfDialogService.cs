using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using Microsoft.Win32;
using VcfEditor.Core.Settings;
using VcfEditor.Models;
using VcfEditor.Views;

namespace VcfEditor.Services;

public sealed class WpfDialogService : IDialogService
{
    private readonly IAppSettingsStore _settingsStore;

    public WpfDialogService(IAppSettingsStore settingsStore)
    {
        ArgumentNullException.ThrowIfNull(settingsStore);
        _settingsStore = settingsStore;
    }

    public void ShowInformation(string message, string title)
        => Own(new AppMessageDialog(title, message, AppMessageKind.Information)).ShowDialog();

    public void ShowWarning(string message, string title)
        => Own(new AppMessageDialog(title, message, AppMessageKind.Warning)).ShowDialog();

    public void ShowError(string message, string title)
        => Own(new AppMessageDialog(title, message, AppMessageKind.Error)).ShowDialog();

    public bool Confirm(string message, string title)
        => Own(new AppMessageDialog(title, message, AppMessageKind.Confirmation, showConfirmationActions: true))
            .ShowDialog() == true;

    public PhoneConnectionResult? ShowConnectPhoneDialog()
    {
        var dialog = Own(new ConnectPhoneDialog(_settingsStore, this));
        return dialog.ShowDialog() == true &&
               dialog.PhoneClient is not null &&
               dialog.PhoneApiClient is not null
            ? new PhoneConnectionResult(dialog.PhoneClient, dialog.PhoneApiClient)
            : null;
    }

    public string? ShowOpenVcfDialog()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "VCF files (*.vcf)|*.vcf|All files (*.*)|*.*",
            Title = "Open VCF File"
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public string? ShowSaveVcfDialog(string? currentPath = null)
    {
        var dialog = new SaveFileDialog
        {
            Filter = "VCF files (*.vcf)|*.vcf|All files (*.*)|*.*",
            Title = "Save VCF File",
            DefaultExt = ".vcf",
            FileName = string.IsNullOrWhiteSpace(currentPath) ? string.Empty : Path.GetFileName(currentPath),
            InitialDirectory = string.IsNullOrWhiteSpace(currentPath) ? string.Empty : Path.GetDirectoryName(currentPath) ?? string.Empty
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public Contact? ShowCreateContactDialog(ContactSource source)
    {
        var contact = new Contact { Source = source };
        var dialog = Own(new EditContactDialog(contact, this));
        return dialog.ShowDialog() == true ? contact : null;
    }

    public bool ShowEditContactDialog(Contact contact)
        => Own(new EditContactDialog(contact, this)).ShowDialog() == true;

    public PhoneNumber? ShowCreatePhoneNumberDialog()
    {
        var dialog = Own(new PhoneNumberDialog(this));
        return dialog.ShowDialog() == true ? dialog.PhoneNumber : null;
    }

    public bool ShowEditPhoneNumberDialog(PhoneNumber phoneNumber)
        => Own(new PhoneNumberDialog(phoneNumber, this)).ShowDialog() == true;

    public string? ShowDownloadFolderDialog()
    {
        var dialog = new SaveFileDialog
        {
            Title = "Choose a folder to save the downloaded files",
            FileName = "Select Folder",
            Filter = "Folder|*.none",
            CheckFileExists = false,
            CheckPathExists = true
        };
        return dialog.ShowDialog() == true ? Path.GetDirectoryName(dialog.FileName) : null;
    }

    public string[] ShowUploadFilesDialog()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select files to upload to phone",
            Multiselect = true,
            Filter = "All Files (*.*)|*.*"
        };
        return dialog.ShowDialog() == true ? dialog.FileNames : Array.Empty<string>();
    }

    public ConflictChoice ShowConflictDialog(string title, string message)
        => InvokeOnUi(() =>
        {
            var dialog = Own(new ConflictResolutionDialog(title, message));
            if (dialog.ShowDialog() != true || dialog.Choice is null)
                return ConflictChoice.Skip;

            return dialog.Choice.Value switch
            {
                ConflictResolutionDialog.Result.Replace => ConflictChoice.Replace,
                ConflictResolutionDialog.Result.KeepBoth => ConflictChoice.KeepBoth,
                _ => ConflictChoice.Skip
            };
        });

    public string? ShowNewFolderDialog()
    {
        var dialog = Own(new NewFolderDialog());
        return dialog.ShowDialog() == true ? dialog.FolderName : null;
    }

    public string? ShowRenameDialog(string currentName)
    {
        var dialog = Own(new RenameDialog(currentName));
        return dialog.ShowDialog() == true ? dialog.NewName : null;
    }

    public string? ShowMoveDialog(string defaultDestinationPath)
    {
        var dialog = Own(new MoveDialog(defaultDestinationPath));
        return dialog.ShowDialog() == true ? dialog.DestinationPath : null;
    }

    public GalleryMetadataResult? ShowGalleryMetadataDialog(
        string title,
        bool? favoriteInitial = null,
        string? descriptionInitial = null)
    {
        var dialog = Own(new GalleryMetadataDialog(title, favoriteInitial, descriptionInitial));
        return dialog.ShowDialog() == true
            ? new GalleryMetadataResult(dialog.Favorite, dialog.Description)
            : null;
    }

    public string? ShowSaveBackupArchiveDialog()
    {
        var dialog = new SaveFileDialog
        {
            Title = "Save AndroidDeck Backup Archive",
            Filter = "AndroidDeck Backup (*.deckbak)|*.deckbak|Legacy VCF Backup (*.vcfbak)|*.vcfbak",
            FileName = $"AndroidDeck_Backup_{DateTime.Now:yyyyMMdd_HHmmss}.deckbak",
            DefaultExt = ".deckbak"
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public string? ShowOpenBackupArchiveDialog()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Open AndroidDeck Backup Archive",
            Filter = "AndroidDeck Backup (*.deckbak)|*.deckbak|Legacy VCF Backup (*.vcfbak)|*.vcfbak|All Files (*.*)|*.*"
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public string? ShowEncryptBackupPasswordDialog()
    {
        var dialog = Own(new PasswordDialog("Encrypt Backup", requireConfirm: true, this));
        return dialog.ShowDialog() == true ? dialog.Password : null;
    }

    public string? ShowDecryptBackupPasswordDialog()
    {
        var dialog = Own(new PasswordDialog("Decrypt Backup", requireConfirm: false, this));
        return dialog.ShowDialog() == true ? dialog.Password : null;
    }

    public bool ShowBackupSummaryDialog(string title, List<(string Label, string Value)> rows)
        => Own(new BackupSummaryDialog(title, rows)).ShowDialog() == true;

    public bool ShowRestorePreviewDialog(
        string title,
        string message,
        List<(string Label, string Value)> rows,
        string primaryActionText = "Start restore")
        => Own(new RestorePreviewDialog(title, message, rows, primaryActionText)).ShowDialog() == true;

    public void ShowBackupCompletionDialog(
        string title,
        string message,
        List<(string Label, string Value)> rows)
        => Own(new BackupCompletionDialog(title, message, rows)).ShowDialog();

    private static T InvokeOnUi<T>(Func<T> action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        return dispatcher is null || dispatcher.CheckAccess()
            ? action()
            : dispatcher.Invoke(action);
    }

    private static T Own<T>(T dialog) where T : Window
    {
        dialog.Owner = Application.Current?.MainWindow;
        return dialog;
    }

}
