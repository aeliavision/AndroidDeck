#!/usr/bin/env python3
"""Regression checks for the second Windows build stabilization pass."""
from __future__ import annotations

from pathlib import Path
import re
import sys

ROOT = Path(__file__).resolve().parents[1]
errors: list[str] = []


def read(relative: str) -> str:
    return (ROOT / relative).read_text(encoding="utf-8-sig")


# CA1707: public xUnit method names must not contain underscores while warnings are errors.
method_pattern = re.compile(
    r"^\s*public\s+(?:async\s+)?(?:[\w<>,?\[\].]+\s+)+(?P<name>[A-Za-z_][A-Za-z0-9_]*)\s*\(",
    re.MULTILINE,
)
for path in sorted((ROOT / "tests").rglob("*.cs")):
    source = path.read_text(encoding="utf-8-sig", errors="replace")
    for match in method_pattern.finditer(source):
        name = match.group("name")
        if "_" in name:
            errors.append(f"{path.relative_to(ROOT)} exposes CA1707-invalid public test method {name}.")

main_project = read("VcfEditor.csproj")
if '<Compile Remove="Core\\AppSettingsStore.cs" />' not in main_project:
    errors.append("VcfEditor.csproj does not exclude the obsolete Core\\AppSettingsStore.cs source left by overwrite-style upgrades.")

settings = read("Core/Settings/JsonAppSettingsStore.cs")
if "private static readonly JsonSerializerOptions JsonOptions" not in settings:
    errors.append("JsonAppSettingsStore does not cache JsonSerializerOptions.")
if "JsonSerializer.Deserialize<SettingsModel>(json, JsonOptions)" not in settings:
    errors.append("JsonAppSettingsStore deserialization does not reuse JsonOptions.")
if "JsonSerializer.Serialize(_settings, JsonOptions)" not in settings:
    errors.append("JsonAppSettingsStore serialization does not reuse JsonOptions.")
if re.search(r"ConfirmOnExit\s*\{[^}]*\}\s*=\s*(?:false|default)", settings, re.DOTALL):
    errors.append("JsonAppSettingsStore explicitly initializes ConfirmOnExit to its default value.")

verify_windows = read("scripts/verify-windows.ps1")
if "verify-phase3-windows-build-fixes-v2.py" not in verify_windows:
    errors.append("verify-windows.ps1 does not run the second Windows build regression gate.")

if errors:
    print("PHASE 3 WINDOWS BUILD FIXES V2 VERIFICATION FAILED")
    for error in errors:
        print(f"- {error}")
    sys.exit(1)

print("PHASE 3 WINDOWS BUILD FIXES V2 VERIFICATION PASSED")
print("- CA1707-safe xUnit method names")
print("- stale AppSettingsStore exclusion")
print("- cached settings JSON options")
print("- default-value initialization cleanup")
