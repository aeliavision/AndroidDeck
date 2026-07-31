using VcfEditor.Models;

namespace VcfEditor.Services;

public interface IThemeService
{
    AppTheme CurrentTheme { get; }

    void Apply(AppTheme theme);
}
