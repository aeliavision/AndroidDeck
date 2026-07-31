#!/usr/bin/env python3
"""Source-level regression checks for the Windows build blockers reported after Phase 3."""
from __future__ import annotations

from pathlib import Path
import re
import sys

ROOT = Path(__file__).resolve().parents[1]
errors: list[str] = []


def read(relative: str) -> str:
    return (ROOT / relative).read_text(encoding="utf-8-sig")


generator = read("tools/DesignTokenGenerator/Program.cs")
if "PropertyNamingPolicy = JsonNamingPolicy.CamelCase" not in generator:
    errors.append("Design-token JSON does not map camelCase document properties.")
if "UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow" not in generator:
    errors.append("Design-token JSON must continue rejecting unknown properties.")

for project in (
    "tests/DesignTokenGenerator.Tests/DesignTokenGenerator.Tests.csproj",
    "tests/VcfEditor.Core.Tests/VcfEditor.Core.Tests.csproj",
    "tests/VcfEditor.UI.Tests/VcfEditor.UI.Tests.csproj",
):
    if '<Using Include="Xunit" />' not in read(project):
        errors.append(f"{project} does not declare the xUnit global using required by [Fact].")

settings = read("Core/Settings/JsonAppSettingsStore.cs")
if "private static readonly JsonSerializerOptions JsonOptions" not in settings:
    errors.append("Settings JSON options are not cached in a static readonly field.")
if "JsonSerializer.Deserialize<SettingsModel>(json, JsonOptions)" not in settings:
    errors.append("Settings deserialization does not reuse the cached JsonOptions instance.")
if "JsonSerializer.Serialize(_settings, JsonOptions)" not in settings:
    errors.append("Settings serialization does not reuse the cached JsonOptions instance.")

# CA1805: reject only an explicit false/default initializer tied to ConfirmOnExit.
explicit_confirm_on_exit = re.compile(
    r"(?:\b_confirmOnExit\b|\bConfirmOnExit\b)\s*=\s*(?:false|default(?:\s*\(\s*bool\s*\))?)\s*;"
)
for path in ROOT.rglob("*.cs"):
    if any(part in {"bin", "obj"} for part in path.parts):
        continue
    source = path.read_text(encoding="utf-8-sig", errors="replace")
    if explicit_confirm_on_exit.search(source):
        errors.append(f"{path.relative_to(ROOT)} explicitly initializes ConfirmOnExit to its default value.")

# Main WPF assembly must not accidentally compile tests or tools.
main_project = read("VcfEditor.csproj")
for required_exclusion in (
    '<Compile Remove="tests\\**" />',
    '<Compile Remove="Tests\\**" />',
    '<Compile Remove="tools\\**" />',
):
    if required_exclusion not in main_project:
        errors.append(f"VcfEditor.csproj is missing source exclusion {required_exclusion}.")

if errors:
    print("Phase 3 Windows build-blocker verification FAILED:")
    for error in errors:
        print(f"- {error}")
    sys.exit(1)

print("Phase 3 Windows build-blocker verification passed.")
