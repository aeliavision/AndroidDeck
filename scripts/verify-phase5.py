#!/usr/bin/env python3
from pathlib import Path
import re
import sys
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
errors: list[str] = []

def require_file(path: str) -> str:
    p = ROOT / path
    if not p.exists():
        errors.append(f"missing required file: {path}")
        return ""
    return p.read_text(encoding="utf-8")

def require(text: str, needle: str, label: str) -> None:
    if needle not in text:
        errors.append(f"{label}: missing {needle!r}")

behavior = require_file("Behaviors/ResponsiveLayoutBehavior.cs")
require(behavior, "ResponsiveLayoutMode.Compact", "responsive behavior")
require(behavior, "ResponsiveLayoutMode.Medium", "responsive behavior")
require(behavior, "ResponsiveLayoutMode.Expanded", "responsive behavior")
require(behavior, "ResolveMode", "responsive behavior")
require_file("tests/VcfEditor.UI.Tests/ResponsiveLayoutBehaviorTests.cs")

phase5_theme = require_file("Themes/Phase5.xaml")
for key in [
    "PageHeaderPanel", "StatusBanner", "OperationCenterCard", "Dialog.PrimaryAction",
    "CommandBarPanel", "EmptyStatePanel", "Brush.MediaScrim"
]:
    require(phase5_theme, key, "Phase 5 theme")

app = require_file("App.xaml")
require(app, "Themes/Phase5.xaml", "App resources")

dashboard = require_file("Views/DashboardView.xaml")
require(dashboard, "ScrollViewer", "Dashboard")
require(dashboard, "WrapPanel", "Dashboard")
require(dashboard, "Recent activity", "Dashboard")
require_file("ViewModels/DashboardViewModel.cs")

contacts = require_file("Views/ContactsView.xaml")
for needle in ["Text=\"Contacts\"", "New contact", "ContextMenu", "ResponsiveLayoutBehavior.Mode"]:
    require(contacts, needle, "Contacts")

files = require_file("Views/FileBrowserView.xaml")
for needle in ["Breadcrumbs", "Upload to phone", "Download to PC", "OperationCenterCard", "VirtualizingPanel.IsVirtualizing"]:
    require(files, needle, "File Browser")

gallery = require_file("Views/GalleryView.xaml")
for needle in ["Brush.MediaScrim", "LoadMoreCommand", "Empty album", "ResponsiveLayoutBehavior.Mode"]:
    require(gallery, needle, "Gallery")

backup = require_file("Views/BackupView.xaml")
for needle in ["Create backup", "Restore backup", "ScopeSummary", "Restore warning", "OperationCenterCard", "RestoreItemOutcomes"]:
    require(backup, needle, "Backup")
backup_api = require_file("Core/BackupApi.cs")
require(backup_api, "RestoreItemOutcome", "Backup restore protocol")
backup_vm = require_file("ViewModels/BackupViewModel.cs")
require(backup_vm, "RestoreItemOutcomes", "BackupViewModel")
require(backup_api, "itemResults", "Backup restore protocol")

settings_vm = require_file("ViewModels/SettingsViewModel.cs")
require_file("tests/VcfEditor.UI.Tests/SettingsViewModelTests.cs")
for needle in ["ObservableObject", "[RelayCommand", "SaveAsync", "ExportDiagnostics", "RevokeDevice"]:
    require(settings_vm, needle, "SettingsViewModel")
settings_xaml = require_file("Views/SettingsView.xaml")
for needle in ["System", "Light", "Dark", "Paired devices", "Export diagnostics"]:
    require(settings_xaml, needle, "Settings")

store = require_file("Core/Settings/IAppSettingsStore.cs")
for needle in ["GetTheme", "SetTheme", "GetCompactSidebar", "SetCompactSidebar"]:
    require(store, needle, "settings store")

require_file("Views/AppDialogWindow.cs")
require_file("Views/AppMessageDialog.cs")
wpf_dialogs = require_file("Services/WpfDialogService.cs")
if "MessageBox.Show" in wpf_dialogs:
    errors.append("WpfDialogService still uses native MessageBox instead of themed app dialogs")
