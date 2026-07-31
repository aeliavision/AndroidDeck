using System;

namespace VcfEditor.Services;

public interface IShellConnectionCoordinator : IDisposable
{
    void Start();
}
