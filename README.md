<div align="center">

# 📝 TodoApp

**A production-ready, multi-tenant todo application built with ASP.NET Core 10 and Blazor**

[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com)
[![Blazor](https://img.shields.io/badge/Blazor-Server-512BD4?logo=blazor&logoColor=white)](https://blazor.net)
[![EF Core](https://img.shields.io/badge/EF_Core-10.0-green)](https://docs.microsoft.com/ef/core/)
[![Tests](https://img.shields.io/badge/Tests-107%20passing-brightgreen)](#-testing)
[![License](https://img.shields.io/badge/License-MIT-blue)](#-license)

[Features](#-features) · [Architecture](#-architecture) · [Getting Started](#-getting-started) · [API Reference](#-api-reference) · [Configuration](#-configuration) · [Testing](#-testing) · [Contributing](#-contributing)

</div>

---

## ✨ Features

### For Users
- **📋 Task management** — Create, edit, complete, and organise todos with priorities (Low / Medium / High), categories, and due dates
- **🔍 Real-time search** — Server-side search across titles, notes, and categories with debounced input
- **📄 Pagination & sorting** — Configurable page size, sort by title / priority / due date / last updated
- **🗑️ Trash & restore** — Soft-delete with a dedicated trash bin; restore or permanently delete anytime
- **⏰ Overdue alerts** — Dismissible banner when tasks have slipped past their due date
- **🌙 Dark mode** — System-aware dark/light toggle that persists across sessions
- **📱 Responsive layout** — Sidebar collapses on mobile; full-width single-column view

### For Administrators
- **🏢 Multi-tenancy** — Full three-tier tenant isolation (Administrator → TenantAdmin → User)
- **👥 User management** — Create users, assign roles, lock/unlock accounts, manage across tenants
- **🔐 Role-based access** — Granular policy enforcement at every endpoint
- **📊 Audit log** — Every create, update, complete, delete, and restore action recorded with a full diff

### For Operations
- **❤️ Health checks** — `/health/live` (liveness) and `/health/ready` (readiness with DB probe)
- **🔭 Distributed tracing** — OpenTelemetry with ASP.NET Core + EF Core instrumentation
- **📈 Metrics** — Runtime metrics (GC, heap, thread pool) via OpenTelemetry
- **📋 Structured logging** — Serilog with per-request enrichment: `UserId`, `TenantId`, `TraceId`, `SpanId`
- **🔑 Refresh tokens** — 30-day sliding refresh tokens with rotation on use
- **🚦 Rate limiting** — Configurable fixed-window limits per endpoint group

---

## 🏗️ Architecture

TodoApp follows **Onion Architecture** (also known as Clean Architecture), with strict dependency flow: outer layers depend on inner layers, never the reverse.

```
┌──────────────────────────────────────────────────────────────────┐
│                         Presentation                             │
│                                                                  │
│   ┌──────────────────────┐      ┌──────────────────────────┐     │
│   │    TodoApp.Web       │      │      TodoApp.Api          │     │
│   │  Blazor Server UI    │─────▶│  ASP.NET Core Minimal API│     │
│   └──────────────────────┘ JWT  └────────────┬─────────────┘     │
└────────────────────────────────────────────────┼─────────────────┘
                                                 │
┌────────────────────────────────────────────────▼─────────────────┐
│                        Infrastructure                             │
│                     TodoApp.Infrastructure                        │
│         EF Core · SQLite · Identity · JWT · Migrations           │
│                         DatabaseSeeder                           │
└────────────────────────────────────────────────┬─────────────────┘
                                                 │
┌────────────────────────────────────────────────▼─────────────────┐
│                        Application                                │
│                      TodoApp.Application                          │
│           TodoService · AuthService · DTOs · Interfaces          │
└────────────────────────────────────────────────┬─────────────────┘
                                                 │
┌────────────────────────────────────────────────▼─────────────────┐
│                           Domain                                  │
│                        TodoApp.Domain                             │
│           TodoItem · AppUser · Tenant · Enums · Interfaces       │
└──────────────────────────────────────────────────────────────────┘
```

### Project Structure

```
📁 Todo 2/
├── 📁 TodoApp.Domain/               # Core entities and interfaces (no dependencies)
│   ├── 📁 Entities/                 # TodoItem, AppUser, Tenant, AuditLog
│   ├── 📁 Enums/                    # Priority
│   └── 📁 Interfaces/               # ITodoRepository, etc.
│
├── 📁 TodoApp.Application/          # Use-case logic (depends on Domain only)
│   ├── 📁 Services/                 # TodoService, AuthService
│   ├── 📁 DTOs/                     # Request / response models
│   └── DependencyInjection.cs       # AddApplication() extension
│
├── 📁 TodoApp.Infrastructure/       # Data access + external concerns
│   ├── 📁 Data/                     # TodoDbContext, AppUser (Identity)
│   ├── 📁 Repositories/             # TodoRepository (EF Core)
│   ├── 📁 Migrations/               # EF Core migration history
│   ├── 📁 Seeding/                  # DatabaseSeeder (roles + super-admin)
│   └── DependencyInjection.cs       # AddInfrastructure() extension
│
├── 📁 TodoApp.Api/                  # Thin Minimal API layer
│   ├── 📁 Endpoints/                # Auth, Todos, Admin, TenantAdmin
│   ├── 📁 Middleware/               # Global exception handler
│   └── Program.cs                   # AddInfrastructure() + AddApplication() + pipeline
│
├── 📁 TodoApp.Web/                  # Blazor Server frontend
│   ├── 📁 Components/
│   │   ├── 📁 Pages/                # Landing, Login, Home, Trash, Admin, TenantAdmin
│   │   └── 📁 Layout/               # MainLayout, NavMenu
│   ├── 📁 Services/                 # AuthService, TodoApiService
│   ├── 📁 Models/                   # Request/response DTOs
│   └── 📁 wwwroot/                  # CSS, PWA manifest
│
├── 📁 TodoApp.Api.Tests/            # Integration tests (WebApplicationFactory)
│   ├── AuthEndpointsTests.cs
│   ├── TodoEndpointsTests.cs
│   ├── AdminEndpointsTests.cs
│   ├── TenantAdminEndpointsTests.cs
│   └── HealthTests.cs
│
└── 📁 TodoApp.Application.Tests/    # Pure unit tests (NSubstitute, no HTTP)
    ├── TodoServiceTests.cs
    └── AuthServiceTests.cs
```

### Multi-Tenancy Model

```
Administrator  (no TenantId)
  └── manages all tenants and all users platform-wide

TenantAdmin    (scoped TenantId)
  └── manages users within their own tenant only

User           (scoped TenantId)
  └── manages their own todos only
```

All users are created by an administrator — there is no public self-registration. Tenant isolation is enforced at the database query level via EF Core global query filters on every request.

---

## 🚀 Getting Started

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- A terminal (PowerShell, bash, zsh)

### 1. Clone the repository

```bash
git clone https://github.com/cvizzini/claw-todo.git
cd claw-todo
```

### 2. Run the API

```bash
cd TodoApp.Api
dotnet run
```

The API starts on `http://localhost:5227`. On first run it will:
- Create the SQLite database (`todos.db`)
- Run all EF Core migrations automatically
- Seed roles and the default super-admin account

**Default super-admin credentials:**

| Field | Value |
|---|---|
| Email | `admin@todoapp.local` |
| Password | `Admin1234!` |

> ⚠️ Change these in `appsettings.json` before deploying to production.

### 3. Run the Blazor frontend

```bash
# In a separate terminal
cd TodoApp.Web
dotnet run
```

Open `http://localhost:5001` in your browser and sign in with the super-admin credentials above.

### 4. Run all tests

```bash
# Integration tests
cd TodoApp.Api.Tests
dotnet test

# Unit tests
cd TodoApp.Application.Tests
dotnet test
```

Expected output: **107 tests, 0 failures**.

### 5. Explore the API docs

With the API running in Development mode, visit:

```
http://localhost:5227/scalar
```

Interactive Scalar UI with every endpoint documented and try-it-out support.

### 6. Add EF Core migrations

Migrations live in `TodoApp.Infrastructure`. Always specify both projects:

```bash
dotnet ef migrations add <MigrationName> \
  --project TodoApp.Infrastructure \
  --startup-project TodoApp.Api

dotnet ef database update \
  --project TodoApp.Infrastructure \
  --startup-project TodoApp.Api
```

---

## 📡 API Reference

All endpoints are versioned under `/api/v1/`.

### Authentication — `/api/v1/auth`

| Method | Endpoint | Auth | Description |
|--------|----------|------|-------------|
| `POST` | `/login` | — | Sign in, returns JWT + refresh token |
| `POST` | `/refresh` | — | Exchange refresh token for new token pair |
| `GET` | `/me` | ✅ | Get current user profile |

### Todos — `/api/v1/todos`

| Method | Endpoint | Description |
|--------|----------|-------------|
| `GET` | `/` | List todos (paginated, searchable, filterable, sortable) |
| `POST` | `/` | Create a todo |
| `GET` | `/{id}` | Get a single todo |
| `PUT` | `/{id}` | Update a todo |
| `PATCH` | `/{id}/toggle` | Toggle completion status |
| `DELETE` | `/{id}` | Soft-delete (moves to trash) |
| `GET` | `/trash` | List soft-deleted todos |
| `POST` | `/{id}/restore` | Restore from trash |
| `DELETE` | `/{id}/permanent` | Permanently delete from trash |
| `DELETE` | `/trash` | Empty trash |
| `DELETE` | `/completed` | Clear all completed todos |
| `GET` | `/stats` | Counts: total, active, completed, overdue |
| `GET` | `/categories` | Distinct categories used by this user |
| `GET` | `/{id}/audit` | Audit history for a specific todo |

**Pagination & filtering:**

```
GET /api/v1/todos?page=1&pageSize=20&search=groceries&category=Personal&completed=false&sortBy=dueDate&sortDesc=false
```

### Admin — `/api/v1/admin` _(Administrator only)_

| Method | Endpoint | Description |
|--------|----------|-------------|
| `GET` | `/tenants` | List all tenants |
| `POST` | `/tenants` | Create a tenant |
| `GET` | `/tenants/{id}` | Get tenant by ID |
| `PUT` | `/tenants/{id}` | Update tenant name / active status |
| `DELETE` | `/tenants/{id}` | Delete tenant (must have no users) |
| `GET` | `/users` | List all users (optionally filter by `?tenantId=`) |
| `GET` | `/users/{id}` | Get user by ID |
| `POST` | `/users` | Create user with role + tenant assignment |
| `PUT` | `/users/{id}` | Update display name, role, tenant |
| `DELETE` | `/users/{id}` | Hard delete user and all their data |
| `PATCH` | `/users/{id}/lock` | Lock account |
| `PATCH` | `/users/{id}/unlock` | Unlock account |

### Tenant Admin — `/api/v1/tenant` _(TenantAdmin or above)_

Same user management endpoints as Admin, automatically scoped to the caller's tenant. Cannot create or manage Administrators.

### Health — `/health`

| Endpoint | Description |
|----------|-------------|
| `GET /health/live` | Liveness probe — `{ "status": "alive" }` |
| `GET /health/ready` | Readiness probe — includes database connectivity check |
| `GET /health` | Full health report with all checks and durations |

---

## ⚙️ Configuration

All configuration lives in `appsettings.json`. Key sections:

```jsonc
{
  "ConnectionStrings": {
    // SQLite by default — swap for PostgreSQL in production
    "DefaultConnection": "Data Source=todos.db"
  },
  "Jwt": {
    "Key": "CHANGE-THIS-TO-A-LONG-RANDOM-SECRET",  // min 32 chars
    "Issuer": "TodoApp.Api",
    "Audience": "TodoApp.Web",
    "ExpiryHours": "8"
  },
  "Seed": {
    // Super-admin created on first run only
    "AdminEmail": "admin@yourcompany.com",
    "AdminPassword": "ChangeMe1234!",
    "AdminDisplayName": "Super Admin"
  },
  "RateLimiting": {
    "AuthLimit": 10,    // requests per minute on /auth
    "ApiLimit": 100     // requests per minute on /api
  },
  "OpenTelemetry": {
    "ServiceName": "TodoApp.Api",
    "Endpoint": ""      // OTLP endpoint (Seq, Jaeger, etc.) — empty = console in Development
  }
}
```

### Environment variables

Any `appsettings.json` key can be overridden via environment variable using `__` as the separator:

```bash
Jwt__Key=my-super-secret-key
Seed__AdminPassword=SecurePassword123!
OpenTelemetry__Endpoint=http://localhost:4317
```

---

## 🔐 Security Notes

Before going to production:

- [ ] Replace the JWT `Key` with a cryptographically random secret (≥ 32 characters)
- [ ] Change the default seed admin email and password
- [ ] Switch `ConnectionStrings:DefaultConnection` from SQLite to PostgreSQL
- [ ] Set `AllowedHosts` to your specific domain
- [ ] Configure HTTPS and set `ASPNETCORE_URLS`
- [ ] Point `OpenTelemetry:Endpoint` at your observability backend

---

## 🧪 Testing

The test suite uses **xUnit** + **FluentAssertions** with two complementary layers:

### Integration Tests (`TodoApp.Api.Tests`)

Uses `WebApplicationFactory` with a shared in-memory SQLite connection — no mocks, real middleware pipeline. Every test gets a fully seeded environment (admin, tenant, tenant-admin, and regular user).

```bash
cd TodoApp.Api.Tests
dotnet test --verbosity normal
```

| Suite | Tests | Covers |
|-------|-------|--------|
| `AuthEndpointsTests` | 8 | Login, refresh, me, lockout |
| `TodoEndpointsTests` | 26 | Full CRUD, soft delete, audit, search, pagination |
| `AdminEndpointsTests` | 22 | Tenant CRUD, user management, cross-tenant isolation |
| `TenantAdminEndpointsTests` | 9 | Scoped admin operations, role enforcement |
| `HealthTests` | 3 | Liveness, readiness, full report |
| **Subtotal** | **74** | |

### Unit Tests (`TodoApp.Application.Tests`)

Pure unit tests of the Application layer using **NSubstitute** for mocking. No HTTP, no database, no EF Core. Tests service logic in isolation.

```bash
cd TodoApp.Application.Tests
dotnet test --verbosity normal
```

| Suite | Tests | Covers |
|-------|-------|--------|
| `TodoServiceTests` | 22 | Create, update, delete, toggle, trash, restore, stats |
| `AuthServiceTests` | 11 | Login, refresh, JWT validation, role checks |
| **Subtotal** | **33** | |

### Combined

```
Total: 107 tests — Failed: 0, Passed: 107
```

---

## 🛣️ Roadmap

- [ ] **PostgreSQL support** — production-grade database with connection pooling
- [ ] **Docker + Docker Compose** — multi-stage build with Postgres + Seq for local dev
- [ ] **GitHub Actions CI** — build → test → publish on PR
- [ ] **Password reset** — email-based forgot-password flow
- [ ] **Problem Details** — RFC 9457 `application/problem+json` on all error responses
- [ ] **Email verification** — wire up `EmailConfirmed` flag
- [ ] **PWA / offline support** — service worker + offline cache

---

## 🤝 Contributing

Contributions are welcome! Here's how to get started:

1. **Fork** the repository
2. **Create a feature branch**: `git checkout -b feature/my-feature`
3. **Make your changes** — keep all 107 tests green
4. **Add tests** for any new behaviour
5. **Push** and open a **Pull Request**

Please keep PRs focused — one feature or fix per PR makes review much easier.

---

## 📄 License

This project is licensed under the **MIT License** — see the [LICENSE](LICENSE) file for details.

---

<div align="center">

Built with ❤️ using [ASP.NET Core 10](https://asp.net) · [Blazor](https://blazor.net) · [Entity Framework Core](https://docs.microsoft.com/ef/core/) · [OpenTelemetry](https://opentelemetry.io)

</div>
