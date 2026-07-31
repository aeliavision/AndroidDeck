# AndroidDeck Phase 4 Completion Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [x]`) syntax for tracking.

**Goal:** Complete the Phase 4 Generic Host, DI/session lifecycle, MVVM workflow, dialog, and verification exit gates before Phase 5 begins.

**Architecture:** One Generic Host owns app-lifetime services. Phone-dependent pages are created inside one disposable DI scope per active phone session. UI notifications flow through `IDialogService`; feature logic moves into focused services and async commands while WPF-only behavior remains in code-behind.

**Tech Stack:** .NET 10 WPF, Microsoft.Extensions.Hosting/DependencyInjection/Logging, CommunityToolkit.Mvvm, xUnit, Python source-contract verification.

## Global Constraints

- Do not begin Phase 5 visual redesign work.
- Do not add a second service provider or service locator.
- Preserve the existing local-first phone protocol and Phase 3 navigation behavior.
- Keep `ShellWindow.xaml.cs` below 80 lines.
- Keep Contacts code-behind below 150 lines, File Browser below 120 lines, and Gallery below 150 lines.
- Only `Services/WpfDialogService.cs` may call `MessageBox.Show`.
- The uploaded archive has no Git history; record changes in the plan and final report instead of creating commits.

---

### Task 1: Add the Phase 4 failing architecture gate

**Files:**
- Create: `scripts/verify-phase4.py`
- Modify: `scripts/verify-windows.ps1`

**Interfaces:**
- Produces: command `python scripts/verify-phase4.py`, returning nonzero when any Phase 4 exit-gate rule is violated.

- [x] Write checks for Generic Host usage, removal of `App.Services`, removal of obsolete MainWindow files, scoped phone-page construction, direct message boxes, helper `async void`, code-behind limits, and focused workflow-service presence.
- [x] Run the script and confirm it fails against the current archive for the expected Phase 4 defects.
- [x] Add the gate to `verify-windows.ps1` before build/test.

### Task 2: Replace manual startup DI with the Generic Host

**Files:**
- Create: `Hosting/ServiceCollectionExtensions.cs`
- Modify: `App.xaml.cs`
- Modify: `Helpers/Logger.cs`

**Interfaces:**
- Produces: `IServiceCollection AddAndroidDeckDesktop(this IServiceCollection services, IConfiguration configuration)`.

- [x] Register all application services, views, view models, HTTP clients, logging dependencies, and phone-session services in the extension.
- [x] Build with `Host.CreateDefaultBuilder()`, enable `ValidateOnBuild` and `ValidateScopes`, start before resolving `ShellWindow`, and synchronously stop/dispose on exit.
- [x] Make the compatibility logger bridge non-owning so the host remains the only owner of `ILoggerFactory`.
- [x] Run the Phase 4 gate and confirm the startup/service-locator failures are removed.

### Task 3: Remove obsolete MainWindow implementation and service-locator constructors

**Files:**
- Delete: `Views/MainWindow.xaml`
- Delete: `Views/MainWindow.xaml.cs`
- Delete: `ViewModels/MainWindowViewModel.cs`
- Modify: `VcfEditor.csproj`
- Modify: `Core/PhoneApiClient.cs`
- Modify: `Core/PhoneContactsClient.cs`
- Modify: `Views/ContactsView.xaml.cs`
- Modify: `Views/SettingsView.xaml.cs`
- Modify: `Views/ConnectPhoneDialog.xaml.cs`

**Interfaces:**
- Phone clients require `IAppSettingsStore` explicitly.
- Views require their view model/services explicitly through constructors.

- [x] Delete obsolete files and compile-removal entries.
- [x] Remove all parameterless constructors that resolve through `App.Services`.
- [x] Update all creation sites to pass dependencies from DI or the dialog service.
- [x] Run the Phase 4 gate and confirm no service-locator reference remains.

### Task 4: Introduce a disposable phone-session scope

