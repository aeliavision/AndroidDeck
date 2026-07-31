#!/usr/bin/env python3
from __future__ import annotations

import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
errors: list[str] = []


def read(relative: str) -> str:
    return (ROOT / relative).read_text(encoding="utf-8-sig")


def require(condition: bool, message: str) -> None:
    if not condition:
        errors.append(message)

settings_vm = read("ViewModels/SettingsViewModel.cs")
settings_xaml = read("Views/SettingsView.xaml")
require("public string AppName =>" not in settings_vm, "SettingsViewModel still exposes instance AppName metadata")
require("public string AppVersion =>" not in settings_vm, "SettingsViewModel still exposes instance AppVersion metadata")
require("xmlns:app=\"clr-namespace:VcfEditor\"" in settings_xaml, "SettingsView does not import application constants")
require("{x:Static app:Constants.AppName}" in settings_xaml, "SettingsView does not bind AppName statically")
require("{x:Static app:Constants.AppVersion}" in settings_xaml, "SettingsView does not bind AppVersion statically")

parser = read("Core/VcfParser.cs")
require("private readonly ILogger<VcfParser> _logger;" in parser, "VcfParser does not own an instance logger")
require("public VcfParser(ILogger<VcfParser>? logger = null)" in parser, "VcfParser logger constructor is missing")
require("LogMessages.TruncatedVcf(_logger);" in parser, "VcfParser parsing methods still do not access instance state")
require("LogMessages.VcfExportCompleted(_logger," in parser, "VCF export still does not access instance state")

log_messages = read("Helpers/LogMessages.cs")
require("VcfExportCompleted" in log_messages, "Source-generated VCF export log event is missing")

drag_drop = read("Helpers/DragDropHelper.cs")
require("using System.Diagnostics.CodeAnalysis;" in drag_drop, "DragDropHelper is missing null-state annotations")
require("[NotNullWhen(true)] out string? vcfPath" in drag_drop, "Successful VCF drop does not prove a non-null path")
require("LogMessages.VcfFileDropped(_logger, filePath);" in drag_drop, "VCF drop logging call changed unexpectedly")
require("if (filePath != null)" not in drag_drop, "Redundant nullable guard remains after successful drop validation")

coordinator = read("Services/ShellConnectionCoordinator.cs")
dashboard = read("ViewModels/DashboardViewModel.cs")
contacts = read("Views/ContactsView.xaml.cs")
require("private void OnRetryPhoneConnectionRequested(object? sender, EventArgs e)" in coordinator,
        "Retry handler does not match EventHandler")
require("public event EventHandler? RetryPhoneConnectionRequested;" in dashboard,
        "Dashboard retry event is not EventHandler")
require("public event EventHandler? RetryPhoneConnectionRequested;" in contacts,
        "Contacts retry event is not EventHandler")
require("RetryPhoneConnectionRequested?.Invoke(this, EventArgs.Empty);" in dashboard,
        "Dashboard retry event invocation is inconsistent")
require("RetryPhoneConnectionRequested?.Invoke(this, EventArgs.Empty);" in contacts,
        "Contacts retry event invocation is inconsistent")

# Analyzer regression checks covering warnings that were reported but are already clean.
production_files = [
    path for path in ROOT.rglob("*.cs")
    if not any(part in {"bin", "obj", ".git", "AndroidCompanion"} for part in path.relative_to(ROOT).parts)
]
for path in production_files:
    text = path.read_text(encoding="utf-8-sig")
    rel = path.relative_to(ROOT)
    for match in re.finditer(r"new\s+(?:System\.Text\.Json\.)?JsonSerializerOptions\b", text):
        line_start = text.rfind("\n", 0, match.start()) + 1
        line_end = text.find("\n", match.start())
        if line_end == -1:
            line_end = len(text)
        line = text[line_start:line_end]
        if "static readonly" not in line:
            errors.append(f"{rel}: JsonSerializerOptions is not cached in a static readonly field")
    require("ConfirmOnExit = false" not in text and "ConfirmOnExit=false" not in text,
            f"{rel}: ConfirmOnExit is explicitly initialized to its default value")

if errors:
    print("PHASE 3 COMPILE-FIX VERIFICATION FAILED")
    for error in errors:
        print(f"- {error}")
    sys.exit(1)

print("PHASE 3 COMPILE-FIX VERIFICATION PASSED")
print("- static settings metadata bindings")
print("- instance-backed VCF parser operations")
print("- null-safe drag/drop logging")
print("- EventHandler-compatible retry events")
print("- cached JsonSerializerOptions and default-value cleanup")
