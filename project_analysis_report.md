# AndroidDeck — Full Project Analysis Report
**Generated:** 2026-07-31 | **Analyst:** Antigravity (superpowers skills)

---

## 1. Executive Summary

**AndroidDeck** is a dual-platform, **Windows-only** desktop + Android companion system for secure, local (no-cloud) management of Android contacts, files, media, and backups. The Windows side is a WPF/.NET 10 application; the Android side is a Kotlin/Compose companion app that runs an embedded HTTP server.

| Dimension | Status |
|---|---|
| Main app build | ✅ **Succeeds** (0 warnings, 0 errors) |
| Test suite build | ⚠️ **17 errors** — stale test project references (`.csproj` points to `tests/` not `Tests/`) |
| Architecture | ✅ Well-structured MVVM + DI |
| Security model | ✅ ECDH key exchange, HMAC signing, TOFU cert-pinning, DPAPI secrets |
| Test coverage | ⚠️ Unit tests exist but can't run due to project config issues |
| Documentation | ✅ README, USER_GUIDE, UI plan present |
| Version | 3.0.6 (WPF) / 3.0.0 (Android) |

---

## 2. Repository Layout

```
AndroidDeck/
├── AndroidDeck.csproj          # WPF app (net10.0-windows)
├── VcfEditor.sln               # Solution — includes 5 sub-projects
├── App.xaml / App.xaml.cs      # Entry point, DI host bootstrap
├── Constants.cs                # All magic values centralised
├── Core/                       # Network + parsing + infrastructure
├── Features/                   # Vertical feature slices
├── Services/                   # App-level services (navigation, DI, dialogs)
├── ViewModels/                 # CommunityToolkit.Mvvm view-models
├── Views/                      # XAML + code-behind
├── Navigation/                 # Shell routing policy
├── Models/                     # Domain models (Contact, PhoneNumber, DTOs)
├── Hosting/                    # DI registration extension
├── Behaviors/                  # WPF attached behaviors
├── Helpers/                    # Converters, logging, utilities
├── Themes/                     # Design system: colors, typography, controls
├── Tests/                      # 4 test sub-projects
├── VcfEditor.Tests/            # Legacy small test project (3 files)
└── AndroidCompanion/           # Full Kotlin/Compose Android sub-project
```

---

## 3. Technology Stack

### Windows Desktop (WPF)

| Layer | Technology |
|---|---|
| Framework | .NET 10.0-windows, WPF |
| MVVM | CommunityToolkit.Mvvm 8.4.2 |
| DI / Hosting | Microsoft.Extensions.Hosting 10.0.10 + DI 10.0.10 |
| HTTP | System.Net.Http (HttpClient factory + SocketsHttpHandler) |
| Validation | FluentValidation 12.1.1 |
| Filesystem abstraction | System.IO.Abstractions 22.2.0 |
| Logging | Microsoft.Extensions.Logging + custom file logger |
| Config | Microsoft.Extensions.Configuration |
| Nullable | Enabled; `TreatWarningsAsErrors = true` |

### Android Companion (Kotlin)

| Layer | Technology |
|---|---|
| Language | Kotlin (JVM 17) |
| UI | Jetpack Compose + Material 3 |
| Navigation | Navigation 3 (2026 standard) |
| DI | Hilt |
| HTTP server | Ktor + Netty (embedded) |
| Async | Kotlin Coroutines + StateFlow |
| Persistence | DataStore Preferences |
| Background | WorkManager |
| Image loading | Coil |
| Min SDK | 29 (Android 10+), Target SDK 37 |

---

## 4. Architecture Deep-Dive

### 4.1 Dependency Injection & Startup

