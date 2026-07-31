using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VcfEditor.Core.Settings;
using VcfEditor.Models;
using VcfEditor.Services;
using VcfEditor.Services.Settings;

namespace VcfEditor.ViewModels;

public partial class SettingsViewModel : ObservableObject, IDisposable
{
    private readonly IAppSettingsStore _store;
    private readonly IThemeService _themeService;
    private readonly IUserNotificationService _notificationService;
    private readonly IDiagnosticExportService _diagnosticExportService;
    private CancellationTokenSource? _feedbackCts;

    public SettingsViewModel(
        IAppSettingsStore store,
        IThemeService themeService,
        IUserNotificationService notificationService,
        IDiagnosticExportService diagnosticExportService)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(themeService);
        ArgumentNullException.ThrowIfNull(notificationService);
        ArgumentNullException.ThrowIfNull(diagnosticExportService);
        _store = store;
        _themeService = themeService;
        _notificationService = notificationService;
        _diagnosticExportService = diagnosticExportService;
        _confirmOnDelete = store.GetConfirmOnDelete();
        _confirmOnExit = store.GetConfirmOnExit();
        _selectedTheme = store.GetTheme();
        _compactSidebar = store.GetCompactSidebar();
        ReloadPairedDevices();
    }

    public IReadOnlyList<AppTheme> ThemeOptions { get; } =
        new[] { AppTheme.System, AppTheme.Light, AppTheme.Dark };
    public ObservableCollection<PairedDeviceRecord> PairedDevices { get; } = new();
    public bool HasPairedDevices => PairedDevices.Count > 0;

    [ObservableProperty] private bool _confirmOnDelete;
    [ObservableProperty] private bool _confirmOnExit;
    [ObservableProperty] private AppTheme _selectedTheme;
    [ObservableProperty] private bool _compactSidebar;
    [ObservableProperty] private bool _isDirty;
    [ObservableProperty] private bool _isSaving;
    [ObservableProperty] private bool _showSaveFeedback;
    [ObservableProperty] private string _saveFeedbackMessage = string.Empty;
    [ObservableProperty] private bool _hasSaveError;
    [ObservableProperty] private string _saveErrorMessage = string.Empty;
    [ObservableProperty] private string _diagnosticExportPath = string.Empty;

    partial void OnConfirmOnDeleteChanged(bool value) => MarkDirty();
    partial void OnConfirmOnExitChanged(bool value) => MarkDirty();
    partial void OnSelectedThemeChanged(AppTheme value) => MarkDirty();
    partial void OnCompactSidebarChanged(bool value) => MarkDirty();

    public event Action<bool>? CompactSidebarChanged;

    private void MarkDirty()
    {
        IsDirty = true;
        SaveCommand.NotifyCanExecuteChanged();
    }

    private bool CanSave() => IsDirty && !IsSaving;

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync()
    {
        IsSaving = true;
        HasSaveError = false;
        SaveErrorMessage = string.Empty;
        SaveCommand.NotifyCanExecuteChanged();
        try
        {
            await _store.SaveDesktopPreferencesAsync(
                ConfirmOnDelete,
                ConfirmOnExit,
                SelectedTheme,
                CompactSidebar);
            _themeService.Apply(SelectedTheme);
            CompactSidebarChanged?.Invoke(CompactSidebar);
            IsDirty = false;
            await ShowFeedbackAsync("Settings saved.");
        }
        catch (Exception ex)
        {
            HasSaveError = true;
            SaveErrorMessage = $"Settings could not be saved: {ex.Message}";
        }
        finally
        {
            IsSaving = false;
            SaveCommand.NotifyCanExecuteChanged();
        }
    }

    [RelayCommand]
    private async Task ExportDiagnosticsAsync()
    {
        try
        {
            DiagnosticExportPath = await _diagnosticExportService.ExportDiagnosticsAsync();
            await ShowFeedbackAsync($"Diagnostics exported to {DiagnosticExportPath}");
        }
        catch (Exception ex)
        {
            HasSaveError = true;
            SaveErrorMessage = $"Diagnostics could not be exported: {ex.Message}";
        }
    }

    [RelayCommand]
    private void RevokeDevice(PairedDeviceRecord? device)
    {
        if (device is null) return;
        _store.RevokePairedDevice(device.Endpoint);
        PairedDevices.Remove(device);
        OnPropertyChanged(nameof(HasPairedDevices));
    }

    private void ReloadPairedDevices()
    {
        PairedDevices.Clear();
        foreach (var device in _store.GetPairedDevices())
            PairedDevices.Add(device);
        OnPropertyChanged(nameof(HasPairedDevices));
    }

    private async Task ShowFeedbackAsync(string message)
    {
        _feedbackCts?.Cancel();
        _feedbackCts?.Dispose();
        _feedbackCts = new CancellationTokenSource();
        SaveFeedbackMessage = message;
        ShowSaveFeedback = true;
        try
        {
            await _notificationService.WaitForDismissalAsync(TimeSpan.FromSeconds(3), _feedbackCts.Token);
            ShowSaveFeedback = false;
            SaveFeedbackMessage = string.Empty;
        }
        catch (OperationCanceledException)
        {
        }
    }

    public void Dispose()
    {
        _feedbackCts?.Cancel();
        _feedbackCts?.Dispose();
        _feedbackCts = null;
        GC.SuppressFinalize(this);
    }
}
