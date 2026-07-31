#!/usr/bin/env python3
from pathlib import Path
import re
import sys

root = Path(__file__).resolve().parents[1]
errors: list[str] = []


def require_file(path: str) -> str:
    p = root / path
    if not p.exists():
        errors.append(f"missing file: {path}")
        return ""
    return p.read_text(encoding="utf-8-sig")


registry = require_file("Navigation/ShellNavigationRegistry.cs")
policy = require_file("Navigation/ShellNavigationPolicy.cs")
layout_policy = require_file("Navigation/ShellLayoutPolicy.cs")
vm = require_file("ViewModels/ShellWindowViewModel.cs")
page_factory = require_file("Services/PageFactory.cs")
navigation_service = require_file("Services/NavigationService.cs")
connection_coordinator = require_file("Services/ShellConnectionCoordinator.cs")
contact_metrics = require_file("Services/IContactMetrics.cs")
shell_vm_tests = require_file("tests/VcfEditor.UI.Tests/Navigation/ShellWindowViewModelTests.cs")
shell_xaml = require_file("Views/ShellWindow.xaml")
sidebar_xaml = require_file("Views/ShellSidebarView.xaml")
phone_status_xaml = require_file("Views/PhoneConnectionStatusView.xaml")
codebehind = require_file("Views/ShellWindow.xaml.cs")
app = require_file("App.xaml.cs")
hosting = require_file("Hosting/ServiceCollectionExtensions.cs")
nav_theme = require_file("Themes/Navigation.xaml")
overlay_behavior = require_file("Behaviors/OverlayDrawerBehavior.cs")
sidebar_codebehind = require_file("Views/ShellSidebarView.xaml.cs")

for group in ("Overview", "Phone Data", "Transfers", "System"):
    if group not in registry:
        errors.append(f"navigation group missing from registry: {group}")

for destination in ("Dashboard", "Contacts", "FileBrowser", "Gallery", "Backup", "Settings"):
    if destination not in registry:
        errors.append(f"destination missing from registry: {destination}")

for shortcut in ("Alt+D", "Alt+C", "Alt+F", "Alt+G", "Alt+B", "Alt+S"):
    if shortcut not in registry:
        errors.append(f"navigation shortcut missing from registry: {shortcut}")

for forbidden in (
    "DashboardNavButton_Click",
    "ContactsNavButton_Click",
    "FileBrowserNavButton_Click",
    "GalleryNavButton_Click",
    "BackupNavButton_Click",
    "SettingsNavButton_Click",
    "private enum ShellSection",
    "private void NavigateTo(",
    "new FileBrowserViewModel",
    "new GalleryViewModel",
    "new BackupViewModel",
    "MainContent.Content",
    "Tag = \"Selected\"",
):
    if forbidden in codebehind:
        errors.append(f"legacy shell navigation/construction remains: {forbidden}")

for required in (
    "IContactMetrics",
    "NavigationItems",
    "SelectedNavigationItem",
    "CurrentContent",
    "LayoutMode",
    "PreferredDesktopSidebarMode",
    "IsOverlayOpen",
    "RetryConnectionRequested",
    "[RelayCommand]",
    "ToggleSidebar",
    "CloseOverlay",
    "RetryConnection",
    "NavigateByKeyAsync",
    "INavigationService",
):
    if required not in vm:
        errors.append(f"ShellWindowViewModel missing: {required}")

for required in (
    'Content="{Binding CurrentContent}"',
    'Command="{Binding ToggleSidebarCommand}"',
    'Command="{Binding CloseOverlayCommand}"',
    'Command="{Binding NavigateByKeyCommand}"',
    'AutomationProperties.LiveSetting="Polite"',
    'Key="Escape"',
    'Modifiers="Alt"',
    'KeyboardNavigation.TabNavigation="Cycle"',
    'OverlayDrawerBehavior.IsOpen="{Binding IsOverlayVisible}"',
    'Grid.Row="0"',
):
    if required not in shell_xaml:
        errors.append(f"ShellWindow XAML missing binding/accessibility behavior: {required}")

for required in (
    'Source="{Binding NavigationItems}"',
    'SelectedItem="{Binding SelectedNavigationItem, Mode=TwoWay}"',
    'AutomationProperties.Name="Main navigation"',
    'AutomationProperties.HelpText',
    'ToolTipService.ShowOnDisabled',
    'Shell.NavigationBadge',
    'PhoneConnectionStatusView',
):
    if required not in sidebar_xaml:
        errors.append(f"ShellSidebar XAML missing grouped navigation behavior: {required}")

