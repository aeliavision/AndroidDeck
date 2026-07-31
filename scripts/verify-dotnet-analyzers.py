#!/usr/bin/env python3
from __future__ import annotations

import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
EXCLUDED = {"tests", "AndroidCompanion", "obj", "bin", ".git"}


def production_cs_files() -> list[Path]:
    files: list[Path] = []
    for path in ROOT.rglob("*.cs"):
        if any(part in EXCLUDED for part in path.relative_to(ROOT).parts):
            continue
        files.append(path)
    return sorted(files)


checks: list[tuple[str, re.Pattern[str], str]] = [
    ("CA1848", re.compile(r"\.Log(?:Information|Warning|Error|Debug|Critical)\s*\("), "Use source-generated LoggerMessage methods."),
    ("CA2016", re.compile(r"ReadToEndAsync\s*\(\s*\)"), "Forward the available CancellationToken."),
    ("CA1869", re.compile(r"new\s+(?:System\.Text\.Json\.)?JsonSerializerOptions\b"), "Reuse cached JsonSerializerOptions."),
    ("CA1872", re.compile(r"BitConverter\.ToString\s*\("), "Use Convert.ToHexStringLower."),
    ("CA1850", re.compile(r"SHA256\.Create\s*\(\s*\)"), "Use SHA256.HashData."),
    ("CA2201", re.compile(r"throw\s+new\s+Exception\s*\("), "Throw a specific exception type."),
    ("CA1513", re.compile(r"throw\s+new\s+ObjectDisposedException\s*\("), "Use ObjectDisposedException.ThrowIf."),
    ("CA1510", re.compile(r"throw\s+new\s+ArgumentNullException\s*\("), "Use ArgumentNullException.ThrowIfNull."),
    ("CA1840", re.compile(r"Thread\.CurrentThread\.ManagedThreadId"), "Use Environment.CurrentManagedThreadId."),
    ("CA1304", re.compile(r"\.ToUpper\s*\(\s*\)"), "Use ToUpperInvariant or an explicit culture."),
    ("CA1846", re.compile(r"\.Substring\s*\("), "Use ranges or spans instead of Substring."),
    ("CA1707", re.compile(r"\bX_(?:MOBILE|WORK|HOME|OTHER|CUSTOM)\b"), "Use analyzer-compliant enum identifiers."),
    ("CA1805", re.compile(r"\bConfirmOnExit\s*=\s*false\b"), "Remove explicit default initialization."),
    ("CALL001", re.compile(r"cancellationToken\s*,\s*cancellationToken\s*:"), "Do not pass the cancellation token twice."),
    ("CALL002", re.compile(r"cancellationToken\s*:\s*(?:content|signaturePath)\b"), "Named arguments must match their target parameter types."),
    ("CA1860", re.compile(r"\.Any\s*\(\s*\)"), "Compare Count to zero when the collection exposes Count."),
    ("CA1865", re.compile(r"\.(?:StartsWith|EndsWith)\s*\(\s*\".\"\s*\)"), "Use the char overload for a one-character comparison."),
]

issues: list[str] = []
for path in production_cs_files():
    rel = path.relative_to(ROOT)
    for line_number, line in enumerate(path.read_text(encoding="utf-8-sig").splitlines(), 1):
        stripped = line.strip()
        if stripped.startswith("//"):
            continue
        for rule, pattern, advice in checks:
            if rule == "CA1869" and "static readonly" in line:
                continue
            if pattern.search(line):
                issues.append(f"{rel}:{line_number}: {rule}: {advice}\n    {stripped}")

