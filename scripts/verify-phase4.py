#!/usr/bin/env python3
"""Phase 4 architecture exit-gate verification for the Windows desktop app."""
from __future__ import annotations

import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
failures: list[str] = []


def text(path: str) -> str:
    p = ROOT / path
    if not p.exists():
        failures.append(f"missing required file: {path}")
        return ""
    return p.read_text(encoding="utf-8-sig")


def require(path: str, needle: str, label: str) -> None:
    if needle not in text(path):
        failures.append(f"{label}: {path} must contain {needle!r}")


def forbid_tree(pattern: str, label: str, *, allowed: set[str] | None = None) -> None:
    rx = re.compile(pattern)
    allowed = allowed or set()
    for p in ROOT.rglob("*.cs"):
        rel = p.relative_to(ROOT).as_posix()
        if rel in allowed or any(part in {"bin", "obj"} for part in p.parts):
            continue
        for lineno, line in enumerate(p.read_text(encoding="utf-8-sig").splitlines(), 1):
            if rx.search(line):
                failures.append(f"{label}: {rel}:{lineno}: {line.strip()}")


# Task 4.1: one Generic Host composition root.
app = text("App.xaml.cs")
for forbidden in ("App.Services", "BuildServiceProvider(", "new ServiceCollection("):
    if forbidden in app:
        failures.append(f"Generic Host: App.xaml.cs still contains {forbidden!r}")
for required in (
    "Host.CreateDefaultBuilder()",
    "ValidateOnBuild = true",
    "ValidateScopes = true",
    "AddAndroidDeckDesktop(context.Configuration)",
    "StartAsync",
    "StopAsync",
):
    if required not in app:
        failures.append(f"Generic Host: App.xaml.cs missing {required!r}")
require(
    "Hosting/ServiceCollectionExtensions.cs",
    "AddAndroidDeckDesktop",
    "Generic Host registration extension",
)

# Task 4.2: obsolete MainWindow removed, including project exclusions.
for obsolete in (
    "Views/MainWindow.xaml",
    "Views/MainWindow.xaml.cs",
    "ViewModels/MainWindowViewModel.cs",
):
    if (ROOT / obsolete).exists():
        failures.append(f"obsolete file still exists: {obsolete}")
csproj = text("VcfEditor.csproj")
if "Views\\MainWindow" in csproj or "MainWindowViewModel" in csproj:
    failures.append("VcfEditor.csproj still contains obsolete MainWindow exclusions/references")

# Task 4.1/4.3: no service locator in production code.
forbid_tree(r"\bApp\.Services\b", "service locator")

# Task 4.3: phone pages are created by a dedicated scope factory, never ActivatorUtilities.
page_factory = text("Services/PageFactory.cs")
for forbidden in ("IServiceProvider", "ActivatorUtilities", "CreateInstance<"):
    if forbidden in page_factory:
        failures.append(f"phone-session scope: PageFactory still contains {forbidden!r}")
for required in ("IPhoneSessionScopeFactory", "PhoneSessionScope"):
    if required not in page_factory:
        failures.append(f"phone-session scope: PageFactory missing {required!r}")
for required_file in (
    "Features/PhoneSession/PhoneSessionContext.cs",
    "Features/PhoneSession/IPhoneSessionScopeFactory.cs",
    "Features/PhoneSession/PhoneSessionScope.cs",
    "Features/PhoneSession/PhoneSessionScopeFactory.cs",
):
    if not (ROOT / required_file).exists():
        failures.append(f"phone-session scope: missing {required_file}")

# Task 4.4: MessageBox is an implementation detail of WpfDialogService only.
forbid_tree(
    r"\b(?:System\.Windows\.)?MessageBox\.Show\s*\(",
    "direct MessageBox",
    allowed={"Services/WpfDialogService.cs"},
)

# Helper async-void methods are forbidden; true event/timer callbacks remain allowed.
async_void = re.compile(
    r"\b(?:private|protected|public|internal)\s+async\s+void\s+(?P<name>[A-Za-z_][A-Za-z0-9_]*)\s*\("
)
allowed_suffixes = (
    "_Click", "_Loaded", "_Unloaded", "_Changed", "_SelectionChanged",
    "_KeyDown", "_DoubleClick", "_Drop", "_DragOver", "_Closing", "_Closed",
)
allowed_names = {"OnStartup", "HeartbeatCallback", "OnDashboardPhoneConnected"}
xaml_event_handlers: set[str] = set()
for xaml in ROOT.rglob("*.xaml"):
    if any(part in {"bin", "obj"} for part in xaml.parts):
        continue
    xaml_event_handlers.update(
        re.findall(r'\b[A-Z][A-Za-z]+="([A-Za-z_][A-Za-z0-9_]*)"', xaml.read_text(encoding="utf-8-sig"))
    )
