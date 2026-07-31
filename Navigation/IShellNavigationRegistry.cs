using System.Collections.Generic;

namespace VcfEditor.Navigation;

public interface IShellNavigationRegistry
{
    IReadOnlyList<ShellNavigationDefinition> Definitions { get; }
}
