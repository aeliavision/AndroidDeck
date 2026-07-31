# AndroidDeck UI Modernization Plan
**Version 1.0 — 2026-07-30**
**Status: AWAITING APPROVAL**

---

## 1. Executive Summary

### Current UI State
AndroidDeck is a **WPF / .NET 10** desktop application with a partially implemented design system. A `design-tokens.json` source file exists, generated color palettes exist, and basic semantic styles (cards, buttons, typography) are defined in `Themes/`. However, the application has **critical visual bugs** — most notably the sidebar rendering with a white gradient in dark mode — and the dashboard layout bears little resemblance to the approved reference image. No quick-action icon cards, no storage summary, no top-header search bar, no statistic card icon badges, and no feature-badge strip on the hero panel exist.

### Target UI State
A professional, premium Windows desktop application that closely follows the approved reference image:
- Neutral-dark shell (`#15161A` bg / `#1E1F24` sidebar + cards)
- Sectioned left sidebar with proper dark background, icon-glyphed nav items, and a bottom connection-status strip
- Top header with page title, global search, notification bell, and avatar
- 4-column statistic card row with colored icon badges and sub-text
- Full-width device-connection hero panel with illustration, feature chips, and action buttons
- Quick-actions icon-card grid + Storage-summary donut chart in a side-by-side row
- Recent-activity list with "View all" action
- All screens using the shared design system — no screen-local styles

### Main Modernization Goals
1. **Fix the sidebar color inversion bug** (highest priority — visible on every screen)
2. **Update the dark-theme palette** to match the approved neutral-dark reference
3. **Rebuild the dashboard** to match the reference image exactly
4. **Modernize the top header** with search, notifications, and avatar
5. **Enforce single-source-of-truth** — remove orphan files and compat-key aliases
6. **Apply the design system consistently** across all 6 remaining screens
7. **Add missing tokens** — sidebar dimensions, header height, animation durations, icon sizes

### Major Architectural Changes
- No framework change. Remain on WPF / .NET 10. The architecture is sound.
- Delete `Themes/Colors.Light.xaml` (orphan, never loaded, conflicting key names)
- Update `design-tokens.json` with the corrected neutral-dark palette values
- Regenerate `Generated.Colors.Dark.xaml` with correct values
- Fix `Navigation.xaml` `Shell.SidebarBackground` to use dark surface colors, not inverse surface
- Extend `Generated.Metrics.xaml` with icon sizes, sidebar width, header height, animation duration tokens
- Rename `Phase5.xaml` → `Themes/ComponentStyles.xaml` for clarity
- Add `Themes/Animations.xaml` for motion tokens
- Add quick-action icon card, statistic card, storage-summary, and hero-panel component styles
- Modernize `ShellWindow.xaml` top-header with search + notification + avatar
- Rebuild `DashboardView.xaml` to match reference layout
- Extend `DashboardViewModel` with storage summary metrics and a 4th statistic (Storage/Backups)

---

## 2. Current Project Audit

### 2.1 Framework and Architecture

| Item | Value |
|---|---|
| Framework | WPF on .NET 10.0-windows |
| MVVM library | CommunityToolkit.Mvvm 8.4.2 |
| DI | Microsoft.Extensions.DependencyInjection + Hosting |
| Nullable | Enabled, TreatWarningsAsErrors=true |
| Icon system | Segoe MDL2 Assets (glyph text in TextBlock) |
| Responsive behavior | ResponsiveLayoutBehavior attached property (Compact < 900px, Medium 900-1199px, Expanded >= 1200px) |
| Theme switching | ThemeService.cs — swaps the palette ResourceDictionary at runtime |
| Default startup theme | Light (App.xaml loads Generated.Colors.Light.xaml) — ThemeService corrects to system preference at startup |

### 2.2 Current Theme File Structure

