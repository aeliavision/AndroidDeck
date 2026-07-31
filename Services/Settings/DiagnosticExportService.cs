using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using VcfEditor.Core.Settings;

namespace VcfEditor.Services.Settings;

public sealed class DiagnosticExportService : IDiagnosticExportService
{
    private readonly IAppSettingsStore _settingsStore;
    private readonly IThemeService _themeService;

    public DiagnosticExportService(IAppSettingsStore settingsStore, IThemeService themeService)
    {
        ArgumentNullException.ThrowIfNull(settingsStore);
        ArgumentNullException.ThrowIfNull(themeService);
        _settingsStore = settingsStore;
        _themeService = themeService;
    }

    public async Task<string> ExportDiagnosticsAsync(CancellationToken cancellationToken = default)
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            "AndroidDeck Diagnostics");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"AndroidDeck-Diagnostics-{DateTime.Now:yyyyMMdd-HHmmss}.txt");
        var builder = new StringBuilder();
        builder.AppendLine("AndroidDeck diagnostics (redacted)");
        builder.AppendLine(CultureInfo.InvariantCulture, $"Version: {Constants.AppVersion}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"Generated: {DateTimeOffset.Now:O}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"Operating system: {Environment.OSVersion}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"64-bit process: {Environment.Is64BitProcess}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"Theme: {_themeService.CurrentTheme}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"Paired device count: {_settingsStore.GetPairedDevices().Count}");
        builder.AppendLine("Secrets, certificate fingerprints, backup seeds, pairing codes, and endpoints are intentionally omitted.");
        var content = builder.ToString();
        await File.WriteAllTextAsync(path, content, Encoding.UTF8, cancellationToken);
        return path;
    }
}
