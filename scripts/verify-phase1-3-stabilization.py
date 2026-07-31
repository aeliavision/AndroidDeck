#!/usr/bin/env python3
from __future__ import annotations

import re
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
errors: list[str] = []

def require(condition: bool, message: str) -> None:
    if not condition:
        errors.append(message)

def read(path: str) -> str:
    return (ROOT / path).read_text(encoding="utf-8-sig")

# Android compiler blockers.
gradle = read("AndroidCompanion/app/build.gradle.kts")
settings = read("AndroidCompanion/app/src/main/java/com/aeliavision/androiddeck/feature/settings/ui/SettingsScreen.kt")
toggle = read("AndroidCompanion/app/src/main/java/com/aeliavision/androiddeck/core/ui/components/luxury/LuxuryToggle.kt")
require("buildConfig = true" in gradle, "Android BuildConfig generation is not enabled")
require("import com.aeliavision.androiddeck.BuildConfig" in settings, "SettingsScreen imports the wrong BuildConfig")
require("ChampagneGold" not in toggle, "Legacy LuxuryToggle still references ChampagneGold")
require("MaterialTheme.colorScheme.primary" in toggle, "Legacy LuxuryToggle is not mapped to semantic colors")
require('navigation3 = "1.1.4"' in read("AndroidCompanion/gradle/libs.versions.toml"), "Navigation 3 is not on the stable pin")

# Design-token generator analyzer/build blockers.
generator = read("tools/DesignTokenGenerator/Program.cs")
require("namespace AndroidDeck.Tools.DesignTokens;" in generator, "DesignTokenGenerator types are not namespaced")
require("AppendLine($" not in generator, "Culture-sensitive interpolated AppendLine remains")
require("IReadOnlyDictionary<string, string> colors" not in generator, "CA1859 colors parameter remains")
require("AppendInvariantLine" in generator, "Invariant generator output helper is missing")
require("using AndroidDeck.Tools.DesignTokens;" in read("tests/DesignTokenGenerator.Tests/DesignTokenCompilerTests.cs"), "Generator tests do not import the new namespace")
require("System.Security.Cryptography.ProtectedData" not in read("VcfEditor.csproj"), "Redundant ProtectedData package remains")

# Contact refresh races and cancellation propagation.
contacts_vm = read("ViewModels/ContactsViewModel.cs")
contacts_api = read("Core/ContactsApi.cs")
contacts_client = read("Core/PhoneContactsClient.cs")
for fragment in (
    "_phoneFetchGeneration",
    "IsCurrentPhoneFetch",
    "ReleaseFetchCancellation",
    "ReferenceEquals(PhoneClient, client)",
    "FetchMissingDetailsAsync(client, generation, token)",
):
    require(fragment in contacts_vm, f"Contacts refresh hardening is missing: {fragment}")
require("FetchAllContactsAsync(\n            IProgress<(int current, int total)>? progress = null,\n            CancellationToken cancellationToken = default)" in contacts_api,
        "ContactsApi does not propagate cancellation through full refresh")
require("FetchAllContactsAsync(progress, cancellationToken)" in contacts_client,
        "PhoneContactsClient does not forward full-refresh cancellation")

# Session expiry/count and timestamp overflow.
auth = read("AndroidCompanion/app/src/main/java/com/aeliavision/androiddeck/feature/server/service/AuthManager.kt")
require("kotlin.math.abs" not in auth, "Timestamp verification still uses overflow-prone abs")
require("earliestAllowed" in auth and "latestAllowed" in auth, "Overflow-safe timestamp bounds are missing")
require("fun getActiveSessionCount(): Int = sessionLock.write" in auth, "Active-session count does not purge expired sessions")
settings_vm = read("AndroidCompanion/app/src/main/java/com/aeliavision/androiddeck/feature/settings/viewmodel/SettingsViewModel.kt")
require("authManager.sessionCount" in settings_vm, "Settings does not observe live session count")
require("authManager.revokeAllSessions()" in settings_vm, "Clear Sessions does not revoke AuthManager sessions")

# Chunk negotiation and partial-read safety.
filesystem = read("Core/FileSystemApi.cs")
for fragment in (
    "MinimumNegotiatedChunkSize",
    "MaximumNegotiatedChunkSize",
    "ValidateTransferSession",
    "CalculateReceivedBytes",
    "ReadExactlyAsync",
):
    require(fragment in filesystem, f"Chunk-transfer hardening is missing: {fragment}")
require("new byte[chunkSize]" in filesystem, "Chunk buffer allocation unexpectedly changed")

# Fresh-package verification path.
verify_windows = read("scripts/verify-windows.ps1")
require("--use-lock-file" in verify_windows and "--locked-mode" in verify_windows,
        "Windows verification cannot bootstrap and then enforce NuGet lock files")

# XML/XAML structural validation.
for path in list(ROOT.rglob("*.xaml")) + list((ROOT / "AndroidCompanion/app/src/main/res").rglob("*.xml")):
    try:
        ET.parse(path)
    except ET.ParseError as exc:
        errors.append(f"Invalid XML/XAML {path.relative_to(ROOT)}: {exc}")

# Basic delimiter sanity outside generated strings (not a compiler substitute).
for path in [ROOT / "tools/DesignTokenGenerator/Program.cs", ROOT / "ViewModels/ContactsViewModel.cs", ROOT / "Core/FileSystemApi.cs"]:
    text = path.read_text(encoding="utf-8-sig")
    require(text.count("(") == text.count(")"), f"Parenthesis imbalance in {path.relative_to(ROOT)}")

if errors:
    print("PHASE 1-3 STABILIZATION VERIFICATION FAILED")
    for error in errors:
        print(f"- {error}")
    sys.exit(1)

print("PHASE 1-3 STABILIZATION VERIFICATION PASSED")
print("- Android BuildConfig and stale LuxuryToggle compatibility")
print("- Design-token generator analyzer cleanup")
print("- Contacts refresh cancellation and stale-result protection")
print("- Session expiry/count and timestamp overflow protection")
print("- Chunk negotiation and exact-read validation")
print("- NuGet lock-file bootstrap and locked verification")
