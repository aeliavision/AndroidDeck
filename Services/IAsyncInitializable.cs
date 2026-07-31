using System.Threading;
using System.Threading.Tasks;

namespace VcfEditor.Services;

public interface IAsyncInitializable
{
    bool IsInitialized { get; }
    Task InitializeAsync(CancellationToken cancellationToken = default);
}