dialogs = [p for p in (ROOT / "Views").glob("*Dialog*.cs") if p.name != "AppDialogWindow.cs"]
for p in dialogs:
    text = p.read_text(encoding="utf-8")
    if "Window" in text and "AppDialogWindow" not in text and p.name not in {"ConnectPhoneDialog.xaml.cs", "EditContactDialog.xaml.cs"}:
        errors.append(f"dialog does not use AppDialogWindow: {p.relative_to(ROOT)}")

hex_pattern = re.compile(r"#[0-9A-Fa-f]{6,8}")
named_color_pattern = re.compile(r'(?:Foreground|Background|BorderBrush)="(?:White|Black|Red|Blue|Green|Gray)"', re.IGNORECASE)
for folder in [ROOT / "Views", ROOT / "Themes"]:
    for p in folder.glob("*.xaml"):
        if p.name.startswith("Generated."):
            continue
        xaml_text = p.read_text(encoding="utf-8")
        for match in hex_pattern.finditer(xaml_text):
            errors.append(f"raw color in {p.relative_to(ROOT)}: {match.group(0)}")
        for match in named_color_pattern.finditer(xaml_text):
            errors.append(f"literal named color in {p.relative_to(ROOT)}: {match.group(0)}")


# Phase 5 completion requirements that are easy to regress.
button_behavior = require_file("Behaviors/ButtonContextMenuBehavior.cs")
require(button_behavior, "contextMenu.IsOpen = true", "context-menu behavior")
for screen in [
    "Views/DashboardView.xaml",
    "Views/ContactsView.xaml",
    "Views/FileBrowserView.xaml",
    "Views/GalleryView.xaml",
    "Views/BackupView.xaml",
]:
    text = require_file(screen)
    require(text, "ButtonContextMenuBehavior.IsEnabled=\"True\"", screen)

gallery_workflow = require_file("Features/Gallery/GalleryTransferWorkflow.cs")
require(gallery_workflow, "GetMediaPageAsync", "Gallery workflow")
gallery_vm = require_file("ViewModels/GalleryViewModel.cs")
require(gallery_vm, "GetMediaPageAsync", "GalleryViewModel")
require(gallery_vm, "NextPage", "GalleryViewModel")
require(gallery_vm, "LoadThumbnailsForItemsAsync", "GalleryViewModel")
gallery_presentation = require_file("Features/Gallery/GalleryViewPresentation.cs")
require(gallery_presentation, "_visibleThumbnailCts", "Gallery thumbnail lifecycle")
require(gallery_presentation, "GetVisibleMediaItems", "Gallery thumbnail lifecycle")
require(gallery, "ScrollChanged=\"MediaGrid_ScrollChanged\"", "Gallery thumbnail lifecycle")

json_store = require_file("Core/Settings/JsonAppSettingsStore.cs")
require(json_store, "deserialized.PairedDeviceLastUsedUtc ??", "settings deserialization")
require(json_store, "deserialized.PinnedCertSha256ByEndpoint ??", "settings deserialization")
require_file("tests/VcfEditor.Core.Tests/RestoreStatusResponseTests.cs")

for dialog_name in [
    "NewFolderDialog.cs", "RenameDialog.cs", "MoveDialog.cs",
    "TextInputDialog.cs", "PasswordDialog.cs", "PhoneNumberDialog.cs"
]:
    require(require_file(f"Views/{dialog_name}"), "SetInlineValidation", dialog_name)

raw_code_patterns = [
    re.compile(r"Color\.From(?:Rgb|Argb)\s*\("),
    re.compile(r"Brushes\.[A-Za-z]+"),
    re.compile(r"new\s+SolidColorBrush\s*\("),
]
for p in (ROOT / "Views").glob("*.cs"):
    text = p.read_text(encoding="utf-8")
    for pattern in raw_code_patterns:
        if pattern.search(text):
            errors.append(f"raw code color in {p.relative_to(ROOT)}: {pattern.pattern}")
            break

for p in (ROOT / "Views").glob("*.xaml"):
    try:
        ET.parse(p)
    except ET.ParseError as exc:
        errors.append(f"invalid XAML XML {p.relative_to(ROOT)}: {exc}")

verify_windows = require_file("scripts/verify-windows.ps1")
require(verify_windows, "verify-phase5.py", "Windows verification entry point")

if errors:
    print("Phase 5 verification failed:")
    for error in errors:
        print(f"- {error}")
    sys.exit(1)

print("Phase 5 desktop modernization verification passed.")
