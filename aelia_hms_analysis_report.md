# Aelia HMS — Full Project Analysis Report

> **Generated:** 2026-07-31 | **Version analyzed:** `v1.3.277` | **Analysis method:** Superpowers skills — exploratory research pass

---

## 1. Executive Summary

**Aelia HMS** is a comprehensive, production-grade **Hospital Management System (HMS)** built as a modular monorepo. It manages the full lifecycle of hospital operations — from patient admission and clinical workflows to stock management, cashier payments, lab/radiology services, real-time dashboards, and system observability.

The project is at a **stable source baseline** for `v1.3.277`, with production acceptance remaining evidence-gated on the deployment host. It is an architecturally sophisticated system with strong engineering discipline: strict source-of-truth ownership, a large library of static verifiers, Playwright E2E testing, and a published OpenAPI contract.

---

## 2. Repository Overview

| Attribute | Value |
|---|---|
| **App version** | `1.3.277` |
| **Package manager** | `pnpm@11.17.0` |
| **Node.js requirement** | ≥ 20.19.0 |
| **Database** | PostgreSQL via Prisma 7.8.0 |
| **Backend framework** | NestJS |
| **Frontend framework** | Next.js |
| **TypeScript version** | 5.9.3 |
| **Test runner** | Jest (API + Web unit), Playwright (E2E) |
| **Prisma models** | 191 |
| **Prisma enums** | 31 |
| **OpenAPI paths** | 453 |
| **OpenAPI operations** | 528 |
| **NestJS controllers** | 62 |
| **Frontend page routes** | 129 |
| **Backend services** | 107 |
| **Automated spec files** | 98 |

---

## 3. Architecture

### 3.1 Monorepo Structure

```
aelia-hms-clean-release/
├── apps/
│   ├── api/         ← NestJS backend (port 4000)
│   └── web/         ← Next.js frontend (port 3000)
├── packages/        ← Shared workspace packages
├── prisma/          ← Canonical schema (schema.prisma) + PostgreSQL extensions
├── scripts/         ← 167 files: verifiers, seeds, smoke tests, release gates
├── tests/e2e/       ← Playwright E2E tests
├── architecture/    ← Module ownership registry and metadata
└── docs/            ← 40 active docs: runbooks, policies, API contract snapshot
```

### 3.2 Backend (NestJS API)

The backend spans **86+ modules** covering every clinical and operational domain. Key architectural layers:

| Layer | Role |
|---|---|
| **Controller** | HTTP routes + DTO entry point only |
| **Read model service** | Query composition for UI/API reads |
| **Writer service** | Transactional writes and business state transitions |
| **Policy service** | Access, ownership, and workflow decisions |
| **Verifier script** | Static guard preventing regression |
| **Spec file** | Runtime unit/integration behavior proof |

> [!IMPORTANT]
> Controllers must not contain business logic. All significant logic lives in domain services. This is enforced by file-size budget verifiers.

### 3.3 Frontend (Next.js)

The frontend has **129 routes** organized into:

```
app/<route>/page.tsx            ← Route entry (thin)
features/<domain>/*Workspace.tsx ← Domain workspace controller/view
components/<domain>/*           ← Reusable domain panels
components/ui/*                 ← Shared UI primitives
lib/api.ts                      ← API wrapper and error parsing
lib/auth.ts                     ← Frontend access-shape helpers
```

Shared UI primitives: `PageHeader`, `MetricCard`, `DataTable`, `EmptyState`, `StatusBanner`, `ConfirmDialog`, `FormField`, `FormSection`, `ResponsiveRecordCard`, `QuickFilterBar`, `ToastProvider`.

The design system is fully token-driven from `apps/web/app/theme.css` — the **sole authority** for palette, typography, spacing, radii, controls, tables, motion, and layering.

---

## 4. Domain Modules

### 4.1 Core Clinical Modules

| Module | Responsibility |
|---|---|
| **Auth / Access** | Login, sessions, route access, role templates, permissions, department scopes |
| **Patients** | Patient identity, profile, history, attachments, scoped patient reads |
| **Visits / Encounters** | ER intake, visit lifecycle, encounter numbers, clinical actions |
| **Admissions** | Admission records, beds, room assignment, transfer requests, discharge, occupancy |
| **Departments** | Department profiles, dashboards, staff assignment, command centers |

