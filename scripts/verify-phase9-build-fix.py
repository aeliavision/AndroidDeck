from pathlib import Path
import sys

ROOT = Path(__file__).resolve().parents[1]
errors: list[str] = []

def read(path: str) -> str:
    return (ROOT / path).read_text(encoding="utf-8-sig")

def require(condition: bool, message: str) -> None:
    if not condition:
        errors.append(message)

paged = read("Core/Paging/PagedFetch.cs")
require("ThrowIfLessThan(initialPage, 1)" in paged, "initialPage must use CA1512 throw helper")
require("ThrowIfLessThan(maxPages, 1)" in paged, "maxPages must use CA1512 throw helper")

incomplete = read("Core/Paging/IncompletePagedResultException.cs")
require("CultureInfo.InvariantCulture" in incomplete, "pagination messages must format page numbers invariantly")

polling = read("Core/Polling/OperationPollingPolicy.cs")
for marker in (
    "ThrowIfLessThan(interval, TimeSpan.Zero)",
    "ThrowIfLessThanOrEqual(timeout, TimeSpan.Zero)",
    "ThrowIfLessThan(maxAttempts, 1)",
):
    require(marker in polling, f"polling validation missing {marker}")

bounded = read("Core/IO/BoundedStreamCopy.cs")
require(bounded.count("ThrowIfNegative(limitBytes)") == 2, "bounded stream limit checks must use ThrowIfNegative")
require("ThrowIfLessThan(bufferSize, 1)" in bounded, "buffer size must use ThrowIfLessThan")

contacts = read("Features/Contacts/ContactEditorWorkflow.cs")
require("private Contact[] _selectedContacts" in contacts, "contact selection must use concrete array storage")
files = read("Features/Files/FileBrowserInteraction.cs")
require("private FileEntryDto[] _selectedEntries" in files, "file selection must use concrete array storage")
planner = read("Features/Files/LocalUploadPlanner.cs")
require("private List<LocalUploadItem> Build(" in planner, "local upload builder must return concrete List")
gallery = read("Features/Gallery/GalleryInteraction.cs")
require("private GalleryMediaItem[] SelectedItems()" in gallery, "gallery selection helper must return array")
presentation = read("Features/Gallery/GalleryViewPresentation.cs")
require("private GalleryMediaItem[] GetVisibleMediaItems()" in presentation, "visible gallery helper must return array")
require("if (visibleItems.Length == 0)" in presentation,
        "gallery visible-item array must use Length rather than the LINQ Count method group")
require("visibleItems.Count == 0" not in presentation,
        "gallery presentation still compares a Count method group to an integer")

paired = read("Models/PairedDeviceRecord.cs")
require(paired.count("CultureInfo.CurrentCulture") >= 2, "paired-device dates must use explicit culture")

diagnostics = read("Services/Settings/DiagnosticExportService.cs")
require(diagnostics.count("AppendLine(CultureInfo.InvariantCulture") >= 6, "diagnostic interpolated lines must use invariant culture")

monitor = read("Services/Performance/UiThreadStallMonitor.cs")
require("Invoke(StartCore, DispatcherPriority.Send, cancellationToken)" in monitor, "start dispatcher call must propagate cancellation")
require("Invoke(StopCore, DispatcherPriority.Send, cancellationToken)" in monitor, "stop dispatcher call must propagate cancellation")

dialog = read("Views/AppDialogWindow.cs")
require("using System.Windows.Documents;" in dialog, "TextElement namespace import is missing")
require("protected static void ApplyDialogStyle" in dialog, "ApplyDialogStyle must be static")
require("protected static TextBlock CreateValidationSummary" in dialog, "CreateValidationSummary must be static")
require("TextBlock? summary" in dialog, "dialog validation summary must be nullable-safe")

connect = read("Views/ConnectPhoneDialog.xaml.cs")
require("using System.Windows.Media;" in connect, "Brush namespace import is missing")
require('FindResource("Brush.Border")' in connect, "connection dialog must use generated border brush key")
require('FindResource("Brush.Error")' in connect, "connection dialog must use generated error brush key")

phone = read("Views/PhoneNumberDialog.cs")
require("existingPhone.Number ?? string.Empty" in phone, "phone cloning must be null-safe")
require("(_numberInput.Text ?? string.Empty).Trim()" in phone, "phone acceptance must be null-safe")

# 3.0.2 compile-contract regressions reported by the native Windows build.
require("IReadOnlyList<TItem> items = itemsSelector(page)" in paged,
        "paged items must have an explicit IReadOnlyList type so Count is a property, not a method group")
require("private static void NormalizeButtons" in dialog,
        "NormalizeButtons must be static for CA1822")

contacts_vm = read("ViewModels/ContactsViewModel.cs")
require("public ContactsViewModel(\n            VcfParser parser" in contacts_vm,
        "legacy VcfParser constructor compatibility overload is missing")
drag_drop = read("Helpers/DragDropHelper.cs")
require("NonInteractiveDialogService.Instance" in drag_drop,
        "legacy DragDropHelper constructor compatibility overload is missing")
edit_dialog = read("Views/EditContactDialog.xaml.cs")
require("public EditContactDialog(Contact contact)" in edit_dialog,
        "legacy EditContactDialog constructor compatibility overload is missing")
require("public PhoneNumberDialog()" in phone,
        "legacy parameterless PhoneNumberDialog constructor is missing")
require("public PhoneNumberDialog(PhoneNumber existingPhone)" in phone,
        "legacy PhoneNumberDialog edit constructor is missing")
require((ROOT / "Services/NonInteractiveDialogService.cs").exists(),
        "non-interactive compatibility dialog service is missing")


services = read("Hosting/ServiceCollectionExtensions.cs")
require("AddSingleton<ContactsViewModel>(provider => new ContactsViewModel(" in services,
        "ContactsViewModel must use an explicit DI factory to avoid ambiguous constructor activation")
require("GetRequiredService<IContactFileWorkflow>()" in services,
        "ContactsViewModel DI factory must select IContactFileWorkflow")
require("AddScoped(provider => provider.GetRequiredService<PhoneSessionContext>().Client)" in services,
        "PhoneApiClient must be registered from the initialized phone-session context")

if errors:
    print("PHASE 9 WINDOWS BUILD FIX VERIFICATION FAILED")
    for error in errors:
        print(f"- {error}")
    sys.exit(1)

print("Phase 9 Windows build-fix verification passed.")