for p in ROOT.rglob("*.cs"):
    if any(part in {"bin", "obj"} for part in p.parts):
        continue
    rel = p.relative_to(ROOT).as_posix()
    for lineno, line in enumerate(p.read_text(encoding="utf-8-sig").splitlines(), 1):
        match = async_void.search(line)
        if not match:
            continue
        name = match.group("name")
        if name in allowed_names or name in xaml_event_handlers or name.startswith("On") or name.endswith(allowed_suffixes):
            continue
        failures.append(f"helper async void: {rel}:{lineno}: {name}")

# Phase 4 code-behind limits.
limits = {
    "Views/ShellWindow.xaml.cs": 80,
    "Views/ContactsView.xaml.cs": 150,
    "Views/FileBrowserView.xaml.cs": 120,
    "Views/GalleryView.xaml.cs": 150,
}
for path, limit in limits.items():
    p = ROOT / path
    if not p.exists():
        failures.append(f"missing code-behind: {path}")
        continue
    count = len(p.read_text(encoding="utf-8-sig").splitlines())
    if count > limit:
        failures.append(f"code-behind limit: {path} has {count} lines; maximum is {limit}")


# Task 4.4: primary user workflows are command-bound, not button click bridges.
command_bindings = {
    "Views/ContactsView.xaml": (
        "Actions.OpenFileCommand", "Actions.SaveFileCommand", "Actions.AddContactCommand",
        "Actions.EditContactsCommand", "Actions.DeleteContactsCommand",
        "Actions.ConnectPhoneCommand", "Actions.RefreshPhoneCommand",
        "Actions.DisconnectPhoneCommand", "Actions.AddPhoneNumberCommand",
        "Actions.EditPhoneNumberCommand", "Actions.DeletePhoneNumberCommand",
    ),
    "Views/FileBrowserView.xaml": (
        "RefreshCommand", "Actions.BackCommand", "Actions.UploadCommand",
        "Actions.DownloadCommand", "CreateFolderCommand",
        "DeleteCommand", "RenameCommand", "MoveCommand",
        "Actions.CancelTransferCommand",
    ),
    "Views/GalleryView.xaml": (
        "Actions.RefreshCommand", "Actions.SelectAllCommand",
        "Actions.ClearSelectionCommand", "Actions.DownloadCommand",
        "DeleteCommand", "RenameCommand", "MoveCommand",
        "EditMetadataCommand", "Actions.ClosePreviewCommand",
        "Actions.PreviousCommand", "Actions.NextCommand",
        "Actions.CancelTransferCommand",
    ),
}
for path, commands in command_bindings.items():
    source = text(path)
    for command in commands:
        if command not in source:
            failures.append(f"command binding: {path} missing {command}")

for path in (
    "Features/Contacts/ContactEditorWorkflow.cs",
    "Features/Files/FileBrowserInteraction.cs",
    "Features/Gallery/GalleryInteraction.cs",
    "ViewModels/BackupViewModel.cs",
):
    source = text(path)
    if "AsyncRelayCommand" not in source:
        failures.append(f"async command controller: {path} missing AsyncRelayCommand")

# Task 4.5: focused collaborators must exist and be wired into matching view models.
workflow_contracts = {
    "Features/Contacts/ContactFileWorkflow.cs": ("ViewModels/ContactsViewModel.cs", "IContactFileWorkflow"),
    "Features/Contacts/ContactEditorWorkflow.cs": ("Views/ContactsView.xaml.cs", "IContactEditorWorkflow"),
    "Features/Files/FileTransferWorkflow.cs": ("ViewModels/FileBrowserViewModel.cs", "IFileTransferWorkflow"),
    "Features/Gallery/GalleryTransferWorkflow.cs": ("ViewModels/GalleryViewModel.cs", "IGalleryTransferWorkflow"),
    "Features/Backup/BackupWorkflow.cs": ("ViewModels/BackupViewModel.cs", "IBackupWorkflow"),
    "Features/Backup/RestoreWorkflow.cs": ("ViewModels/BackupViewModel.cs", "IRestoreWorkflow"),
    "Features/Backup/BackupHistoryService.cs": ("ViewModels/BackupViewModel.cs", "IBackupHistoryService"),
    "Features/Backup/BackupArchiveService.cs": ("ViewModels/BackupViewModel.cs", "IBackupArchiveService"),
}
for workflow, (consumer, interface_name) in workflow_contracts.items():
    if not (ROOT / workflow).exists():
        failures.append(f"focused collaborator missing: {workflow}")
    if interface_name not in text(consumer):
        failures.append(f"focused collaborator not wired: {consumer} missing {interface_name}")