### 4.2 Operations Modules

| Module | Responsibility |
|---|---|
| **Orders** | Department orders, approvals, fulfillment queue, receipt, requests center |
| **Stock** | Items, batches, balances, movements, visibility, transfers, inventory views |
| **Pharmacy** | Pharmacy workspace, fulfillment policies, medication support |
| **Lab / Radiology** | Service queues, module orders, request workflows, dashboard read models |
| **Cashier** | Pricing, charges, ledger, payments, cashier shifts, financial clearance |

### 4.3 Advanced / Specialist Modules

| Module | Notes |
|---|---|
| **eMAR / Nursing** | eMAR administration safety, nursing assessments, handovers, fluids, flowsheets |
| **ICU / Surgery / Maternity** | Acute care episodes, theatre records, newborn workflows |
| **Blood Bank / PACS / LIS/RIS** | Diagnostics, blood-bank inventory, radiology schedules, lab integration |
| **Billing / Insurance / HIM** | Revenue cycle, billing accounts, invoices, insurance policies, claims, coding |
| **HR / Accounting / Maintenance** | Enterprise operations (personnel, payroll, asset management) |
| **Infection Prevention / Quality** | Infection control, safety, care transitions |
| **Procurement** | Suppliers, purchase orders, goods receipt |
| **Catalog** | Reference catalogs, service definitions, item categories |
| **Backup** | Database backup and restore (API + PowerShell scripts) |
| **Observability** | Runtime health monitoring at `/system-health` |
| **Reporting** | Stock card, movement ledger, low-stock, expiring-stock |
| **Printing** | Printable HTML documents via immutable `PrintRecord` snapshots |
| **Audit / Timeline** | System events, operational timeline views, universal audit writer |
| **Realtime** | WebSocket gateway broadcasting domain events |

---

## 5. Key Engineering Principles

### 5.1 One Source of Truth (SSOT)

The project enforces a strict SSOT rule: **one domain concept = one owner = one canonical implementation = one verifier.**

| Domain | Canonical owner |
|---|---|
| Database schema | `prisma/schema.prisma` |
| App version | `aelia.version.json` |
| Permissions | `apps/api/src/permissions/permission-catalog.ts` |
| Default role templates | `apps/api/src/permissions/default-role-template-policy.ts` |
| Route access | `apps/api/src/access-policy/route-access-policy.ts` |
| Human-readable numbers | `apps/api/src/numbering/business-number.service.ts` |
| Theme and UI tokens | `apps/web/app/theme.css` |
| API contract snapshot | `docs/generated-api-contract.openapi.json` |

### 5.2 Forbidden Patterns

The AGENTS.md and AI_AGENT_SYSTEM_GUIDE.md explicitly forbid:

- Frontend-only permission decisions
- Raw `User.role === ADMIN` bypasses
- Row-count or timestamp business-number fallbacks
- Separate inventory models for Lab/Radiology/Pharmacy
- Duplicated role permission sets in frontend, seed files, or docs
- Hidden fallback behavior (missing config must fail clearly)
- Adding historical documentation to the release package

### 5.3 Access Model

Access is not a simple enum role check — it is a composed policy:

```
role template permissions
+ user role assignments
+ user permission overrides
+ department scope
+ service module access
+ route policy
+ workflow ownership checks
```

**12 default job templates:** Admin, Manager, Supervisor, Doctor, Emergency Nurse, Nurse, Lab, Radiology, Pharmacy, Cashier, Stock, Viewer.

**5 service module access flags:** `PHARMACY_ACCESS`, `LAB_ACCESS`, `RADIOLOGY_ACCESS`, `CASHIER_ACCESS`, `STOCK_ACCESS`.

### 5.4 Business Numbering

All human-readable numbers (visit numbers, admission numbers, order numbers, stock transfer numbers, etc.) must be generated by `BusinessNumberService` with advisory transaction locks. Row-counting is forbidden.

### 5.5 Order Workflow

Strict state machine:

```
Draft/requested → pending approval → approved → fulfillment → dispatched/ready → received/closed
```

---

## 6. Database Layer

- **ORM:** Prisma 7.8.0 with PostgreSQL adapter
- **Schema:** `prisma/schema.prisma` — 143 KB, 191 models, 31 enums
- **Extensions:** `prisma/schema.extensions.sql` — partial unique indexes, trigram indexes, operational indexes
- **Production database:** Native Windows PostgreSQL at `localhost:5433/aelia_health_er`

