#!/usr/bin/env python3
"""Static compile-adjacent checks that can run without the Windows .NET SDK."""
from __future__ import annotations

import re
import sys
from pathlib import Path

from lxml import etree

ROOT = Path(__file__).resolve().parents[1]
errors: list[str] = []


def strip_csharp(source: str) -> str:
    """Remove comments and literals while preserving newlines for delimiter checks."""
    out: list[str] = []
    i = 0
    length = len(source)

    while i < length:
        c = source[i]
        n = source[i + 1] if i + 1 < length else ""

        if c == "/" and n == "/":
            out.extend("  ")
            i += 2
            while i < length and source[i] != "\n":
                out.append(" ")
                i += 1
            continue

        if c == "/" and n == "*":
            out.extend("  ")
            i += 2
            while i < length:
                if source[i] == "*" and i + 1 < length and source[i + 1] == "/":
                    out.extend("  ")
                    i += 2
                    break
                out.append("\n" if source[i] == "\n" else " ")
                i += 1
            continue

        # C# raw string literals, including interpolated raw strings ($"""...""").
        raw_match = re.match(r"\$*\"{3,}", source[i:])
        if raw_match:
            token = raw_match.group(0)
            quote_count = len(token) - token.count("$")
            delimiter = '"' * quote_count
            out.extend(" " * len(token))
            i += len(token)
            end = source.find(delimiter, i)
            if end < 0:
                out.extend("\n" if ch == "\n" else " " for ch in source[i:])
                break
            out.extend("\n" if ch == "\n" else " " for ch in source[i:end + quote_count])
            i = end + quote_count
            continue

        # Verbatim strings: @"...", $@"...", @$"...". Doubled quotes escape quotes.
        verbatim_prefix = None
        for prefix in ("$@\"", "@$\"", "@\""):
            if source.startswith(prefix, i):
                verbatim_prefix = prefix
                break
        if verbatim_prefix is not None:
            out.extend(" " * len(verbatim_prefix))
            i += len(verbatim_prefix)
            while i < length:
                if source.startswith('""', i):
                    out.extend("  ")
                    i += 2
                    continue
                out.append("\n" if source[i] == "\n" else " ")
                if source[i] == '"':
                    i += 1
                    break
                i += 1
            continue

        # Normal and interpolated strings.
        string_prefix = None
        for prefix in ("$\"", "\""):
            if source.startswith(prefix, i):
                string_prefix = prefix
                break
        if string_prefix is not None:
            out.extend(" " * len(string_prefix))
            i += len(string_prefix)
            while i < length:
                if source[i] == "\\":
                    out.extend("  ")
                    i += 2
                    continue
                out.append("\n" if source[i] == "\n" else " ")
                if source[i] == '"':
                    i += 1
                    break
                i += 1
            continue

        if c == "'":
            out.append(" ")
            i += 1
            while i < length:
                if source[i] == "\\":
                    out.extend("  ")
                    i += 2
                    continue
                out.append("\n" if source[i] == "\n" else " ")
                if source[i] == "'":
                    i += 1
                    break
                i += 1
            continue

        out.append(c)
        i += 1

    return "".join(out)


pairs = {"{": "}", "(": ")", "[": "]"}
for path in ROOT.rglob("*.cs"):
    if any(part in {"bin", "obj", ".git"} for part in path.parts):
        continue
    clean = strip_csharp(path.read_text(encoding="utf-8-sig"))
    stack: list[tuple[str, int]] = []
    mismatch = False
    for pos, char in enumerate(clean):
        if char in pairs:
            stack.append((char, pos))
        elif char in pairs.values():
            if not stack or pairs[stack[-1][0]] != char:
                line = clean.count("\n", 0, pos) + 1
                errors.append(
                    f"delimiter mismatch: {path.relative_to(ROOT)}:{line} unexpected {char}"
                )
                mismatch = True
                break
            stack.pop()
    if not mismatch and stack:
        char, pos = stack[-1]
        line = clean.count("\n", 0, pos) + 1
        errors.append(f"delimiter mismatch: {path.relative_to(ROOT)}:{line} unclosed {char}")

