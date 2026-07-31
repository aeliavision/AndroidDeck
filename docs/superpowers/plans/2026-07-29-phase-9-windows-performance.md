# Phase 9 Windows Performance Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Complete the Windows portions of Phase 9 so pagination, polling, streams, transfers, and large-data presentation are cancellation-aware and bounded.

**Architecture:** Introduce small reusable guards rather than duplicating safety logic in API clients. Pagination uses a typed incomplete-result exception and stable page fingerprints. Network and archive streams use centralized byte limits and bounded copy helpers. Polling uses a finite policy with cancellation and timeout. Static verification prevents new unreviewed infinite loops.

**Tech Stack:** .NET 10, C# 14, WPF, xUnit, FluentAssertions, Python source-verification scripts.

## Global Constraints

- Windows-only scope; ignore all Android tasks and files.
- Preserve protocol compatibility and current public API behavior except typed failures for incomplete pagination/timeout conditions.
- Do not buffer complete large files or archives in memory.
- Every externally controlled loop must have cancellation plus a deterministic bound.
- Keep WPF collection virtualization and session-close cancellation active.

---

### Task 1: Guard pagination

**Files:**
- Create: `Core/Paging/PaginationGuard.cs`
- Create: `Core/Paging/IncompletePagedResultException.cs`
- Modify: `Core/ContactsApi.cs`
- Modify: `Core/GalleryApi.cs`
- Test: `tests/VcfEditor.Core.Tests/PaginationGuardTests.cs`

- [ ] Write tests for A/B/A fingerprints, repeated/invalid next pages, maximum-page cap, successful completion, and cancellation.
- [ ] Run the Phase 9 source gate and confirm it fails before implementation.
- [ ] Implement the guard and typed exception.
- [ ] Refactor Contacts and Gallery to follow `NextPage`, detect non-forward progress, and throw typed incomplete-result errors.
- [ ] Run source gates and tests on a Windows .NET SDK.

### Task 2: Bound polling and stream loops

**Files:**
- Create: `Core/IO/TransferLimits.cs`
- Create: `Core/IO/BoundedStreamCopy.cs`
- Create: `Core/Polling/OperationPollingPolicy.cs`
- Modify: `Core/BackupApi.cs`
- Modify: `Core/FileSystemApi.cs`
- Modify: `Core/GalleryApi.cs`
- Modify: `Core/ContactsApi.cs`
- Modify: `Core/ProgressableStreamContent.cs`
- Modify: `Features/Backup/BackupWorkflow.cs`
- Modify: `Features/Backup/RestoreWorkflow.cs`
- Test: `tests/VcfEditor.Core.Tests/BoundedStreamCopyTests.cs`
- Test: `tests/VcfEditor.Core.Tests/OperationPollingPolicyTests.cs`

- [ ] Write failing tests for byte-limit rejection, cancellation, progress, timeout, and terminal polling states.
- [ ] Implement centralized limits and bounded streaming helpers.
- [ ] Reject oversized content lengths before transfer and stop streams that exceed limits.
- [ ] Replace unbounded backup/restore polling with finite policy execution.
- [ ] Mark intentionally finite stream loops with reviewed comments only where a helper cannot replace them.

### Task 3: Large-data UI and lifecycle safety

**Files:**
- Modify: `ViewModels/ContactsViewModel.cs`
- Modify: `ViewModels/FileBrowserViewModel.cs`
- Modify: `ViewModels/GalleryViewModel.cs`
- Modify: `ViewModels/BackupViewModel.cs`
- Modify: relevant XAML views
- Test: UI view-model cancellation tests where practical

- [ ] Confirm contacts, files, and gallery controls use recycling virtualization.
- [ ] Confirm session/page disposal cancels thumbnails, transfers, backup, and restore operations.
- [ ] Add batch collection updates where large lists currently raise one notification per item.
- [ ] Add lightweight operation-duration telemetry for high-risk workflows without logging sensitive data.

### Task 4: Benchmarks and regression gate

**Files:**
- Create: `tests/VcfEditor.Performance.Tests/VcfEditor.Performance.Tests.csproj`
- Create: benchmark-style tests for VCF parsing/export, 10k contacts, 5k gallery items, bounded 1GB transfer simulation, and backup manifest processing.
- Create: `scripts/verify-phase9-windows.py`
- Modify: `VcfEditor.sln`
- Modify: `scripts/verify-windows.ps1`
- Modify: `Modernization_Plan.md`
- Create: `PHASE_9_WINDOWS_COMPLETION_REPORT.md`

- [ ] Add deterministic performance regression tests with generous CI thresholds and no 1GB memory allocation.
- [ ] Add a static gate for unreviewed `while(true)`, missing pagination guards, missing transfer limits, and verification wiring.
- [ ] Run all available source gates and parse every XAML file.
- [ ] Package a clean Phase 9 Windows archive and record SHA-256.