for required in (
    "RetryConnectionCommand",
    "CanRetryConnection",
    "PhoneStatusAutomationText",
    "AutomationProperties.LiveSetting",
):
    if required not in phone_status_xaml:
        errors.append(f"phone connection status component missing: {required}")

for required in (
    "IPageFactory",
    "IPhoneSessionScopeFactory",
    "PhoneSessionScope",
    "IAsyncInitializable",
    "InitializePageAsync",
):
    if required not in page_factory:
        errors.append(f"PageFactory missing: {required}")

for required in ("INavigationService", "NavigateAsync", "Navigated", "InitializePageAsync"):
    if required not in navigation_service:
        errors.append(f"NavigationService missing: {required}")

for required in ("IShellConnectionCoordinator", "StartCapabilityDiscovery", "RetryConnectionRequested"):
    if required not in connection_coordinator:
        errors.append(f"ShellConnectionCoordinator missing: {required}")

for required in ("ContactCount", "INotifyPropertyChanged"):
    if required not in contact_metrics:
        errors.append(f"IContactMetrics missing: {required}")

for required in ("UpdateWindowWidth", "NavigateToAsync", "DisabledNavigation", "Overlay"):
    if required not in shell_vm_tests:
        errors.append(f"ShellWindowViewModel tests missing scenario: {required}")

for registration in (
    "AddSingleton<IContactMetrics>(provider => provider.GetRequiredService<ContactsViewModel>())",
    "AddSingleton<IPageFactory, PageFactory>()",
    "AddSingleton<INavigationService, NavigationService>()",
    "AddSingleton<ShellWindowViewModel>()",
    "AddSingleton<IShellConnectionCoordinator, ShellConnectionCoordinator>()",
):
    if registration not in hosting:
        errors.append(f"DI registration missing: {registration}")

for required in (
    "Shell.NavigationList",
    "Shell.NavigationItem",
    "Shell.NavigationBadge",
    "Shell.OverlayScrim",
    "Shell.PhoneStatusActionButton",
):
    if required not in nav_theme:
        errors.append(f"navigation resource missing: {required}")


for required in ("ScrollIntoView", "FocusSelectedItem", "Keyboard.Focus"):
    if required not in sidebar_codebehind:
        errors.append(f"ShellSidebar focus restoration missing: {required}")

for required in ("SystemParameters.ClientAreaAnimation", "DoubleAnimation", "Visibility.Collapsed"):
    if required not in overlay_behavior:
        errors.append(f"overlay drawer behavior missing: {required}")

for path in (
    "ViewModels/FileBrowserViewModel.cs",
    "ViewModels/GalleryViewModel.cs",
    "ViewModels/BackupViewModel.cs",
):
    text = require_file(path)
    for required in ("IAsyncInitializable", "IsInitialized", "InitializeAsync"):
        if required not in text:
            errors.append(f"{path} missing explicit page initialization contract: {required}")
    if path.endswith("GalleryViewModel.cs") and "public Task InitialiseAsync() => InitializeAsync();" not in text:
        errors.append("GalleryViewModel legacy initialization alias must preserve one-time initialization")

if "Albums.Count == 0 ||" in page_factory or "MediaItems.Count == 0" in page_factory:
    errors.append("PageFactory still infers initialization from collection counts")

for required in (
    "Connect a phone to browse files.",
    "Photo and video permission is missing",
    "CapabilityError",
):
    if required not in policy:
        errors.append(f"capability policy missing explicit reason: {required}")

for required in ("GetEffectiveMode", "ExpandedMinimumWidth", "CompactMinimumWidth"):
    if required not in layout_policy:
        errors.append(f"responsive layout policy missing: {required}")

for handler in re.findall(r"_contactMetrics\.PropertyChanged\s*[+-]=\s*(\w+)\s*;", vm):
    if re.search(rf"private\s+void\s+{re.escape(handler)}\s*\(", vm) is None:
        errors.append(f"contact metrics event handler is referenced but not declared: {handler}")

shell_code_lines = [line for line in codebehind.splitlines() if line.strip()]
if len(shell_code_lines) > 80:
    errors.append(f"ShellWindow code-behind exceeds 80 non-blank lines: {len(shell_code_lines)}")

if errors:
    print("Phase 3 navigation contract FAILED")
    for error in errors:
        print(f" - {error}")
    sys.exit(1)

print("Phase 3 navigation contract passed")
print(f"- ShellWindow code-behind: {len(shell_code_lines)} non-blank lines")