**Files:**
- Create: `Features/PhoneSession/PhoneSessionContext.cs`
- Create: `Features/PhoneSession/IPhoneSessionScopeFactory.cs`
- Create: `Features/PhoneSession/PhoneSessionScope.cs`
- Create: `Features/PhoneSession/PhoneSessionScopeFactory.cs`
- Modify: `Services/PageFactory.cs`
- Modify: `Services/IPageFactory.cs`
- Modify: `Services/ShellConnectionCoordinator.cs`
- Modify: `Hosting/ServiceCollectionExtensions.cs`
- Modify: phone-feature view constructors

**Interfaces:**
- `PhoneSessionScope IPhoneSessionScopeFactory.Create(PhoneApiClient client)`.
- `PhoneSessionContext.Client` exposes the initialized client and `Capabilities` exposes the current snapshot.
- `IPageFactory.UpdateCapabilities(ShellCapabilitySnapshot capabilities)` updates the active context.

- [x] Register phone APIs, view models, and views as scoped.
- [x] Replace `ActivatorUtilities` and root `IServiceProvider` usage in `PageFactory` with `IPhoneSessionScopeFactory`.
- [x] Dispose old scope on reconnect/disconnect and propagate capabilities into the context.
- [x] Ensure all feature view models are disposed by the scope.
- [x] Run the Phase 4 gate.

### Task 5: Centralize dialogs and user notifications

**Files:**
- Modify: `Services/IDialogService.cs`
- Modify: `Services/WpfDialogService.cs`
- Modify: `Services/ApplicationExceptionHandler.cs`
- Modify: `Services/ShellConnectionCoordinator.cs`
- Modify: feature views/dialogs containing `MessageBox.Show`

**Interfaces:**
- Add `ShowInformation`, `ShowWarning`, and `ShowError`.
- Retain `Confirm` and existing typed backup-dialog operations.

- [x] Add generic notification methods to the abstraction.
- [x] Inject/use the abstraction everywhere outside `WpfDialogService`.
- [x] Preserve owner-window behavior in `WpfDialogService`.
- [x] Run the Phase 4 gate and confirm no direct message box remains elsewhere.

### Task 6: Extract focused workflow collaborators and reduce code-behind

**Files:**
- Create focused services under `Features/Contacts`, `Features/Files`, `Features/Gallery`, and `Features/Backup`.
- Modify matching view models, views, and XAML command bindings.

**Interfaces:**
- Contacts: file workflow and selection/editor workflow.
- Files: transfer/selection workflow.
- Gallery: transfer/selection workflow.
- Backup: backup, restore, and history collaborators.

- [x] Move reusable file parsing/saving, transfer, polling, and archive operations from large view models to focused services.
- [x] Move user actions into async commands or controller methods invoked by minimal event bridges.
- [x] Convert helper `async void` methods to `Task`; keep only true WPF event handlers as `async void`.
- [x] Reduce feature code-behind to the Phase 4 limits without hiding business logic in partial code-behind files.
- [x] Run the Phase 4 gate.

### Task 7: Final verification and package

**Files:**
- Modify: `Modernization_Plan.md`
- Create: `PHASE_4_COMPLETION_REPORT.md`
- Create: updated project ZIP outside the source folder.

**Interfaces:**
- Produces: a source-verification-clean Phase 4 archive and Windows commands for final native build proof.

- [x] Run all available Python/static checks and XML parsing.
- [x] Run .NET build/tests when an SDK is available; otherwise record the environmental limitation without claiming native build success.
- [x] Mark completed Phase 4 checklist items and add completion evidence to the modernization plan.
- [x] Create the completion report and package the clean source tree.


## Execution Result

- All seven tasks were implemented in the supplied source archive.
- Git commits/worktrees were not possible because the archive contains no `.git` directory.
- All available Python architecture, analyzer, navigation, resource, XAML, and compile-contract checks pass.
- Native `dotnet build` and `dotnet test` remain a Windows-side certification step because this environment has no .NET SDK and cannot build WPF.
