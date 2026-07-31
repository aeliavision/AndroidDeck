# AndroidDeck Phase 4 Completion Design

## Goal

Complete the Windows desktop architecture work required by Phase 4 of `Modernization_Plan.md` before beginning any Phase 5 visual redesign.

## Confirmed starting state

The current archive already has the Phase 3 shell/navigation redesign and some MVVM groundwork, but Phase 4 is incomplete: startup uses a manually built service provider, views and phone clients resolve dependencies through `App.Services`, phone pages are created with `ActivatorUtilities`, obsolete `MainWindow` files remain, direct message boxes bypass the dialog abstraction, and the main feature code-behind/view-model files remain oversized.

## Architecture

1. Use one .NET Generic Host as the composition root. The host owns logging, configuration, HTTP clients, application services, view models, and views, and is started before `ShellWindow` is resolved and stopped/disposed on application exit.
2. Keep app-lifetime pages in the root scope, but place phone-dependent pages and view models in a dedicated DI scope represented by `PhoneSessionScope`. Replacing or disconnecting a phone disposes the old scope and cancels its operations.
3. Centralize user notifications and confirmations in `IDialogService`. Feature views and coordinators call the abstraction; only `WpfDialogService` may call `MessageBox.Show`.
4. Move reusable file/contact/gallery/backup responsibilities into focused workflow services. View models remain state coordinators. Code-behind retains only WPF-specific selection, focus, drag/drop, and native-event bridging.
5. Add a Phase 4 source-verification gate and integrate it into Windows verification. The gate must fail for service-location, manual providers, obsolete MainWindow files, direct message boxes outside the dialog service, helper `async void` methods, or exit-gate line-count regressions.

## Lifecycle

- `App.OnStartup` builds and starts the host, initializes the compatibility logging bridge from the host-owned `ILoggerFactory`, applies the persisted/system theme, installs exception handling, resolves `ShellWindow`, and shows it.
- `App.OnExit` stops and disposes the host exactly once. The compatibility logging bridge releases its reference without disposing the host-owned factory.
- `PageFactory.SetPhoneClient` disposes the prior `PhoneSessionScope`, creates a new scope when a client is supplied, and registers the scoped views in navigation.
- All scoped feature view models implement deterministic disposal and cancel active work.

## Error handling

- Expected user-facing failures are converted to `IDialogService.ShowError`, `ShowWarning`, or structured view-model state.
- Unhandled dispatcher exceptions are passed through `IApplicationExceptionHandler`; fatal exceptions remain unhandled and trigger shutdown.
- Fire-and-forget event bridges delegate to named `Task` methods and log failures.

## Verification

- Static Phase 4 gate runs on every Windows verification.
- Existing xUnit projects remain the behavioral test home; architecture checks are additionally represented as source contract tests where practical.
- Final Windows proof requires `dotnet restore`, `dotnet build`, and `dotnet test` on a Windows machine with the SDK pinned in `global.json` because WPF cannot be compiled in this Linux container.