`App.xaml.cs` bootstraps a `Microsoft.Extensions.Hosting` `IHost`. All registrations are in [`ServiceCollectionExtensions.cs`](file:///c:/Users/WTS-PC/source/repos/AndroidDeck/Hosting/ServiceCollectionExtensions.cs):

- **Singleton**: core services (navigation, dialog, settings, theme, connection coordinator, contact VM, dashboard VM)
- **Scoped**: phone-session-bound services (FileBrowserViewModel, GalleryViewModel, BackupViewModel, all workflow and API objects). Scope is created per phone session via `PhoneSessionScopeFactory`.
- **Transient**: `ShellWindow`

> [!NOTE]
> `ValidateOnBuild = true` and `ValidateScopes = true` are enabled — DI errors are caught at startup, not at runtime.

### 4.2 MVVM Pattern

All view-models use `CommunityToolkit.Mvvm` (`ObservableObject`, `[ObservableProperty]`, `[RelayCommand]`). View-models are singletons (or session-scoped), injected into views through the DI container. Views communicate back to the coordinator layer via C# events, not commands, to avoid circular dependencies.

### 4.3 Feature-Slice Layout

Each feature lives in `Features/<Name>/` and contains its own:
- **Workflow** — orchestration layer (`IBackupWorkflow`, `IFileTransferWorkflow`, etc.)
- **Service** — data/IO operations (`BackupArchiveService`, `GalleryTransferService`)
- **Presentation** — UI selection helpers (`ContactsViewPresentation`, `FileBrowserPresentation`)
- **Interaction** — user interaction logic (`FileBrowserInteraction`, `GalleryInteraction`)

This separation keeps the view-model thin and the domain logic testable.

### 4.4 Navigation

`Navigation/` defines a policy-driven shell navigation system:
- `ShellNavigationRegistry` — maps `ShellDestination` enum values to view factories
- `ShellNavigationPolicy` — controls which destinations are available based on connection state
- `ShellLayoutPolicy` — switches between Compact/Medium/Expanded responsive layouts
- `ShellCapabilitySnapshot` — carries runtime-detected phone capability flags (Files, Gallery, Backup)

---

## 5. Core Subsystems

### 5.1 HTTP Transport & Security ([`HttpTransport.cs`](file:///c:/Users/WTS-PC/source/repos/AndroidDeck/Core/HttpTransport.cs))

The `HttpTransport` class is the centrepiece of the Android connection layer:

- **Two HttpClient instances**: one for normal JSON requests (300 s timeout), one for large file transfers (2-hour timeout), both sharing a single `SocketsHttpHandler`
- **HMAC-SHA256 request signing**: every request includes `X-Client-Id`, `X-Timestamp`, `X-Nonce`, `X-Content-SHA256`, and `Authorization: HMAC <sig>` headers
- **TOFU certificate pinning**: first HTTPS connection pins the server cert SHA-256 fingerprint via `IAppSettingsStore`; subsequent connections reject mismatches unless an explicit re-pair is in progress
- **Retry logic**: GET/HEAD/OPTIONS retry up to 3× with exponential backoff + jitter (250 ms base, up to 200 ms jitter). POST/PUT/DELETE never retry
- **Error mapping**: HTTP 401 → session expired, 403 → read-only or permission denied, 409 → conflict, 429 → rate-limited, all translated to typed `PhoneConnectionException`

### 5.2 Pairing & Session ([`PairingKeyExchange.cs`](file:///c:/Users/WTS-PC/source/repos/AndroidDeck/Core/Security/PairingKeyExchange.cs), [`SessionManager.cs`](file:///c:/Users/WTS-PC/source/repos/AndroidDeck/Core/SessionManager.cs))

- **ECDH on NIST P-256** with HKDF-SHA256 key derivation (info string: `"AndroidDeck pairing v3"`, 32-byte output)
- 6-digit PIN pairing code flow (`PairingCodeLength = 6`)
- Heartbeat timer fires every 30 s; 3 consecutive failures trigger session error
- Session recovery retries a `/status` ping with a 6-second timeout

### 5.3 Secret Storage ([`WindowsDpapiSecretStore.cs`](file:///c:/Users/WTS-PC/source/repos/AndroidDeck/Core/Security/WindowsDpapiSecretStore.cs))

- HMAC secrets and pinned certs are stored in `%LOCALAPPDATA%\VcfEditor\secrets.json`
- Each secret is protected with Windows DPAPI (`DataProtectionScope.CurrentUser`) before Base64-encoding
- Atomic file writes use `File.Replace` with a `.bak` fallback
- Corrupt store is quarantined with a timestamped `.corrupt-*` copy and re-initialised empty

### 5.4 VCF Parser ([`VcfParser.cs`](file:///c:/Users/WTS-PC/source/repos/AndroidDeck/Core/VcfParser.cs))

- **Streaming async**: `IAsyncEnumerable<Contact>` — parses multi-thousand-contact files without loading everything into memory
- Supports vCard 2.1 (Quoted-Printable, soft line-break continuations) and vCard 3.0/RFC 6350 (folded continuations)
- Input size limits enforced per line (`VcfParsingLimits`)
- Cancellation-token support throughout

### 5.5 Backup System ([`BackupApi.cs`](file:///c:/Users/WTS-PC/source/repos/AndroidDeck/Core/BackupApi.cs), [`BackupArchiveService.cs`](file:///c:/Users/WTS-PC/source/repos/AndroidDeck/Features/Backup/BackupArchiveService.cs))

- Archive format progression: `VCFBAK01` (legacy) → `VCFBAK02` (compat) → `DECKBAK2` (current)
- Local AES-256-GCM encryption of backup archives (password-based)
- Incremental backups via `sinceMs` timestamp parameter
- Progress reporting via `IProgress<double>` throughout the async pipeline
- Restore preview, conflict resolution dialog, and item-level outcome tracking

### 5.6 Paging & Polling

- `PagedFetch` in `Core/Paging/` provides safe cursor-based paging with `PaginationGuard` to detect run-away loops
- `OperationPollingPolicy` in `Core/Polling/` implements timeout-aware polling for long-running server-side operations (backup, restore)

---

## 6. UI / Design System

The design system lives entirely in `Themes/`:

| File | Purpose |
|---|---|
| `Generated.Colors.Dark.xaml` | Token-generated dark palette |
| `Generated.Colors.Light.xaml` | Token-generated light palette |
| `Generated.Metrics.xaml` | Spacing, radius, icon sizes, sidebar/header dimensions |
| `Typography.xaml` | Text styles (Display, Headline, Body, Label, etc.) |
| `Controls.xaml` | Button, card, badge, input, progress, chip styles |
| `Navigation.xaml` | Sidebar nav item styles |
| `Layout.xaml` | Panel and container styles |
| `Animations.xaml` | Motion duration tokens |
| `Phase5.xaml` | Additional component styles (pending rename to `ComponentStyles.xaml`) |

`ThemeService` swaps the palette `ResourceDictionary` at runtime for theme switching. Responsive layout breakpoints are: **Compact** (< 900px), **Medium** (900–1199px), **Expanded** (≥ 1200px), driven by `ResponsiveLayoutBehavior`.

> [!WARNING]
> The UI Modernization Plan (`docs/ANDROIDDECK_UI_MODERNIZATION_PLAN.md`, status: **AWAITING APPROVAL**) documents a known critical bug: the sidebar renders with a **white gradient in dark mode** due to `Shell.SidebarBackground` incorrectly using the inverse surface color instead of the dark surface. This affects every screen.

---

## 7. Android Companion App

Located in `AndroidCompanion/`, this is a full Kotlin/Compose app (`com.aeliavision.androiddeck`):

- **Embedded Ktor/Netty server** — exposes the REST API the desktop app connects to
- **Features**: Contacts (via Android ContentProvider), Files (local file manager), Gallery (media grid with paging), Backups, Settings
- **HMAC authentication**: the server validates the same signature format the desktop client sends
- **Hilt DI** throughout; `StateFlow` + `collectAsStateWithLifecycle` for UI state
- Material 3 with dynamic color support
- Baseline Profiles for startup performance
- `compileSdk = 37`, `minSdk = 29` (Android 10+)

All planned improvement items (Files, Gallery, Settings, Architecture) are marked **COMPLETED** in `IMPROVEMENT_PLAN.md`.

---

## 8. Testing

| Project | Location | Test Count | Status |
|---|---|---|---|
| `VcfEditor.Tests` (legacy) | `VcfEditor.Tests/` | 3 | ✅ Builds |
| `VcfEditor.Core.Tests` | `Tests/VcfEditor.Core.Tests/` | 15 | ❌ Build errors (stale project ref) |
| `VcfEditor.UI.Tests` | `Tests/VcfEditor.UI.Tests/` | 9 | ❌ Build errors (stale project ref) |
| `VcfEditor.Performance.Tests` | `Tests/VcfEditor.Performance.Tests/` | 1 | ❌ Build errors (stale project ref) |
| `DesignTokenGenerator.Tests` | `Tests/DesignTokenGenerator.Tests/` | Unknown | ❌ Build errors (stale project ref) |

**Root cause of test build failures**: The solution references test projects under `tests\` (lowercase) but they physically live under `Tests\` (uppercase). On Windows this works at the filesystem level but MSBuild resolves them differently, causing missing assembly references.

The covered areas include:
- `VcfParserTests` — parsing correctness
- `BackupArchiveServiceTests` — encryption round-trips
- `ContactValidatorTests` — FluentValidation rules
- `JsonAppSettingsStoreTests` — settings persistence
- `PairingKeyExchangeTests` — crypto correctness
- `OperationPollingPolicyTests` — timeout/polling logic
- `BoundedStreamCopyTests` — IO limits
- `DashboardViewModelTests`, `SettingsViewModelTests` — VM behaviour
- `LargeDataPerformanceTests` — parsing 10,000-contact VCF performance

---

## 9. Code Quality Assessment

### Strengths

- **Nullable enabled + TreatWarningsAsErrors**: main app builds clean with zero warnings
- **`ArgumentNullException.ThrowIfNull` everywhere**: consistent null guard pattern
- **`sealed` classes throughout**: correct disposal semantics (`IDisposable`) in every long-lived service
- **`CryptographicOperations.ZeroMemory`** used after every sensitive byte array use
- **Structured logging**: `LogMessages` static class using compile-time `[LoggerMessage]` source generators — zero-allocation log calls
- **`System.IO.Abstractions`**: filesystem is abstracted, enabling unit testing without touching disk
- **FluentValidation** for contact validation — rules centralised and reusable
- **Interface-first design**: almost every service has a corresponding `I*` interface

### Areas for Improvement

1. **Test suite broken**: 17 build errors in 4 of 5 test projects — tests cannot be run. Likely a simple `.csproj` path fix.
2. **`ShellConnectionCoordinator` directly depends on `Views.ContactsView`**: a concrete view is constructor-injected into a coordinator, creating a view–service coupling that makes unit testing impossible.
3. **`ConnectPhoneDialog.xaml.cs` is 19 KB**: large code-behind handling complex pairing flow; could be extracted to a dedicated workflow/presenter.
4. **`Phase5.xaml`** is a stale name — the UI modernization plan already flags it for rename to `ComponentStyles.xaml`.
5. **No git history**: the repo is not a git repository, so there is no commit history, branch model, or automated CI pipeline visible.
6. **README is stale**: still describes the original Phase 1 single-window VCF editor ("A simple C# WPF application") rather than the full Android management platform it has grown into.

---

## 10. Security Posture

| Concern | Mitigation |
|---|---|
| Network eavesdropping | HTTPS (optional, configurable) + TOFU cert pinning |
| Request tampering | HMAC-SHA256 per-request signature with nonce + timestamp |
| Replay attacks | Nonce + timestamp enforced server-side (max drift: 5 min) |
| Secret storage | Windows DPAPI (user-scoped) — secrets encrypted at rest |
| Memory safety | `CryptographicOperations.ZeroMemory` on all key material after use |
| Session expiry | 30 s heartbeat; server-side session expiry propagated to UI |
| Read-only account protection | 403 `read_only` error code mapped to user-friendly message |

> [!NOTE]
> HTTPS is currently **optional** (`useHttps = false` default in `HttpTransport` constructor). In production the companion app likely enforces TLS; however if a deployment runs HTTP-only, traffic is authenticated (HMAC) but not encrypted.

---

## 11. Open Issues & Recommendations

| Priority | Issue | Recommendation |
|---|---|---|
| 🔴 High | Test suite build broken (17 errors) | Fix `.csproj` references: `tests\` → `Tests\` (or vice versa) |
| 🔴 High | Sidebar dark mode bug | Apply fix per UI Modernization Plan (already designed) |
| 🟡 Medium | `ShellConnectionCoordinator` → `ContactsView` coupling | Introduce `IContactsViewPhoneClient` interface |
| 🟡 Medium | `ConnectPhoneDialog.xaml.cs` (19 KB) | Extract pairing logic to `PairingWorkflow` |
| 🟡 Medium | README is out of date | Rewrite to reflect the full platform scope |
| 🟢 Low | `Phase5.xaml` naming | Rename to `ComponentStyles.xaml` |
| 🟢 Low | No git repository | Initialise git, add `.gitignore`, establish branching strategy |
| 🟢 Low | HTTP-only mode | Consider making HTTPS mandatory or clearly documenting HTTP-only risk |

---

## 12. Metrics Summary

| Metric | Value |
|---|---|
| Total source files (WPF app) | ~110 `.cs` + ~40 `.xaml` |
| Largest ViewModels | `BackupViewModel` (38 KB), `ContactsViewModel` (34 KB), `GalleryViewModel` (30 KB) |
| Largest service | `HttpTransport` (495 lines), `BackupApi` (515 lines), `VcfParser` (595 lines) |
| Largest view | `DashboardView.xaml` (34 KB), `ContactsView.xaml` (27 KB), `MainWindow.xaml` (26 KB) |
| NuGet packages (WPF) | 8 |
| Android dependencies | ~30 (Compose BOM, Ktor, Hilt, Coil, Navigation 3…) |
| Test files | 30 across 5 test projects |
| Design token files | 9 XAML theme files |