# Explicit architectural analyzer contracts that are not safely detected by a simple regex.
contracts = {
    "Core/HttpTransport.cs": [
        "HttpCompletionOption completionOption = HttpCompletionOption.ResponseHeadersRead,\n            CancellationToken cancellationToken = default)",
        "bool allowRetry,\n            CancellationToken cancellationToken)",
        "private static readonly JsonSerializerOptions ErrorJsonOptions",
        "ToString(CultureInfo.InvariantCulture)",
        "SHA256.HashData",
    ],
    "Core/ContactValidator.cs": [
        "public static partial class ContactValidator",
        "public static ValidationResult ValidateContact",
        "public static PhoneValidationResult ValidatePhoneNumber",
        "public static EmailValidationResult ValidateEmail",
        "PhoneNumbers.Count == 0",
        "errors.Count == 0",
        "StartsWith('+')",
    ],
    "Core/AdbHelper.cs": [
        "private static readonly char[] LineSeparators",
        "private static readonly char[] DeviceColumnSeparators",
        "ReadToEndAsync(cancellationToken)",
    ],
    "Core/BackupApi.cs": ["ToString(\"yyyy-MM-dd  HH:mm\", CultureInfo.CurrentCulture)"],
    "Core/ContactsApi.cs": [
        "FetchContactPhotoAsync(string androidId, CancellationToken cancellationToken = default)",
        "ReadAsStringAsync(cancellationToken)",
        "BoundedStreamCopy.ReadAllBytesAsync",
    ],
    "Core/VcfParser.cs": [
        "ToUpperInvariant()",
        "StringComparison.OrdinalIgnoreCase",
        "Append(line.AsSpan",
        "private static void ProcessProperty",
        "private static void ParseNameLine",
        "private static void ParsePhoneLine",
        "private static string GetVcfPhoneType",
    ],
    "Core/PhoneContactsClient.cs": [
        "FetchContactPhotoAsync(string androidId, CancellationToken cancellationToken = default)",
        "FetchContactPhotoAsync(androidId, cancellationToken)",
        "public void Dispose()",
        "public async ValueTask DisposeAsync()",
        "GC.SuppressFinalize(this)",
    ],
    "Core/GalleryApi.cs": [
        "BoundedStreamCopy.ReadAllBytesAsync",
        "TransferLimits.MaxThumbnailBytes",
    ],
    "Core/SessionManager.cs": ["public void Dispose()", "GC.SuppressFinalize(this)"],
    "Helpers/Logger.cs": [
        "Environment.CurrentManagedThreadId",
        "public void Dispose()",
        "GC.SuppressFinalize(this)",
    ],
    "Helpers/DragDropHelper.cs": ["private void ShowDropError", "IDialogService"],
    "Models/DTOs/ContactDto.cs": ["ToString(CultureInfo.InvariantCulture)"],
    "Models/DTOs/DtoMapper.cs": ["ToString(CultureInfo.InvariantCulture)"],
    "ViewModels/BackupViewModel.cs": [
        "INotifyPropertyChanged, IAsyncInitializable, IDisposable",
        "public void Dispose()",
        "_cts?.Cancel()",
        "_cts?.Dispose()",
        "GC.SuppressFinalize(this)",
        "ToString(CultureInfo.InvariantCulture)",
    ],
    "ViewModels/ContactsViewModel.cs": [
        "IDisposable",
        "private static void ExecuteOnUI",
        "public void Dispose()",
        "ReleaseFetchCancellation",
        "GC.SuppressFinalize(this)",
    ],
    "Views/ConnectPhoneDialog.xaml.cs": [
        "Window, IDisposable",
        "private static string GetDefaultApkPath",
        "Closed += (_, _) => Dispose()",
        "public void Dispose()",
        "_pairingCts?.Dispose()",
        "_phoneClient?.Dispose()",
        "GC.SuppressFinalize(this)",
    ],
    "Views/ShellWindow.xaml.cs": [
        "Window, IDisposable",
        "Closed += OnClosed",
        "public void Dispose()",
        "_viewModel.Dispose()",
        "_connectionCoordinator.Dispose()",
        "GC.SuppressFinalize(this)",
    ],
    "Services/ShellConnectionCoordinator.cs": [
        "IShellConnectionCoordinator",
        "private static ShellPhoneConnectionState MapToShellState",
        "public void Dispose()",
        "_phoneBannerCancellation?.Dispose()",
        "_capabilityCancellation?.Dispose()",
        "_pageFactory.Dispose()",
        "_phoneClient?.Dispose()",
        "GC.SuppressFinalize(this)",
    ],
}
for rel, required_fragments in contracts.items():
    text = (ROOT / rel).read_text(encoding="utf-8-sig")
    for fragment in required_fragments:
        if fragment not in text:
            issues.append(f"{rel}: contract: missing required fragment {fragment!r}")

