using System.Threading;
using System.Threading.Tasks;

namespace VcfEditor.Services.Settings;

public interface IDiagnosticExportService
{
    Task<string> ExportDiagnosticsAsync(CancellationToken cancellationToken = default);
}
