#!/usr/bin/env python3
from __future__ import annotations

from pathlib import Path
import re
import sys

ROOT = Path(__file__).resolve().parents[1]
errors: list[str] = []


def read(path: str) -> str:
    target = ROOT / path
    if not target.exists():
        errors.append(f"missing required file: {path}")
        return ""
    return target.read_text(encoding="utf-8")


def require(text: str, needle: str, label: str) -> None:
    if needle not in text:
        errors.append(f"{label}: missing {needle!r}")


pagination = read("Core/Paging/PagedFetch.cs")
require(pagination, "IncompletePagedResultException", "pagination helper")
require(pagination, "MaximumPageCountExceeded", "pagination helper")
require(pagination, "RepeatedPageContent", "pagination helper")
require(pagination, "nextPage.Value <= currentPage", "pagination helper")

contacts = read("Core/ContactsApi.cs")
require(contacts, "PagedFetch.FetchAllAsync", "ContactsApi")
require(contacts, "pageDto.NextPage", "ContactsApi")

gallery = read("Core/GalleryApi.cs")
require(gallery, "PagedFetch.FetchAllAsync", "GalleryApi")
require(gallery, "pageData.NextPage", "GalleryApi")

bounded_copy = read("Core/IO/BoundedStreamCopy.cs")
require(bounded_copy, "TransferLimitExceededException", "bounded stream copy")
require(bounded_copy, "cancellationToken", "bounded stream copy")

limits = read("Core/IO/TransferLimits.cs")
for needle in ["MaxBackupArchiveBytes", "MaxFileTransferBytes", "MaxThumbnailBytes", "MaxContactPhotoBytes"]:
    require(limits, needle, "transfer limits")

polling = read("Core/Polling/OperationPollingPolicy.cs")
require(polling, "OperationPollingTimeoutException", "polling policy")
require(polling, "maxAttempts", "polling policy")
require(polling, "timeout", "polling policy")
require(polling, "CreateLinkedTokenSource", "polling policy")
require(polling, "ThrowIfCancellationRequested", "polling policy")

for workflow_path in ["Features/Backup/BackupWorkflow.cs", "Features/Backup/RestoreWorkflow.cs"]:
    workflow = read(workflow_path)
    require(workflow, "OperationPollingPolicy.PollAsync", workflow_path)
    if "while (true)" in workflow:
        errors.append(f"unbounded polling loop remains in {workflow_path}")

vcf_limits = read("Core/VcfParsingLimits.cs")
require(vcf_limits, "MaxLineCharacters", "VCF parsing limits")
require(vcf_limits, "MaxInputCharacters", "VCF parsing limits")
vcf_parser = read("Core/VcfParser.cs")
require(vcf_parser, "VcfParsingLimits.MaxLineCharacters", "VcfParser")
require(vcf_parser, "VcfParsingLimits.MaxInputCharacters", "VcfParser")


progress_content = read("Core/ProgressableStreamContent.cs")
require(progress_content, "maxBytes", "streaming upload content")
require(progress_content, "BoundedStreamCopy.CopyAsync", "streaming upload content")

file_api = read("Core/FileSystemApi.cs")
require(file_api, "TransferLimits.MaxFileTransferBytes", "FileSystemApi")
require(file_api, "BoundedStreamCopy.CopyAsync", "FileSystemApi")
if "ReadAllBytesAsync(localPath" in file_api:
    errors.append("FileSystemApi: whole-file upload allocation remains")

backup_api = read("Core/BackupApi.cs")
require(backup_api, "TransferLimits.MaxBackupArchiveBytes", "BackupApi")
require(backup_api, "BoundedStreamCopy.CopyAsync", "BackupApi")

archive_service = read("Features/Backup/BackupArchiveService.cs")
require(archive_service, "MaxDecompressedBackupBytes", "BackupArchiveService")
require(archive_service, "BoundedStreamCopy.CopyAsync", "BackupArchiveService")

contact_workflow = read("Features/Contacts/ContactFileWorkflow.cs")
require(contact_workflow, "WriteVcfAsync", "ContactFileWorkflow streaming export")

file_browser_xaml = read("Views/FileBrowserView.xaml")
require(file_browser_xaml, "VirtualizingWrapPanel", "File Browser virtualization")
gallery_xaml = read("Views/GalleryView.xaml")
require(gallery_xaml, "VirtualizingWrapPanel", "Gallery virtualization")
contacts_xaml = read("Views/ContactsView.xaml")
require(contacts_xaml, "VirtualizingPanel.VirtualizationMode=\"Recycling\"", "Contacts virtualization")

stall_monitor = read("Services/Performance/UiThreadStallMonitor.cs")
require(stall_monitor, "IHostedService", "UI-thread stall monitor")
require(stall_monitor, "WarningThreshold", "UI-thread stall monitor")
registration = read("Hosting/ServiceCollectionExtensions.cs")
require(registration, "AddHostedService<UiThreadStallMonitor>", "UI-thread stall monitor registration")

# Long-running feature work must be cancelled when its page/session is disposed.
for path, markers in {
    "ViewModels/ContactsViewModel.cs": ["_fetchCts?.Cancel();", "public void Dispose()"],
    "ViewModels/FileBrowserViewModel.cs": ["_transferCts?.Cancel();", "public void Dispose()"],
    "ViewModels/GalleryViewModel.cs": ["_loadCts?.Cancel();", "_transferCts?.Cancel();", "public void Dispose()"],
    "ViewModels/BackupViewModel.cs": ["_cts?.Cancel();", "public void Dispose()"],
    "Features/Gallery/GalleryViewPresentation.cs": ["_visibleThumbnailCts?.Cancel();", "public void Dispose()"],
}.items():
    content = read(path)
    for marker in markers:
        require(content, marker, f"page/session cancellation in {path}")

performance_project = read("tests/VcfEditor.Performance.Tests/VcfEditor.Performance.Tests.csproj")
performance_tests = read("tests/VcfEditor.Performance.Tests/LargeDataPerformanceTests.cs")
for needle in ["10_000", "5_000", "oneGigabyte", "BackupManifest"]:
    require(performance_tests, needle, "large-data performance tests")
solution = read("VcfEditor.sln")
require(solution, "VcfEditor.Performance.Tests", "solution performance-test registration")

for test_file in [
    "tests/VcfEditor.Core.Tests/PaginationGuardTests.cs",
    "tests/VcfEditor.Core.Tests/BoundedStreamCopyTests.cs",
    "tests/VcfEditor.Core.Tests/OperationPollingPolicyTests.cs",
    "tests/VcfEditor.Core.Tests/VcfParserTests.cs",
]:
    read(test_file)

# New while(true) loops are prohibited unless reviewed on the same line.
for path in ROOT.rglob("*.cs"):
    if any(part in {"bin", "obj"} for part in path.parts):
        continue
    for line_number, line in enumerate(path.read_text(encoding="utf-8").splitlines(), start=1):
        if re.search(r"while\s*\(\s*true\s*\)", line) and "LOOP-REVIEWED:" not in line:
            errors.append(f"unreviewed while(true): {path.relative_to(ROOT)}:{line_number}")

verify_windows = read("scripts/verify-windows.ps1")
require(verify_windows, "verify-phase9-windows.py", "Windows verification entry point")

plan = read("Modernization_Plan.md")
require(plan, "Phase 9 Windows implementation status", "modernization plan")

if errors:
    print("Phase 9 Windows verification failed:")
    for error in errors:
        print(f"- {error}")
    sys.exit(1)

print("Phase 9 Windows loops, paging, cancellation, and performance verification passed.")