negative_contracts = {
    "Views/ContactsView.xaml.cs": ["_vcfParser => new VcfParser()"],
    "Core/SessionManager.cs": ["DeviceNameReceived"],
    "Core/HttpTransport.cs": [
        "?? throw new InvalidOperationException(\"HttpTransport not initialised. Call EnsureInitialised() first.\");\n                        ?? throw",
    ],
}
for rel, forbidden_fragments in negative_contracts.items():
    text = (ROOT / rel).read_text(encoding="utf-8-sig")
    for fragment in forbidden_fragments:
        if fragment in text:
            issues.append(f"{rel}: contract: forbidden fragment present {fragment!r}")



# CA1068: CancellationToken belongs last in method declarations. Parse declaration-like
# signatures only; invocations are excluded by requiring an access modifier and body marker.
method_pattern = re.compile(
    r'(?m)^\s*(?:public|private|protected|internal)\s+'
    r'(?:static\s+|async\s+|sealed\s+|virtual\s+|override\s+|partial\s+|new\s+)*'
    r'[^=;{}\n]+?\s+\w+(?:<[^\n()]+>)?\s*\((.*?)\)\s*'
    r'(?:where[^\{=>]+)?(?:\{|=>)',
    re.DOTALL,
)
for path in production_cs_files():
    rel = path.relative_to(ROOT)
    text = path.read_text(encoding="utf-8-sig")
    for declaration in method_pattern.finditer(text):
        parameters_text = declaration.group(1)
        if "CancellationToken" not in parameters_text:
            continue
        parameters: list[str] = []
        start = 0
        depth = 0
        for index, char in enumerate(parameters_text):
            if char in "(<[": depth += 1
            elif char in ")>]": depth = max(0, depth - 1)
            elif char == ',' and depth == 0:
                parameters.append(parameters_text[start:index].strip())
                start = index + 1
        parameters.append(parameters_text[start:].strip())
        token_indexes = [index for index, parameter in enumerate(parameters) if "CancellationToken" in parameter]
        if token_indexes and token_indexes[-1] != len(parameters) - 1:
            line = text.count('\n', 0, declaration.start()) + 1
            issues.append(f"{rel}:{line}: CA1068: CancellationToken parameter must be last")

# Validate source-generated logging declarations and every call site. This catches
# missing event methods, duplicate event IDs, placeholder/signature drift, and
# incorrect argument counts without relying on a full SDK in the audit container.
log_messages_path = ROOT / "Helpers/LogMessages.cs"
log_text = log_messages_path.read_text(encoding="utf-8-sig")
log_pattern = re.compile(
    r'\[LoggerMessage\((\d+),\s*LogLevel\.\w+,\s*"((?:\\.|[^"\\])*)"\)\]\s*'
    r'internal\s+static\s+partial\s+void\s+(\w+)\s*\(([^)]*)\)\s*;',
    re.DOTALL,
)
log_definitions: dict[str, int] = {}
event_ids: set[str] = set()
for match in log_pattern.finditer(log_text):
    event_id, message, method_name, parameters_text = match.groups()
    if event_id in event_ids:
        issues.append(f"Helpers/LogMessages.cs: duplicate LoggerMessage event ID {event_id}")
    event_ids.add(event_id)
    parameters = [part.strip() for part in parameters_text.split(',') if part.strip()]
    log_definitions[method_name] = len(parameters)
    parameter_names = {
        re.split(r'\s+', parameter)[-1].lstrip('@').lower()
        for parameter in parameters
    }
    placeholders = {
        placeholder.lower()
        for placeholder in re.findall(r'\{([A-Za-z_][A-Za-z0-9_]*)', message)
    }
    missing = placeholders - parameter_names
    if missing:
        issues.append(
            f"Helpers/LogMessages.cs: {method_name} is missing parameters for placeholders: "
            + ", ".join(sorted(missing))
        )

if not log_definitions:
    issues.append("Helpers/LogMessages.cs: no source-generated LoggerMessage declarations found")