### 6.1 Database Zone Ownership

| Zone | Key models |
|---|---|
| Access | `User`, `RoleProfile`, `Permission`, `RolePermission`, `UserRoleAssignment`, `UserPermissionOverride`, `UserDepartmentScope` |
| Patient identity | `Patient`, `PatientIdentityEvent`, `PatientMergeAudit` |
| Visit and encounter | `Visit`, `Encounter`, `Attachment`, `MedicationAdministration`, `VisitCharge` |
| Admissions and beds | `Admission`, `Bed`, `AdmissionBedTransfer`, `AdmissionTransferRequest` |
| Departments | `Department`, `DepartmentType`, `DepartmentCapability`, `DepartmentWorkflowSetting` |
| Orders | `Order`, `OrderItem`, `OrderApproval`, `OrderFulfillment`, `OrderReceipt`, `OrderTimeline` |
| Stock | `StockItem`, `MedicineProfile`, `StockItemBatch`, `StockLocation`, `StockBalance`, `StockMovement`, `StockTransfer`, `StockConsumption`, `StockReconciliation` |
| Cashier | `CashierShift`, `Payment`, `PriceItem` |
| Catalog | `CatalogItem`, `CatalogWorkflowTemplate` |
| Procurement | `Supplier`, `PurchaseOrder`, `PurchaseOrderItem`, `GoodsReceipt` |
| Audit/realtime | `AuditEvent`, `TimelineProjection`, `OutboxEvent` |

---

## 7. Testing Strategy

### 7.1 Automated Test Coverage

| Test type | Command | Coverage |
|---|---|---|
| API unit/integration | `pnpm test:api` | Jest specs for all service/controller pairs |
| Web unit | `pnpm test:web` | Jest specs for frontend utilities |
| E2E (development) | `pnpm test:e2e` | Playwright against dev servers on default ports |
| E2E (production build) | `pnpm test:e2e:prod` | Playwright against built API (port 4200) + built Web (port 3200) |
| Type checking | `pnpm verify:typecheck` | TypeScript `tsc --noEmit` for both apps |

### 7.2 E2E Test Suites

- `login.spec.ts` — login flow
- `authenticated-pages.spec.ts` — all authenticated routes accessible per role
- `core-mutation-workflows.spec.ts` — end-to-end clinical workflows
- `stabilization-workflow-regression.spec.ts` — regression suite
- `ui-redesign-baseline.spec.ts` — visual/layout baseline measurements
- `high-risk-workflows.spec.ts` — concurrent mutation safety

### 7.3 Load Testing

Self-contained TypeScript load runner (no external tool dependency):

```powershell
pnpm load:test:30    # 30 virtual users — strict acceptance
pnpm load:test:100   # 100 virtual users — production readiness gate
pnpm load:test:200   # 200 virtual users — capacity measurement
```

**Metrics collected:** avg/p95/p99 response time, error rate, throughput, slowest endpoints, CPU/RAM snapshot, PostgreSQL connection metrics, workflow iteration count.

---

## 8. Verification Infrastructure

The project has **167 scripts** in `/scripts`. The verification system is extremely comprehensive:

### 8.1 Key Verification Gates

| Gate | Command |
|---|---|
| Full source-of-truth verification | `pnpm verify:source-of-truth` |
| Fresh install verification | `pnpm fresh:verify` |
| Full release verification | `pnpm verify:release` |
| Final integrated release proof | `pnpm release:prove` |
| Architecture invariants | `pnpm verify:architecture-invariants` |
| API contract drift check | `pnpm verify:api-contract` |

### 8.2 Domain-Specific Verifiers (sample)

| Verifier | Purpose |
|---|---|
| `verify:permission-catalog` | Permission catalog completeness |
| `verify:role-template-policy` | Default role template correctness |
| `verify:order-workflow-engine` | Order state machine validity |
| `verify:business-numbering-service` | Business number generation correctness |
| `verify:patient-department-ownership` | Patient ownership scope enforcement |
| `verify:no-fallback-policy` | No hidden fallback code |
| `verify:ui-design-system` | No feature-local CSS/palette violations |
| `verify:frontend-type-safety` | Explicit-any ratchet enforcement |
| `verify:explicit-any` | Raw `any` baseline enforcement |
| `verify:file-size-budget` | Controller/service file size limits |
| `verify:hospital-ux-polish` | Hospital UX component wiring |
| `verify:backup-restore` | Backup/restore script integrity |
| `verify:observability-health` | Runtime health monitoring wiring |
| `verify:load-test-suite` | Load test capacity script existence |
| `verify:production-demo-mode-safety` | Demo mode disabled in production templates |

