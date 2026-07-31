# Phase 5 Desktop Modernization Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [x]`) syntax for tracking.

**Goal:** Modernize every Windows desktop screen into a responsive, accessible, theme-consistent experience with explicit loading, empty, error, disconnected, and success states.

**Architecture:** Build shared semantic resources and a reusable responsive-breakpoint behavior first, then modernize each screen as a vertical slice. Keep business workflows in view models/services, code-behind limited to WPF-specific interaction, and use the generated design-token palettes as the only color source.

**Tech Stack:** .NET 10, WPF, CommunityToolkit.Mvvm 8.4.2, Microsoft.Extensions.Hosting/DI, xUnit, FluentAssertions, generated XAML design tokens.

## Global Constraints

- Desktop target remains `net10.0-windows` with nullable reference types and warnings as errors.
- Keep the existing Generic Host composition root and session-scoped phone features.
- No raw hexadecimal colors in desktop views or shared hand-authored theme dictionaries.
- Every major action must be command-bound or delegated to a typed interaction service.
- Every screen must expose meaningful loading, empty, error, disconnected, and content states where applicable.
- Light, dark, system, and Windows high-contrast modes must share one semantic resource catalog.
- Minimum interactive target height is 44 pixels; primary navigation target remains 48 pixels.
- Preserve RTL contact data, keyboard navigation, and screen-reader labels.

---

### Task 1: Phase 5 shared presentation foundation

**Files:**
- Create: `Behaviors/ResponsiveLayoutBehavior.cs`
- Create: `Models/ResponsiveLayoutMode.cs`
- Create: `Themes/Phase5.xaml`
- Modify: `App.xaml`
- Modify: `Themes/Generated.Colors.Light.xaml`
- Modify: `Themes/Generated.Colors.Dark.xaml`
- Modify: `tools/DesignTokenGenerator/Program.cs`
- Test: `scripts/verify-phase5.py`

- [x] Write a failing Phase 5 source gate.
- [x] Add responsive compact/medium/expanded attached state.
- [x] Add semantic status, operation-center, dialog, command-bar, card, and media resources.
- [x] Generate `Brush.MediaScrim` from design tokens.
- [x] Verify all XAML resource references resolve.

### Task 2: Dashboard vertical slice

**Files:**
- Create: `ViewModels/DashboardViewModel.cs`
- Modify: `Views/DashboardView.xaml`
- Modify: `Views/DashboardView.xaml.cs`
- Modify: `Services/PageFactory.cs`
- Modify: `Services/ShellConnectionCoordinator.cs`
- Modify: `Hosting/ServiceCollectionExtensions.cs`
- Test: `tests/VcfEditor.UI.Tests/DashboardViewModelTests.cs`

- [x] Move dashboard state and commands out of the shell/view events.
- [x] Add responsive metric cards, primary/secondary connection actions, adaptive quick actions, recent-activity timestamps, and explicit unavailable values.
- [x] Add accessible headings and automation names.

### Task 3: Contacts workspace

**Files:**
- Modify: `Views/ContactsView.xaml`
- Modify: `Views/ContactsView.xaml.cs`
- Modify: `ViewModels/ContactsViewModel.cs`
- Modify: `ViewModels/ContactsViewModel.cs` with focused source/search/empty-state properties

- [x] Create an adaptive list/detail workspace.
- [x] Consolidate commands into search/filter/new/overflow.
- [x] Add source, empty, search, permission, and connection states.
- [x] Preserve keyboard multi-selection and meaningful row automation names.

### Task 4: File Browser and operation center

**Files:**
- Modify: `Views/FileBrowserView.xaml`
- Modify: `Views/FileBrowserView.xaml.cs`
- Modify: `ViewModels/FileBrowserViewModel.cs`
- Create: `Models/TransferOperationItem.cs`

- [x] Standardize breadcrumb/search/view/more command bar.
- [x] Add virtualized list/grid, inline permission/connection errors, selection preservation, and non-blocking cancel/retry transfer outcomes.

### Task 5: Gallery

**Files:**
- Modify: `Views/GalleryView.xaml`
- Modify: `Views/GalleryView.xaml.cs`
- Modify: `ViewModels/GalleryViewModel.cs`

- [x] Add adaptive album/gallery/preview panes.
- [x] Consolidate contextual commands and simplify tiles.
- [x] Add empty-album state, incremental loading state, and cancellation-safe preview/thumbnail presentation.

### Task 6: Backup and Restore

**Files:**
- Modify: `Views/BackupView.xaml`
- Modify: `ViewModels/BackupViewModel.cs`
- Modify: `ViewModels/BackupViewModel.cs` with focused preflight and result properties

- [x] Add scope, permission, destination, estimate, encryption, and restore-warning summaries.
- [x] Replace fixed actions/stepper/history columns with responsive semantic presentation.
- [x] Surface per-item conflicts and final verifiable restore summary without secrets.

### Task 7: Settings and security

**Files:**
- Modify: `Core/Settings/IAppSettingsStore.cs`
- Modify: `Core/Settings/JsonAppSettingsStore.cs`
- Modify: `ViewModels/SettingsViewModel.cs`
- Modify: `Views/SettingsView.xaml`
- Create: `Services/Settings/IUserNotificationService.cs`
- Implement: `Services/Settings/IUserNotificationService.cs`
- Create: `Services/Settings/IDiagnosticExportService.cs`
- Create: `Services/Settings/DiagnosticExportService.cs`
- Create: `Models/PairedDeviceRecord.cs`
- Test: `tests/VcfEditor.Core.Tests/JsonAppSettingsStoreTests.cs`
- Test: `tests/VcfEditor.UI.Tests/SettingsViewModelTests.cs`

- [x] Persist System/Light/Dark and compact-sidebar preferences asynchronously.
- [x] Replace handwritten command/timer code with toolkit commands and injectable notification delay.
- [x] Add paired-device security list/revoke and redacted diagnostic export.

### Task 8: Dialog consistency and final source of trust

**Files:**
- Create: `Views/AppDialogWindow.cs`
- Modify: all `Views/*Dialog*`, `MoveDialog.cs`, `RenameDialog.cs`, `TextInputDialog.cs`
- Modify: `Services/WpfDialogService.cs`
- Modify: `scripts/verify-windows.ps1`
- Modify: `Modernization_Plan.md`
- Create: `PHASE_5_COMPLETION_REPORT.md`

- [x] Standardize dialog ownership, safe default focus, Escape, validation, automation names, and minimum widths.
- [x] Eliminate raw colors and non-semantic theme references from desktop views.
- [x] Run Phase 3, Phase 4, Phase 5, resource, XAML, and available tests.
- [x] Package the completed Phase 5 source.

## Execution record

- Implemented inline against the supplied Phase 4 archive on July 29, 2026.
- The archive contained no `.git`, so no worktree or per-task commits could be created.
- Implementation deviations from the initial file map were responsibility-preserving: state-only helper models were kept as focused observable properties when no reusable domain type was needed.
- Source verification is recorded in `PHASE_5_COMPLETION_REPORT.md`; native Windows build/test and interactive matrix certification remain required.