def find_closing_parenthesis(text: str, opening_index: int) -> int | None:
    depth = 0
    state = "code"
    i = opening_index
    while i < len(text):
        ch = text[i]
        nxt = text[i + 1] if i + 1 < len(text) else ""
        if state == "code":
            if ch == '/' and nxt == '/':
                state = "line_comment"; i += 2; continue
            if ch == '/' and nxt == '*':
                state = "block_comment"; i += 2; continue
            if ch == '@' and nxt == '"':
                state = "verbatim_string"; i += 2; continue
            if ch == '"':
                state = "string"; i += 1; continue
            if ch == "'":
                state = "char"; i += 1; continue
            if ch == '(':
                depth += 1
            elif ch == ')':
                depth -= 1
                if depth == 0:
                    return i
        elif state == "line_comment":
            if ch in "\r\n": state = "code"
        elif state == "block_comment":
            if ch == '*' and nxt == '/': state = "code"; i += 2; continue
        elif state == "string":
            if ch == '\\': i += 2; continue
            if ch == '"': state = "code"
        elif state == "verbatim_string":
            if ch == '"' and nxt == '"': i += 2; continue
            if ch == '"': state = "code"
        elif state == "char":
            if ch == '\\': i += 2; continue
            if ch == "'": state = "code"
        i += 1
    return None


def count_top_level_arguments(arguments: str) -> int:
    if not arguments.strip():
        return 0
    depth_round = depth_square = depth_curly = 0
    state = "code"
    commas = 0
    i = 0
    while i < len(arguments):
        ch = arguments[i]
        nxt = arguments[i + 1] if i + 1 < len(arguments) else ""
        if state == "code":
            if ch == '/' and nxt == '/': state = "line_comment"; i += 2; continue
            if ch == '/' and nxt == '*': state = "block_comment"; i += 2; continue
            if ch == '@' and nxt == '"': state = "verbatim_string"; i += 2; continue
            if ch == '"': state = "string"; i += 1; continue
            if ch == "'": state = "char"; i += 1; continue
            if ch == '(': depth_round += 1
            elif ch == ')': depth_round -= 1
            elif ch == '[': depth_square += 1
            elif ch == ']': depth_square -= 1
            elif ch == '{': depth_curly += 1
            elif ch == '}': depth_curly -= 1
            elif ch == ',' and depth_round == depth_square == depth_curly == 0: commas += 1
        elif state == "line_comment":
            if ch in "\r\n": state = "code"
        elif state == "block_comment":
            if ch == '*' and nxt == '/': state = "code"; i += 2; continue
        elif state == "string":
            if ch == '\\': i += 2; continue
            if ch == '"': state = "code"
        elif state == "verbatim_string":
            if ch == '"' and nxt == '"': i += 2; continue
            if ch == '"': state = "code"
        elif state == "char":
            if ch == '\\': i += 2; continue
            if ch == "'": state = "code"
        i += 1
    return commas + 1

for path in production_cs_files():
    if path == log_messages_path:
        continue
    rel = path.relative_to(ROOT)
    text = path.read_text(encoding="utf-8-sig")
    for call in re.finditer(r'LogMessages\.(\w+)\s*\(', text):
        method_name = call.group(1)
        opening = call.end() - 1
        closing = find_closing_parenthesis(text, opening)
        if closing is None:
            issues.append(f"{rel}: unterminated LogMessages.{method_name} invocation")
            continue
        if method_name not in log_definitions:
            issues.append(f"{rel}: unknown LogMessages method {method_name}")
            continue
        actual = count_top_level_arguments(text[opening + 1:closing])
        expected = log_definitions[method_name]
        if actual != expected:
            line = text.count('\n', 0, call.start()) + 1
            issues.append(
                f"{rel}:{line}: LogMessages.{method_name} expects {expected} argument(s), got {actual}"
            )

# Numeric formatting call sites named by the analyzer report must use an explicit culture.
for rel in ["ViewModels/BackupViewModel.cs", "Models/DTOs/DtoMapper.cs", "Models/DTOs/ContactDto.cs"]:
    text = (ROOT / rel).read_text(encoding="utf-8-sig")
    if re.search(r"(?:ItemCount|RestoredItems|SkippedItems|FailedItems|AndroidRawType\.Value|GetInt64\(\)|MapToAndroidPhoneType\([^)]*\))\.ToString\(\)", text):
        issues.append(f"{rel}: CA1305: numeric ToString must pass an IFormatProvider")

if issues:
    print(f".NET analyzer cleanup contract FAILED with {len(issues)} issue(s):")
    print("\n".join(issues))
    sys.exit(1)

print(".NET analyzer cleanup contract passed.")
