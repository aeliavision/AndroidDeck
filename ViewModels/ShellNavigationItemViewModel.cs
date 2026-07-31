using CommunityToolkit.Mvvm.ComponentModel;
using VcfEditor.Navigation;

namespace VcfEditor.ViewModels;

public sealed class ShellNavigationItemViewModel : ObservableObject
{
    private bool _isVisible = true;
    private bool _isEnabled = true;
    private string? _disabledReason;
    private string? _badgeText;

    public ShellNavigationItemViewModel(ShellNavigationDefinition definition)
    {
        Definition = definition;
    }

    public ShellNavigationDefinition Definition { get; }
    public string Key => Definition.Key;
    public ShellDestination Destination => Definition.Destination;
    public string Label => Definition.Label;
    public string IconGlyph => Definition.IconGlyph;
    public string GroupKey => Definition.GroupKey;
    public string GroupLabel => Definition.GroupLabel;
    public int GroupOrder => Definition.GroupOrder;
    public int ItemOrder => Definition.ItemOrder;
    public string AccessKey => Definition.AccessKey;
    public string AutomationName => Label;

    public bool IsVisible
    {
        get => _isVisible;
        set => SetProperty(ref _isVisible, value);
    }

    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (!SetProperty(ref _isEnabled, value))
                return;

            OnPropertyChanged(nameof(ToolTipText));
            OnPropertyChanged(nameof(AutomationHelpText));
        }
    }

    public string? DisabledReason
    {
        get => _disabledReason;
        set
        {
            if (!SetProperty(ref _disabledReason, value))
                return;

            OnPropertyChanged(nameof(ToolTipText));
            OnPropertyChanged(nameof(AutomationHelpText));
        }
    }

    public string? BadgeText
    {
        get => _badgeText;
        set
        {
            if (!SetProperty(ref _badgeText, value))
                return;

            OnPropertyChanged(nameof(HasBadge));
            OnPropertyChanged(nameof(AutomationHelpText));
        }
    }

    public bool HasBadge => !string.IsNullOrWhiteSpace(BadgeText);

    public string ToolTipText => IsEnabled
        ? $"{Label} ({AccessKey})"
        : $"{Label} — {DisabledReason}";

    public string AutomationHelpText
    {
        get
        {
            var badge = HasBadge ? $" Badge {BadgeText}." : string.Empty;
            return IsEnabled
                ? $"{GroupLabel}. Shortcut {AccessKey}.{badge}"
                : $"Unavailable. {DisabledReason}{badge}";
        }
    }
}