```
Themes/
├── Colors.Light.xaml              ← ORPHAN — not loaded by App.xaml, conflicting key names
├── Generated.Colors.Dark.xaml     ← Authoritative dark palette + legacy compat keys
├── Generated.Colors.Light.xaml    ← Authoritative light palette + legacy compat keys
├── Generated.Metrics.xaml         ← Corner radii, spacing, font sizes, control heights
├── Layout.xaml                    ← ModernCard, ToolbarCard, HeroPanel, Converters
├── Navigation.xaml                ← Shell sidebar styles, nav items, status brushes
├── Phase5.xaml                    ← Page-level styles, button variants, dialog styles (BAD NAME)
└── Typography.xaml                ← Base Window/TextBlock + named text role styles
```

### 2.3 Current Problems — Severity Ranked

#### CRITICAL (visible immediately)

**P1 — Sidebar is white/light in dark mode**
- `Navigation.xaml` L3-6: `Shell.SidebarBackground` is a LinearGradientBrush from `Color.InverseSurface` (#F8FAFC) to `Color.InverseSurfaceAlt` (#E2E8F0)
- In dark mode the sidebar renders as a white-to-light-gray gradient. The reference requires a dark sidebar (~#1E1F24)

**P2 — Dark palette colors do not match the reference**
- Current dark: Background `#0B1120` (navy), Surface `#111827` (dark navy-gray)
- Reference: Background `#15161A` (neutral dark), Cards `#1E1F24` (neutral dark gray)
- The current palette has a cool blue-navy tint vs the reference neutral charcoal

#### HIGH (visible on dashboard)

**P3 — Dashboard missing major reference-image components**
- Missing: colored icon badges on stat cards
- Missing: 4th "Storage / Backups" stat card (ViewModel only has 3)
- Missing: feature chips ("100% Local", "Secure", "Fast") on hero panel
- Missing: phone illustration on hero panel left
- Missing: "Device details" secondary button
- Missing: quick-action icon card grid (currently plain buttons)
- Missing: storage summary panel with donut chart
- Missing: "View all" button on Recent Activity

**P4 — Top header is minimal**
- Current: hamburger (overlay mode only) + page title text
- Reference: page title + subtitle, search pill, notification bell, avatar with dropdown

#### MEDIUM (structural)

**P5 — `Colors.Light.xaml` is an orphan file** — never loaded, conflicting key names (`Brush.Card`, `Brush.Primary.Dark`, `Brush.Text.Primary` vs authoritative `Brush.Surface`, `Brush.Primary`, `Brush.TextPrimary`)

**P6 — Legacy alias keys in Generated color files** — 30+ compat keys (`PrimaryBrush`, `BackgroundBrush`, `SurfaceBrush`) mixed with semantic keys. `ShellWindow.xaml` still uses compat names.

**P7 — `Phase5.xaml` filename is not descriptive** — should be `ComponentStyles.xaml`

**P8 — Hardcoded values in Navigation/Sidebar XAML**
- `ShellSidebarView.xaml` L57: `FontSize="18"` (sidebar title)
- `ShellSidebarView.xaml` L61: `FontSize="11"` (subtitle)
- `ShellSidebarView.xaml` L150: `FontSize="13"` (nav label)
- `Navigation.xaml` L27: `FontSize="11"` in `Shell.SidebarSectionLabelText`
- `ShellWindow.xaml` L48: `Height="64"` (top header)

**P9 — Missing design tokens**
- No `Sidebar.Width`, `Header.Height` tokens
- No icon-size tokens (`IconSize.Navigation`, `IconSize.Card`, etc.)
- No animation/transition duration tokens
- No shadow/elevation definitions

**P10 — No storage-summary data in DashboardViewModel** — required by reference image storage-summary card

---

## 3. Reference-Image Design Analysis

### 3.1 Overall Grid

```
┌─────────────────────────────────────────────────────────┐
│ ┌──────────┬──────────────────────────────────────────┐ │
│ │ Sidebar  │ Header (60px tall)                       │ │
│ │ 190px    ├──────────────────────────────────────────┤ │
│ │          │ Content area (scrollable, 24px padding)  │ │
│ │          │  ┌──┬──┬──┬──┐  4-column stat cards      │ │
│ │          │  ┌────────────────────────────┐  hero    │ │
│ │          │  ┌─────────────┐  ┌──────────┐           │ │
│ │          │  │ Quick acts  │  │ Storage  │           │ │
│ │          │  └─────────────┘  └──────────┘           │ │
│ │          │  ┌──────────────────────────┐  activity  │ │
│ │ [status] │                                          │ │
│ └──────────┴──────────────────────────────────────────┘ │
└─────────────────────────────────────────────────────────┘
```

### 3.2 Color Roles (from image)

| Token name | Hex | Usage |
|---|---|---|
| Color.App.Background | #15161A | Window background, content area |
| Color.App.Sidebar | #1E1F24 | Sidebar, cards |
| Color.App.CardBorder | #2C2D33 | 1px border on cards |
| Color.App.NavSelected | #3B5BDB | Selected nav item fill |
| Color.App.Accent | #4F8BF9 | Primary buttons, active states |
| Color.App.TextPrimary | #E8E9EA | Primary text |
| Color.App.TextSecondary | #9CA3AF | Secondary text |
| Color.App.TextMuted | #6B7280 | Section labels, captions |
| Color.App.StatusConnected | #4CAF50 | Green connected dot |
| Color.App.IconContacts | #3B82F6 | Contacts badge (blue) |
| Color.App.IconPhotos | #10B981 | Photos badge (green) |
| Color.App.IconGroups | #8B5CF6 | Groups badge (purple) |
| Color.App.IconStorage | #64748B | Storage badge (slate) |

### 3.3 Typography Roles (from image)

| Role | Size | Weight | Color |
|---|---|---|---|
| Page title | 22-24px | SemiBold | TextPrimary |
| Page subtitle | 13px | Regular | TextSecondary |
| Stat card value | 26-28px | Bold | TextPrimary |
| Stat card label | 12px | Regular | TextSecondary |
| Section title | 16px | SemiBold | TextPrimary |
| Quick-action label | 13px | SemiBold | TextPrimary |
| Nav label | 13px | Medium | TextSecondary |
| Section group | 10-11px | SemiBold | TextMuted (CAPS) |

### 3.4 Dashboard Component Hierarchy

```
Dashboard Page
├── 4-column UniformGrid (stat cards)
│   ├── StatCard: Contacts [blue icon]
│   ├── StatCard: Photos [green icon]
│   ├── StatCard: Groups [purple icon]
│   └── StatCard: Storage / Backups [slate icon]
├── Device hero panel (full width, 3 columns)
│   ├── Left: phone illustration / glyph
│   ├── Center: title + description + feature chips
│   └── Right: Connect button + Device details button
├── Two-column row (60/40)
│   ├── Quick Actions (4 icon cards: Browse files, Open gallery, Create backup, Import contacts)
│   └── Storage Summary (donut + legend + progress bar)
└── Recent Activity (full width + "View all" link)
```

---

## 4. Proposed Design System

### 4.1 Target File Structure

```
Themes/
├── design-tokens.json              ← Single source of truth (update values)
├── Generated.Colors.Dark.xaml      ← Regenerated (fix colors + new tokens)
├── Generated.Colors.Light.xaml     ← Add new semantic keys
├── Generated.Metrics.xaml          ← Add icon sizes, sidebar/header dims
├── Typography.xaml                 ← Add missing text role styles
├── Layout.xaml                     ← Extend with new card types
├── Navigation.xaml                 ← Fix sidebar background bug
├── ComponentStyles.xaml            ← Renamed from Phase5.xaml
└── Animations.xaml                 ← NEW: motion token definitions

Colors.Light.xaml                   ← DELETE (orphan, conflicting keys)
```

---

## 5. Design Tokens

### 5.1 Dark Palette Changes

| Token key | Current | Proposed | Notes |
|---|---|---|---|
| Color.Background | #0B1120 | **#15161A** | Neutral dark (not navy) |
| Color.Surface | #111827 | **#1E1F24** | Cards, sidebar |
| Color.SurfaceAlt | #1F2937 | **#26272D** | Hover states |
| Color.SurfaceRaised | #172033 | **#212229** | Elevated cards |
| Color.Border | #273449 | **#2C2D33** | Card borders |
| Color.BorderStrong | #475569 | **#3F4047** | Stronger borders |
| Color.TextPrimary | #F8FAFC | **#E8E9EA** | Slightly warmer |
| Color.TextSecondary | #CBD5E1 | **#9CA3AF** | Match reference |
| Color.Primary | #60A5FA | **#4F8BF9** | Match reference accent |
| Color.Sidebar | *(missing)* | **#1E1F24** | NEW — sidebar background |
| Color.TextMuted | *(missing)* | **#6B7280** | NEW — section labels |
| Color.NavSelected | *(missing)* | **#3B5BDB** | NEW — nav fill |
| Color.NavHover | *(missing)* | **#2C2D33** | NEW — nav hover |
| Color.IconContacts | *(missing)* | **#3B82F6** | NEW — badge color |
| Color.IconPhotos | *(missing)* | **#10B981** | NEW — badge color |
| Color.IconGroups | *(missing)* | **#8B5CF6** | NEW — badge color |
| Color.IconStorage | *(missing)* | **#64748B** | NEW — badge color |

### 5.2 New Dimension Tokens (add to Generated.Metrics.xaml)

| Key | Value | Notes |
|---|---|---|
| Shell.Sidebar.Width | 190 | Desktop sidebar width |
| Shell.Sidebar.CollapsedWidth | 60 | Icon-only width |
| Shell.Header.Height | 60 | Top header height |
| IconSize.Sm | 14 | Small inline icons |
| IconSize.Nav | 17 | Sidebar nav icons |
| IconSize.Md | 20 | Standard card icons |
| IconSize.Lg | 24 | Prominent icons |
| IconSize.Hero | 48 | Hero / empty-state icons |
| StatCard.BadgeSize | 40 | Stat card badge circle diameter |
| Anim.HoverMs | 150 | Hover transition (ms) |
| Anim.NavSelectMs | 200 | Nav selection transition (ms) |
| Anim.FadeMs | 250 | Content fade-in (ms) |

### 5.3 New Typography Styles (add to Typography.xaml)

| Key | Size | Weight | Foreground |
|---|---|---|---|
| StatValueText | 26px | Bold | TextPrimary |
| MutedText | 12px | Regular | TextMuted |
| NavSectionLabel | 11px | SemiBold | TextMuted |
| QuickActionLabel | 13px | SemiBold | TextPrimary |
| QuickActionSubtitle | 12px | Regular | TextMuted |
| HeroTitle | 20px | SemiBold | TextPrimary |
| HeroSubtitle | 14px | Regular | TextSecondary |

---

## 6. Reusable Component Inventory

| Component | Purpose | States | New/Existing |
|---|---|---|---|
| StatisticCard | Metric with icon badge + value + subtext | Normal, Loading, Disconnected | Refactor MetricCard |
| QuickActionCard | Icon + label + sublabel, clickable card | Normal, Hover, Pressed, Disabled | New |
| StorageSummaryPanel | Donut chart + legend + progress bar | Normal, Loading, Empty | New |
| FeatureChip | Icon + text pill (hero panel chips) | Static | New |
| SectionHeader | Title + subtitle + optional right action | Static | New style |
| GlobalSearchBox | Search pill with Ctrl+K shortcut | Normal, Focused, Active | New |
| AvatarButton | Circular initials + dropdown | Normal, Hover, Menu-open | New |
| NotificationButton | Bell icon + optional badge | Normal, Hover, Has-badge | Extend IconButton |
| StatusChip | Colored dot + status text pill | Connected, Disconnected, Busy, Error | Refactor PhoneConnectionStatusView |

---

## 7. Dashboard Redesign Plan

### 7.1 Top Header (ShellWindow.xaml)

Current: hamburger + page title only

Proposed 3-column Grid:
- Col 0 (Auto): hamburger (overlay mode) + stacked page title + subtitle
- Col 1 (*): spacer
- Col 2 (Auto): GlobalSearchBox + NotificationButton + AvatarButton

New ViewModel properties needed on ShellWindowViewModel:
- `CurrentSubtitle` — per-page subtitle text
- `GlobalSearchCommand` (stub for now)

### 7.2 Stat Card Row

- Replace WrapPanel + MetricCard with `UniformGrid Columns="4"`
- DataTemplate includes: colored circle badge (icon glyph), metric label, large value, sub-text, right arrow chevron
- 4th metric: "Storage / Backups" — add to DashboardViewModel.Metrics

### 7.3 Device Connection Hero Panel

3-column layout:
- Left (150px): Android glyph in gradient circle illustration area
- Center (*): Title + description paragraph + WrapPanel of 3 FeatureChips
- Right (Auto): VStack with "Connect device" PrimaryButton + "Device details" SecondaryButton

States via DataTrigger on PhoneConnectionState:
- Disconnected: dark gradient bg, "No device connected"
- Connecting: yellow tint, spinner
- Connected: green tint, device name
- Error: red tint, "Retry connection"

### 7.4 Quick Actions + Storage Summary Row

2-column Grid (ratio ~60/40):
- Left: Section header "Quick actions" + 4-card UniformGrid (Browse files, Open gallery, Create backup, Import contacts)
- Right: Section header "Storage summary" + StorageSummaryPanel (donut + legend + bar)

### 7.5 Recent Activity

- Add DockPanel header: "Recent activity" title on left, "View all" LinkButton on right
- Keep existing ItemsControl bound to RecentActivity
- Keep existing empty state; update icon to clipboard glyph

---

## 8. Theme Architecture

### Single Source of Truth Flow

```
design-tokens.json
  → Generated.Colors.Dark.xaml  /  Generated.Colors.Light.xaml
  → ThemeService swaps at runtime
  → All XAML via DynamicResource
```

ThemeService already implements runtime switching correctly. The only fix needed is the sidebar background color inversion bug in Navigation.xaml.

---

## 9. MVVM Requirements

**New ViewModel properties needed:**

DashboardViewModel:
- `DashboardMetricItem StorageMetric` (4th stat card)
- `long BackupBytes`, `long OtherDataBytes`, `long TotalBytes` (storage summary)
- `bool IsStorageLoaded`
- `ICommand ShowDeviceDetailsCommand`

ShellWindowViewModel:
- `string CurrentSubtitle` (computed per page)
- `ICommand GlobalSearchCommand` (stub)

**Rules maintained:**
- No hardcoded colors in XAML
- No business logic in code-behind
- No TwoWay bindings to read-only properties
- No duplicate ViewModel constructors
- DI injection only

---

## 10. Accessibility Requirements

| Requirement | Plan |
|---|---|
| AutomationProperties.Name | Add to all new controls (stat cards, quick-action cards, search, avatar) |
| Keyboard navigation | Verify Tab order on rebuilt dashboard |
| Focus indicators | 2px Brush.Focus ring on all new controls |
| High Contrast | DataTrigger on SystemParameters.HighContrast in all new styles |
| Min contrast ratio | TextMuted (#6B7280) on card (#1E1F24): ~4.5:1 minimum |
| Touch targets | Quick-action cards: min 48x48px |
| Text scaling | Test at 125% and 150% Windows text size |
| High DPI | SnapsToDevicePixels="True" on all card borders |
| Min window size | Test at 720px — no horizontal scroll |

---

## 11. Performance Requirements

- All brushes via shared DynamicResource — no new SolidColorBrush() inline
- Animation durations <= 250ms; respect SystemParameters.ClientAreaAnimation
- Donut chart: DrawingBrush + ArcSegment geometry (no charting library)
- No new third-party UI libraries introduced
- Existing contact/gallery virtualization maintained

---

## 12. Testing Strategy

Existing: 38 unit tests — all must pass after every phase.

New tests to add:
```csharp
[Fact] void AllRequiredBrushesExist_DarkTheme()
[Fact] void AllRequiredBrushesExist_LightTheme()
[Fact] void DashboardViewModel_Has4Metrics()
[Fact] void DashboardViewModel_StorageMetric_UpdatesCorrectly()
```

Manual checklist after each phase:
- [ ] Sidebar is dark in dark mode
- [ ] Stat cards show 4 cards with icon badges
- [ ] Hero panel transitions between all states
- [ ] Quick-action icon cards visible
- [ ] Storage summary renders
- [ ] All navigation items work
- [ ] Keyboard accessible (Tab, Enter)
- [ ] No horizontal scroll at 720px width

---

## 13. Risks and Mitigation

| Risk | Likelihood | Impact | Mitigation |
|---|---|---|---|
| Palette change breaks Light theme contrast | Medium | High | Run WCAG checks before committing each color |
| New resource key typo causes XamlParseException | Medium | High | Resource validation unit tests in Phase 1 |
| UniformGrid breaks stat cards at 720px width | Medium | Medium | DataTrigger fallback to 2-column grid |
| Donut chart is CPU-intensive | Low | Medium | DrawingBrush retained-mode (not DrawingContext) |
| ShellWindowViewModel changes break navigation | Medium | High | Minimize changes; subtitle as computed property only |
| Removing Phase5.xaml reference in App.xaml | Low | High | Search all files for Phase5 before deleting |
| Removing compat alias keys breaks views | High | High | Full usage audit before removal — Phase 8 only |

---

## 14. Phase-by-Phase Implementation Plan

### Phase 0 — Audit and Baseline (COMPLETE — this document)
No code changes. Plan created.

---

### Phase 1 — Design-System Foundation
**Goal:** Fix palette values, add missing tokens, delete orphan file. No visual layout changes.
**Complexity:** Low | **Behavior change:** Color values only

Files to change:
- `design-tokens.json` — update dark colors, add sidebar/textMuted/navSelected/icon* tokens
- `Themes/Generated.Colors.Dark.xaml` — update values, add Brush.Sidebar, Brush.TextMuted, Brush.NavSelected, Brush.NavHover, Brush.Icon* brushes
- `Themes/Generated.Colors.Light.xaml` — add matching new semantic brushes
- `Themes/Generated.Metrics.xaml` — add Shell.Sidebar.Width, Shell.Header.Height, IconSize.*, Anim.* tokens
- `Themes/Typography.xaml` — add StatValueText, MutedText, NavSectionLabel, QuickActionLabel, QuickActionSubtitle, HeroTitle, HeroSubtitle styles

New files:
- `Themes/Animations.xaml` — motion duration values only (no storyboards yet)

Files to delete:
- `Themes/Colors.Light.xaml` — orphan with conflicting key naming

App.xaml: Add `Themes/Animations.xaml` to MergedDictionaries

Acceptance criteria:
- `dotnet build` — 0 errors | All 38 tests pass
- Dark palette uses #15161A background
- No XAML resource-not-found exceptions at startup

---

### Phase 2 — Application Shell
**Goal:** Fix sidebar background bug. Modernize top header. All navigation behavior preserved.
**Complexity:** Medium | **Behavior change:** Visual — navigation logic unchanged

Files to change:
- `Themes/Navigation.xaml` — replace Shell.SidebarBackground gradient with Brush.Sidebar flat brush; add Brush.NavSelected/NavHover brushes; update nav item triggers
- `Views/ShellWindow.xaml` — replace Height="64" with token; expand header to 3-column grid; add search box, bell, avatar; replace compat brush key usages with semantic keys
- `Views/ShellSidebarView.xaml` — replace hardcoded FontSize values with token references
- `ViewModels/ShellWindowViewModel.cs` — add CurrentSubtitle property; GlobalSearchCommand stub

Acceptance criteria:
- Sidebar is dark in dark mode, light in light mode ✅
- Top header shows: title + subtitle + search + bell + avatar
- All navigation items and keyboard shortcuts work
- All 38+ tests pass

---

### Phase 3 — Dashboard Structure
**Goal:** Rebuild DashboardView.xaml to match reference image. Placeholder data for new sections.
**Complexity:** High | **Behavior change:** Visual restructure, existing bindings preserved

Files to change:
- `Views/DashboardView.xaml` — complete layout rebuild (4-column stats, hero panel, quick-action grid, storage summary, recent activity)
- `Themes/Layout.xaml` — add QuickActionCard style, StatisticCard style, FeatureChip style
- `Themes/ComponentStyles.xaml` (renamed from Phase5.xaml) — add hero-panel state styles
- `App.xaml` — update dictionary reference Phase5.xaml → ComponentStyles.xaml

Acceptance criteria:
- Dashboard matches reference image at 1280px
- 4 stat cards with icon badges
- Hero panel with 3-column layout and feature chips
- Quick-action 4-card grid
- Storage summary panel (zeros)
- Recent activity with "View all" button
- No hardcoded colors or font sizes
- All 38+ tests pass

---

### Phase 4 — Dashboard Data Integration
**Goal:** Wire all new dashboard components to real ViewModel data.
**Complexity:** Medium | **Behavior change:** Yes — storage summary and 4th stat card show real data

Files to change:
- `ViewModels/DashboardViewModel.cs` — add StorageMetric, storage summary properties, ShowDeviceDetailsCommand
- `Services/ShellConnectionCoordinator.cs` — pipe storage data into DashboardViewModel
- `Views/DashboardView.xaml` — replace static storage values with bindings; add hero panel state DataTriggers

Acceptance criteria:
- 4th stat card shows backup size or "—" when disconnected
- Hero panel transitions correctly between all 4 states
- Storage summary shows real data when phone connected
- All 38+ tests pass

---

### Phase 5 — Remaining Screens
**Goal:** Apply design system to all 5 remaining screens.
**Complexity:** Medium per screen

Order: Settings → Backup & Restore → Contacts → Files → Gallery

Each screen: replace local styles with shared tokens, ensure consistent button hierarchy, remove any hardcoded values.

Acceptance criteria per screen: Build passes, all tests pass, no hardcoded colors or font sizes remain.

---

### Phase 6 — Dialogs and Feedback States
**Goal:** Modernize all dialogs.

Files: `ConnectPhoneDialog.xaml`, `EditContactDialog.xaml`, programmatic dialog classes (`AppMessageDialog`, `BackupCompletionDialog`, etc.)

Acceptance criteria: All dialogs open with correct semantic colors, button hierarchy, keyboard navigation.

---

### Phase 7 — Accessibility and Adaptive Layout
**Goal:** Systematic accessibility pass.

Tasks: Tab order audit; AutomationProperties.Name on all new controls; focus ring verification; test at 720px, 125% text scale, 150% DPI; screen-reader live regions for connection status.

---

### Phase 8 — Cleanup and Enforcement
**Goal:** Remove all legacy compat keys after all screens migrated.

Files: `Generated.Colors.Dark.xaml`, `Generated.Colors.Light.xaml` (remove L74-108 alias section); any views still using compat brush names.

Add: Resource validation tests that assert no PascalCase compat brush keys remain.

---

### Phase 9 — Final Verification
Full regression checklist (see Definition of Done below).

---

## 15. Definition of Done

The modernization is complete when ALL of the following are true:

- [x] Sidebar is correctly dark in dark mode
- [x] Dashboard matches the approved reference image
- [x] 4 stat cards with colored icon badges render
- [x] Device connection hero panel shows illustration, feature chips, and correct buttons
- [x] Quick-action icon card grid renders (4 cards)
- [x] Storage summary renders
- [x] Top header has search, notification, and avatar controls
- [x] All 6 screens use shared design system (no screen-local button or card styles)
- [x] `design-tokens.json` is the single source of truth for all visual values
- [x] No production view contains a hardcoded hex color
- [x] No production view contains a hardcoded font size (all use {StaticResource FontSize.xxx})
- [x] No production view contains an arbitrary Padding/Margin value (all use spacing tokens)
- [x] `Themes/Colors.Light.xaml` orphan is deleted
- [x] All legacy compat alias brush keys removed from Generated files
- [x] Theme switching (Dark / Light / System) works correctly at runtime
- [x] `dotnet build` — 0 errors
- [x] All tests pass (38+ total)
- [x] All interactive elements have AutomationProperties.Name
- [x] No horizontal scroll at MinWidth (720px)
- [x] High Contrast mode renders accessibly
- [x] Documentation exists explaining how to extend the design system

---

*Plan created by: Antigravity*
*Based on: full project audit of all Themes/, Views/, ViewModels/ files + approved reference image analysis*
*No code has been changed as part of this plan.*
*Awaiting explicit approval to begin Phase 1.*
