<div align="center">

# 📝 TodoApp

**A production-ready, multi-tenant todo application built with ASP.NET Core 10 and Blazor**

[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com)
[![Blazor](https://img.shields.io/badge/Blazor-Server-512BD4?logo=blazor&logoColor=white)](https://blazor.net)
[![EF Core](https://img.shields.io/badge/EF_Core-10.0-green)](https://docs.microsoft.com/ef/core/)
[![Tests](https://img.shields.io/badge/Tests-74%20passing-brightgreen)](./TodoApp.Api.Tests)
[![License](https://img.shields.io/badge/License-MIT-blue)](#license)

[Features](#-features) · [Architecture](#-architecture) · [Getting Started](#-getting-started) · [API Reference](#-api-reference) · [Configuration](#-configuration) · [Contributing](#-contributing)

</div>

---

## ✨ Features

### For Users
- **📋 Task management** — Create, edit, complete, and organise todos with priorities (Low / Medium / High), categories, and due dates
- **🔍 Real-time search** — Server-side search across titles, notes, and categories
- **📄 Pagination** — Smooth, configurable pagination with sort and filter controls
- **🗑️ Trash & restore** — Soft-delete with a dedicated trash bin; restore or permanently delete anytime
- **⏰ Overdue alerts** — Instant banner when tasks have slipped past their due date
- **🌙 Dark mode** — System-aware dark/light toggle that persists across sessions
- **📱 PWA** — Installable as a progressive web app with offline support

### For Administrators
- **🏢 Multi-tenancy** — Full three-tier tenant isolation (Administrator → TenantAdmin → User)
- **👥 User management** — Create users, assign roles, lock/unlock accounts, manage across tenants
- **🔐 Role-based access** — Granular policy enforcement at every endpoint
- **📊 Audit log** — Every create, update, complete, delete, and restore action is recorded with a full diff

### For Operations
- **❤️ Health checks** — `/health/live` (liveness) and `/health/ready` (readiness with DB probe)
- **🔭 Distributed tracing** — OpenTelemetry traces with ASP.NET Core + EF Core instrumentation
- **📈 Metrics** — Runtime metrics (GC, heap, thread pool) via OpenTelemetry
- **📋 Structured logging** — Serilog with per-request enrichment: `UserId`, `TenantId`, `TraceId`, `SpanId`
- **🔑 Refresh tokens** — 30-day sliding refresh tokens with rotation on use
- **🚦 Rate limiting** — Configurable fixed-window limits per endpoint group

---

## 🏗️ Architecture

```
┌─────────────────────────────────────────────────────┐
│                    TodoApp.Web                      │
│              Blazor Server Frontend                  │
│    Landing · Todos · Trash · Admin · Tenant Mgmt    │
└──────────────────────┬──────────────────────────────┘
                       │ HTTP + JWT
┌──────────────────────▼──────────────────────────────┐
│                    TodoApp.Api                       │
│           ASP.NET Core 10 Minimal API                │
│                                                     │
│  ┌────────────┐  ┌──────────────┐  ┌─────────────┐  │
│  │   /auth    │  │   /todos     │  │   /admin    │  │
│  │  login     │  │  CRUD + soft │  │  tenants +  │  │
│  │  refresh   │  │  delete +    │  │  users      │  │
│  │  me        │  │  audit log   │  │             │  │
│  └────────────┘  └──────────────┘  └─────────────┘  │
│                                                     │
│  ┌─────────────────────────────────────────────────┐ │
│  │  Middleware: Rate Limiting · Global Exceptions  │ │
│  │  Serilog · OpenTelemetry · Health Checks        │ │
│  └─────────────────────────────────────────────────┘ │
│                                                     │
│  ┌────────────────────────────────────────────────┐  │
│  │  EF Core 10 + ASP.NET Core Identity (SQLite)   │  │
│  └────────────────────────────────────────────────┘  │
└─────────────────────────────────────────────────────┘
```

### Project Structure

```
📁 todo 2/
├── 📁 TodoApp.Api/              # ASP.NET Core 10 Minimal API
│   ├── 📁 Data/                 # DbContext, Identity setup
│   ├── 📁 Endpoints/            # Auth, Todos, Admin, TenantAdmin
│   ├── 📁 Middleware/           # Global exception handler
│   ├── 📁 Migrations/           # EF Core migration history
│   ├── 📁 Models/               # Domain entities
│   └── Program.cs               # App wiring, DI, middleware pipeline
│
├── 📁 TodoApp.Web/              # Blazor Server frontend
│   ├── 📁 Components/
│   │   ├── 📁 Pages/            # Home, Login, Landing, Trash, Admin, TenantAdmin
│   │   └── 📁 Layout/           # MainLayout, NavMenu
│   ├── 📁 Services/             # AuthService, TodoApiService
│   ├── 📁 Models/               # Request/response DTOs
│   └── 📁 wwwroot/              # CSS, JS, PWA manifest, service worker
│
└── 📁 TodoApp.Api.Tests/        # xUnit integration tests (74 tests)
    ├── CustomWebApplicationFactory.cs
    ├── TestBase.cs
    ├── AuthEndpointsTests.cs
    ├── TodoEndpointsTests.cs
    ├── AdminEndpointsTests.cs
    ├── TenantAdminEndpointsTests.cs
    └── HealthTests.cs
```

### Multi-Tenancy Model

```
Administrator  (null TenantId)
  └── can manage all tenants and all users platform-wide

TenantAdmin    (scoped TenantId)
  └── can manage users within their own tenant only

User           (scoped TenantId)
  └── can manage their own todos only
```

All users are created by an administrator — there is no public self-registration. Tenant isolation is enforced at the database query level on every request.

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
- Run all migrations automatically
- Seed a super-admin account

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

Open `http://localhost:5001` in your browser. Sign in with the super-admin credentials above.

### 4. Run the tests

```bash
cd TodoApp.Api.Tests
dotnet test
```

Expected output:
```
Passed! - Failed: 0, Passed: 74, Skipped: 0, Total: 74
```

### 5. Explore the API docs

With the API running in Development mode, visit:

```
http://localhost:5227/scalar
```

Interactive Scalar UI with every endpoint documented and try-it-out support.

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
| `DELETE` | `/{id}` | Soft-delete (moves to trash) |
| `POST` | `/{id}/complete` | Toggle completion |
| `GET` | `/trash` | List soft-deleted todos |
| `POST` | `/{id}/restore` | Restore from trash |
| `DELETE` | `/{id}/permanent` | Permanently delete from trash |
| `DELETE` | `/trash` | Empty trash |
| `GET` | `/stats` | Counts: total, completed, overdue, trash |
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
| `DELETE` | `/users/{id}` | Hard delete user + all their data |
| `PATCH` | `/users/{id}/lock` | Lock account |
| `PATCH` | `/users/{id}/unlock` | Unlock account |

### Tenant Admin — `/api/v1/tenant` _(TenantAdmin or above)_

Same user management endpoints as Admin, but automatically scoped to the caller's tenant. Cannot create or manage Administrators.

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
    // Super-admin seeded on first run
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
    "Endpoint": ""      // Set to your OTLP endpoint (Seq, Jaeger, etc.)
                        // Leave empty for console output in Development
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

## 🛣️ Roadmap

Things planned but not yet implemented:

- [ ] **Clean Architecture refactor** — split into Domain / Application / Infrastructure / Api layers
- [ ] **PostgreSQL support** — production-grade database with connection pooling
- [ ] **Docker + Docker Compose** — multi-stage build with Postgres + Seq for local dev
- [ ] **GitHub Actions CI** — build → test → publish on PR
- [ ] **Password reset** — email-based forgot-password flow
- [ ] **Problem Details** — RFC 9457 `application/problem+json` on all error responses
- [ ] **PATCH support** — JSON Merge Patch for partial todo updates
- [ ] **Email verification** — wire up `EmailConfirmed` flag

---

## 🧪 Testing

The test suite uses **xUnit** + **FluentAssertions** with a real `WebApplicationFactory` — no mocks. Every test runs against an in-memory SQLite database with a fully seeded environment.

```bash
dotnet test --verbosity normal
```

| Suite | Tests | Covers |
|-------|-------|--------|
| `AuthEndpointsTests` | 8 | Login, refresh, me, lockout |
| `TodoEndpointsTests` | 26 | Full CRUD, soft delete, audit, search, pagination |
| `AdminEndpointsTests` | 22 | Tenant CRUD, user management, cross-tenant isolation |
| `TenantAdminEndpointsTests` | 9 | Scoped admin operations, role enforcement |
| `HealthTests` | 3 | Liveness, readiness, full report |
| **Total** | **74** | **0 failures** |

---

## 🤝 Contributing

Contributions are welcome! Here's how to get started:

1. **Fork** the repository
2. **Create a feature branch**: `git checkout -b feature/my-feature`
3. **Make your changes** — keep the 74 tests green
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