---

## 9. Deployment and Production Readiness

### 9.1 Deployment Profiles

| Profile | Configuration |
|---|---|
| Local development | Native Windows PostgreSQL, `.env.windows-postgres.example` |
| Docker (standard) | `docker-compose.yml` + `.env.docker.example` |
| Docker (alt port 5433) | `.env.docker-port-5433.example` |
| Production Docker | `docker-compose.production.yml` + `.env.production.example` |
| Staging | `.env.staging.example` |
| Cloudflare Tunnel | `CLOUDFLARE_ONE_LINK_SETUP.ps1`, `.env.cloudflare.example` |

### 9.2 Observability

- **Public health endpoints:** `GET /health`, `/health/live`, `/health/ready`, `/health/metrics`
- **Admin runtime health:** `GET /system-health` (requires `ADMIN_ACCESS`)
- **Admin UI page:** `/setup/advanced/system-health`

Monitored: request totals, avg duration, memory, CPU, slow endpoints (>1000ms), slow Prisma queries (>750ms), critical errors, DB connections, Redis status, disk usage, backup age.

### 9.3 Backup System

| Command | Script |
|---|---|
| `pnpm backup:create` | `scripts/backup-db.ps1` |
| `pnpm backup:restore` | `scripts/restore-db.ps1` |
| `pnpm backup:restore:dry-run` | `scripts/restore-db.ps1 --dry-run` |
| `pnpm backup:status` | `scripts/check-backup-status.ts` |

> [!WARNING]
> Backup + Restore Hardening remains **provisional / not completed** until encrypted backup creation and restore dry-run pass on the target deployment host. This is the one open gap in the production readiness checklist.

### 9.4 Security Considerations

- Session cookies issued and validated by API only (not frontend)
- CSRF protection middleware active
- Session data must not be persisted in `localStorage`
- Production/staging `NEXT_PUBLIC_DEMO_MODE=false` enforced by verifier
- Input sanitization pipe on all endpoints
- Correlation ID middleware for request tracing

---

## 10. Documentation Policy

The project enforces strict documentation hygiene via `pnpm verify:release-docs-clean` and `pnpm verify:docs-current`. **Allowed in the release package:**

- Current install/setup docs
- Current architecture docs
- Source-of-truth policy docs
- Workflow guardrails
- Release checklist/status docs
- Design boundary docs for future work

**Not allowed:**
- Historical implementation notes or phase documents
- Audit reports, hotfix reports, temporary planning docs
- Roadmaps or cleanup reports

**Active docs count:** 40 files in `/docs/`.

---

## 11. Feature Boundaries

### Currently Active

| Feature | Status |
|---|---|
| Stock card reporting (`/reports`) | ✅ Active — read-only projections over stock source of truth |
| Printable HTML documents | ✅ Active — immutable `PrintRecord` snapshots |
| Production-build E2E mode | ✅ Active — `pnpm test:e2e:prod` on dedicated ports |
| Hospital UX polish layer | ✅ Active — `HospitalUxPolish.tsx` component family |
| Configurable print templates | ✅ Active — admin-managed `PrintTemplate` source of truth |
| Pharmacy department convergence | ✅ Active — managed `PHARMACY` department is internal owner |
| Patient department ownership | ✅ Active — `PatientDepartmentOwnershipService` + scoped clinical actions |
| Load testing capacity proof | ✅ Active — 30/100/200 user tiers |
| Observability + runtime health | ✅ Active — `/system-health` API + admin UI |
| Canonical UI design system | ✅ Active — `theme.css` is sole token authority |
| Frontend type safety (no-any ratchet) | ✅ Active — per-module explicit-any baseline enforced |
| Smart backup system | ✅ Active — API + PowerShell scripts |

### Intentionally Blocked (Future Work)

