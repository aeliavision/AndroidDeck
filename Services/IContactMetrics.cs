using System.ComponentModel;

namespace VcfEditor.Services;

public interface IContactMetrics : INotifyPropertyChanged
{
    int ContactCount { get; }
    bool IsSourceLoaded { get; }
}