backup_vm = text("ViewModels/BackupViewModel.cs")
for forbidden in (
    "Aes.Create()",
    "Rfc2898DeriveBytes.Pbkdf2",
    "HMACSHA256",
    "while (!_cts.Token.IsCancellationRequested)",
):
    if forbidden in backup_vm:
        failures.append(f"backup responsibility split: BackupViewModel still contains {forbidden!r}")

# Oversized view models coordinate state only; direct local file-system operations live in services.
for path in (
    "ViewModels/BackupViewModel.cs",
    "ViewModels/ContactsViewModel.cs",
    "ViewModels/FileBrowserViewModel.cs",
    "ViewModels/GalleryViewModel.cs",
):
    source = text(path)
    for forbidden in ("File.Exists(", "File.Delete(", "Directory.Exists(", "Directory.GetFiles(", "new FileInfo("):
        if forbidden in source:
            failures.append(f"view-model file operation: {path} still contains {forbidden!r}")

require(
    "Features/Files/LocalUploadPlanner.cs",
    "ILocalUploadPlanner",
    "local upload planning service",
)
if not (ROOT / "tests/VcfEditor.Core.Tests/LocalUploadPlannerTests.cs").exists():
    failures.append("phase 4 workflow test missing: tests/VcfEditor.Core.Tests/LocalUploadPlannerTests.cs")
require(
    "Features/Backup/BackupArchiveService.cs",
    "TryDeleteTemporaryFile",
    "backup temporary-file service boundary",
)
require(
    "Features/Backup/BackupArchiveService.cs",
    "GetFileSize",
    "backup file-size service boundary",
)

# Event bridges in modernized feature views must await work so failures reach the UI boundary.
for path in (
    "Views/BackupView.xaml.cs",
    "Views/ContactsView.xaml.cs",
    "Views/GalleryView.xaml.cs",
    "Helpers/DragDropHelper.cs",
):
    if re.search(r"_\s*=\s*[^;\n]*Async\s*\(", text(path)):
        failures.append(f"unobserved async work: {path} contains a discarded async call")
require(
    "Helpers/DragDropHelper.cs",
    "Func<string, Task>",
    "async drag/drop callback contract",
)

# Phase 4 error handling must not silently swallow failures in modernized workflows.
for path in (
    "ViewModels/BackupViewModel.cs",
    "Features/Backup/BackupArchiveService.cs",
    "Features/Backup/BackupWorkflow.cs",
    "Features/Backup/RestoreWorkflow.cs",
    "Features/Contacts/ContactEditorWorkflow.cs",
    "Features/Contacts/ContactFileWorkflow.cs",
    "Features/Files/FileBrowserInteraction.cs",
    "Features/Files/FileTransferWorkflow.cs",
    "Features/Gallery/GalleryInteraction.cs",
    "Features/Gallery/GalleryTransferWorkflow.cs",
):
    source = text(path)
    if re.search(r"catch\s*\{", source):
        failures.append(f"broad exception handling: {path} contains an unqualified catch block")
    if re.search(r"catch\s*(?:\([^)]*\))?\s*\{\s*\}", source, re.DOTALL):
        failures.append(f"silent exception handling: {path} contains an empty catch block")

for test_path in (
    "tests/VcfEditor.Core.Tests/ContactFileWorkflowTests.cs",
    "tests/VcfEditor.Core.Tests/BackupArchiveServiceTests.cs",
):
    if not (ROOT / test_path).exists():
        failures.append(f"phase 4 workflow test missing: {test_path}")

if failures:
    print("Phase 4 verification FAILED:")
    for failure in failures:
        print(f" - {failure}")
    sys.exit(1)

print("Phase 4 architecture verification passed.")