# Every view XAML file must be well-formed and every routed-event handler must exist.
EVENT_ATTRIBUTES = {
    "Checked",
    "Click",
    "Closed",
    "Closing",
    "DragOver",
    "Drop",
    "IsVisibleChanged",
    "KeyDown",
    "KeyUp",
    "Loaded",
    "LostFocus",
    "MouseDoubleClick",
    "MouseLeftButtonDown",
    "MouseLeftButtonUp",
    "MouseMove",
    "MouseWheel",
    "PasswordChanged",
    "PreviewKeyDown",
    "PreviewKeyUp",
    "PreviewMouseDown",
    "PreviewMouseLeftButtonDown",
    "PreviewMouseMove",
    "PreviewMouseWheel",
    "PreviewTextInput",
    "SelectionChanged",
    "SizeChanged",
    "TextChanged",
    "Unchecked",
    "Unloaded",
}
method_pattern = re.compile(
    r"\b(?:private|protected|public|internal)\s+"
    r"(?:static\s+)?(?:async\s+)?"
    r"(?:void|Task(?:<[^>]+>)?|System\.Threading\.Tasks\.Task(?:<[^>]+>)?)\s+"
    r"([A-Za-z_][A-Za-z0-9_]*)\s*\("
)
for xaml in (ROOT / "Views").rglob("*.xaml"):
    raw = xaml.read_text(encoding="utf-8-sig")
    try:
        document = etree.fromstring(raw.encode("utf-8"))
    except etree.XMLSyntaxError as exc:
        errors.append(f"invalid XAML XML: {xaml.relative_to(ROOT)}: {exc}")
        continue

    handlers: set[str] = set()
    for element in document.iter():
        for attribute_name, value in element.attrib.items():
            local_name = attribute_name.rsplit("}", 1)[-1]
            # Attached routed events such as GridViewColumnHeader.Click retain the suffix.
            event_name = local_name.rsplit(".", 1)[-1]
            if event_name in EVENT_ATTRIBUTES and re.fullmatch(
                r"[A-Za-z_][A-Za-z0-9_]*", value
            ):
                handlers.add(value)

    if not handlers:
        continue

    codebehind = Path(str(xaml) + ".cs")
    if not codebehind.exists():
        errors.append(f"XAML handlers without code-behind: {xaml.relative_to(ROOT)}")
        continue

    methods = set(method_pattern.findall(codebehind.read_text(encoding="utf-8-sig")))
    for handler in sorted(handlers - methods):
        errors.append(f"missing XAML handler: {xaml.relative_to(ROOT)} -> {handler}")

# Constructor/DI contracts for Phase 4 scoped pages and focused collaborators.
hosting_path = ROOT / "Hosting/ServiceCollectionExtensions.cs"
if not hosting_path.exists():
    errors.append("missing Hosting/ServiceCollectionExtensions.cs")
else:
    hosting = hosting_path.read_text(encoding="utf-8-sig")
    for registration in (
        "AddScoped<IFileTransferWorkflow, FileTransferWorkflow>()",
        "AddScoped<ILocalUploadPlanner, LocalUploadPlanner>()",
        "AddScoped<IFileBrowserInteraction, FileBrowserInteraction>()",
        "AddScoped<IGalleryTransferWorkflow, GalleryTransferWorkflow>()",
        "AddScoped<IGalleryInteraction, GalleryInteraction>()",
        "AddScoped<IBackupWorkflow, BackupWorkflow>()",
        "AddScoped<IRestoreWorkflow, RestoreWorkflow>()",
        "AddScoped<IBackupHistoryService, BackupHistoryService>()",
        "AddScoped<IBackupArchiveService, BackupArchiveService>()",
    ):
        if registration not in hosting:
            errors.append(f"missing DI contract: {registration}")

if errors:
    print("Phase 4 compile-contract verification FAILED:")
    for error in errors:
        print(f" - {error}")
    sys.exit(1)

print("Phase 4 compile-contract verification passed.")