| Feature | Status |
|---|---|
| PDF generation | 🚫 Blocked — no approved design boundary |
| Invoice generation | 🚫 Blocked |
| Report export (CSV/Excel from reporting workspace) | 🚫 Blocked |
| Advanced runtime reporting beyond stock cards | 🚫 Blocked |

---

## 12. Strengths

1. **Rigorous source-of-truth discipline** — every business domain has one canonical owner and one verifier. Drift is caught by static verifiers before it reaches production.

2. **Extremely comprehensive verification pipeline** — `pnpm verify:source-of-truth` alone chains ~50+ individual verifier scripts. The `pnpm release:prove` command provides integrated end-to-end proof.

3. **Strong access model** — composed policy engine with department scope, service module access, and workflow ownership checks avoids simplistic `role === ADMIN` shortcuts.

4. **Production-ready deployment infrastructure** — Docker profiles, Cloudflare Tunnel, GitHub Actions release workflow, server sizing guide, monitoring checklist, and security checklist are all present.

5. **Load testing built-in** — self-contained TypeScript load runner with 30/100/200 user tiers. No external tool dependency.

6. **Explicit-any ratchet** — frontend and backend have a baseline enforcement mechanism that prevents type safety from regressing.

7. **Canonical UI design system** — single `theme.css` token authority enforced by a release verifier. No feature can introduce rogue palettes or font stacks.

8. **Realtime architecture** — WebSocket gateway bridges domain events from the outbox pattern to connected clients, scoped by permission/department.

9. **Breadth of domain coverage** — from basic patient registration to ICU episodes, surgery, maternity, blood bank, LIS/RIS, revenue cycle, HR, and enterprise operations. The API surface alone has 528 operations.

---

## 13. Risks and Open Items

| Risk | Severity | Notes |
|---|---|---|
| **Backup not production-accepted** | 🔴 High | Backup + Restore remains provisional until live backup creation + dry-run restore pass on deployment host |
| **Scale of module surface** | 🟡 Medium | 86+ backend modules, 129 frontend routes. Navigation complexity grows with team size |
| **ExplicitAny debt** | 🟡 Medium | A per-module ratchet is in place, but reducing the existing baseline requires ongoing cleanup phases |
| **Specialist modules breadth** | 🟡 Medium | ICU, surgery, maternity, blood-bank, LIS/RIS, revenue cycle, HR, etc. are modeled but their depth/completeness is not fully audited in this report |
| **PDF/export boundary** | 🟢 Low | Intentionally blocked with design; future approval needed before implementation |
| **Windows-only dev environment** | 🟢 Low | PowerShell scripts and native Windows PostgreSQL are the primary development path; Docker is available as alternative |

---

## 14. Recommended Next Steps

1. **Complete backup acceptance** — run `scripts/backup-db.ps1` and `scripts/restore-db.ps1 --dry-run` on the target deployment host and provide evidence to close the provisional backup gap.

2. **Run full release gates** — execute `pnpm release:prove` on the target host to produce integrated runtime proof covering fresh verification, builds, E2E, backup creation, status, and restore dry-run.

3. **Reduce explicit-any baseline** — continue `any` cleanup phases per module, lowering the ratchet baseline after each completed phase using `pnpm update:explicit-any-baseline`.

4. **Audit specialist module depth** — the breadth of advanced modules (ICU, surgery, maternity, blood-bank, LIS/RIS, revenue cycle, HR) should be audited for UI/backend completeness vs. scaffold status.

5. **Establish a realistic seeded demo** — run `pnpm seed:realistic-cases` to populate a meaningful dataset before live acceptance testing.

---

## 15. Quick Reference Commands

```powershell
# Install and fresh start
pnpm install
pnpm fresh:install

# Development
pnpm dev:api       # NestJS API on port 4000
pnpm dev:web       # Next.js web on port 3000

# Core verification
pnpm verify:source-of-truth
pnpm fresh:verify
pnpm verify:release

# Testing
pnpm test:api
pnpm test:web
pnpm test:e2e
pnpm test:e2e:prod

# Release proof
pnpm release:prove

# Load testing
pnpm load:test:30
pnpm load:test:100
pnpm load:test:200

# Backup
pnpm backup:create
pnpm backup:restore:dry-run
pnpm backup:status
```

---

*Report generated by Antigravity using Superpowers brainstorming + exploratory research skills. Based on source-derived analysis of `e:\Downloads\aelia-hms-clean-release` at version `1.3.277`.*
