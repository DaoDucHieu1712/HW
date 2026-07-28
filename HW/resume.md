# Nguyen Van A

**Mid-level Full-stack Developer — .NET & React** · 4 years experience
Ho Chi Minh City, Vietnam · Open to hybrid / remote

📧 nguyenvana.dev@example.com · 📱 +84 90 123 4567
🔗 linkedin.com/in/nguyenvana-dev · 💻 github.com/nguyenvana-dev

---

## 1. Summary

Full-stack developer with 4 years shipping ASP.NET Core APIs and React front-ends for e-commerce and internal
business platforms. Comfortable owning a feature end to end — schema, API, UI, tests, deploy — and used to
working in Clean Architecture / CQRS backends paired with typed React + TypeScript clients. Recently led the
migration of a 200k-LOC monolith module to vertical slices and rebuilt its admin UI in React, cutting average
endpoint latency by 40% and first-paint time by half.

---

## 2. Work Experience

### ABC Tech Solutions — Full-stack Developer (.NET / React)
*Mar 2025 – Present · Ho Chi Minh City · Product company, ~60 engineers*

#### Project 1 — RetailHub: Multi-tenant Order & Inventory Platform
> **Duration:** Mar 2025 – Present (1 yr 5 mo) · **Team:** 9 (1 PO, 1 QA, 5 BE, 2 FE) · **Role:** Backend owner of the Order module + React feature dev
> **Domain:** Retail SaaS serving ~1,200 daily active merchants; ~80k orders/day at peak
> **Stack:** .NET 8, ASP.NET Core Web API, EF Core 8, MariaDB, Redis, RabbitMQ, Kafka · React 18, TypeScript, Vite, TanStack Query, Tailwind CSS · Docker, Azure DevOps

- Refactored the order module from a fat service layer into CQRS vertical slices (MediatR commands/queries +
  pipeline behaviors for validation, transactions, logging), reducing average handler size from ~400 to ~80
  lines and making a new endpoint a 1-file change.
- Introduced the transactional **Outbox pattern** so domain events and RabbitMQ messages ship atomically with
  the database write — eliminated a recurring class of "order paid but stock never reserved" support tickets.
- Cut the inventory dashboard query from 4.2 s to 380 ms via projections replacing N+1 lazy loads plus two
  covering indexes; paired it with a virtualized React table that renders 10k rows without jank.
- Rebuilt the merchant admin UI as a React 18 + TypeScript SPA (React Router, TanStack Query, Zustand);
  code-splitting and query caching brought first contentful paint from 3.1 s to 1.4 s.
- Generated the front-end API client and types from the backend OpenAPI schema, turning DTO drift into a
  compile error in the UI instead of a runtime bug.
- Wrote Testcontainers integration tests (MySQL + RabbitMQ) and React Testing Library specs for checkout;
  module coverage 22% → 68%.

#### Project 2 — Supplier Price Sync Service
> **Duration:** Sep 2025 – Jan 2026 (5 mo) · **Team:** 3 (2 BE, 1 QA) · **Role:** Sole backend developer
> **Goal:** Replace nightly CSV imports from 40+ suppliers with near-real-time price and stock ingestion
> **Stack:** .NET 8 Worker Service, Kafka, MariaDB, Redis, Serilog + Seq, Docker

- Built an idempotent Kafka consumer (dedup by message key + processed-message table) handling ~150k
  messages/day with exponential-backoff retry and dead-letter routing.
- Designed the price-change reconciliation job so a partial supplier feed never wipes valid prices; reduced
  bad-price incidents from ~4/month to zero over the following two quarters.
- Added a small React ops screen for the support team to inspect DLQ messages and replay them without a
  developer, removing roughly 3 escalations a week.
- Instrumented consumer lag and failure counts into Seq dashboards with alert thresholds.

---

### XYZ Software Outsourcing — .NET Developer
*Jul 2023 – Feb 2025 · Ho Chi Minh City · Outsourcing, 2 concurrent client projects*

#### Project 1 — HRMS: HR Management System *(client: Japanese manufacturing group)*
> **Duration:** Jul 2023 – Jun 2024 (1 yr) · **Team:** 6 (1 BA, 1 QA, 4 devs) · **Role:** Backend developer
> **Scope:** Employee records, leave approval workflow, payroll export for ~2,500 employees
> **Stack:** .NET 6, ASP.NET Core Web API, EF Core, SQL Server, ClosedXML, Docker

- Implemented ~25 REST endpoints with JWT authentication, role-based authorization, and FluentValidation
  request validation.
- Migrated data access from ADO.NET stored-procedure calls to EF Core with a repository + unit-of-work layer,
  removing ~3,000 lines of duplicated code and making unit testing possible.
- Built a scheduled `IHostedService` worker exporting payroll to Excel via ClosedXML, replacing a manual
  process that cost the client's HR team ~4 hours per cycle.
- Handled UAT defect triage directly with the client BA across a 6-week acceptance period.

#### Project 2 — TrackPort: Logistics Tracking Portal *(client: regional freight forwarder)*
> **Duration:** Jul 2024 – Feb 2025 (8 mo) · **Team:** 5 (1 BA, 1 QA, 2 BE, 1 FE) · **Role:** Full-stack developer
> **Scope:** Shipment tracking dashboard for internal staff + customer-facing status lookup
> **Stack:** .NET 6, ASP.NET Core, EF Core, SQL Server, SignalR · React 17, Redux Toolkit · GitHub Actions

- Built the tracking dashboard in React + Redux Toolkit — filterable shipment list, map view, role-gated
  routes — working from Figma hand-offs with a designer.
- Added a SignalR notification hub for real-time shipment status, consumed via a custom React hook, replacing a
  30-second polling loop that had accounted for ~60% of API traffic.
- Implemented ~15 endpoints for shipment search and status history, including a public lookup endpoint with
  rate limiting.
- Set up the team's first CI pipeline (GitHub Actions: build → test → Docker image → staging deploy), taking
  releases from manual FTP uploads to one click.

---

### StartUp Labs — Junior .NET Developer *(Intern → Full-time)*
*Sep 2022 – Jun 2023 · Ho Chi Minh City*

#### Project — Internal Operations Admin Portal
> **Duration:** Sep 2022 – Jun 2023 (10 mo) · **Team:** 4 · **Role:** Junior developer (maintenance + small features)
> **Stack:** ASP.NET MVC 5, Entity Framework 6, SQL Server, jQuery, Bootstrap

- Maintained CRUD screens and report exports; closed ~120 Jira tickets across the period.
- Converted several server-rendered pages to jQuery/AJAX partial updates — first exposure to client-side state
  handling that later carried over to React.
- Wrote SQL Server queries and stored procedures for management reports; learned indexing fundamentals from
  production slow-query reviews with the tech lead.

---

## 3. Technical Skills

| Area | Technologies |
|---|---|
| **Backend languages** | C# (10/12), SQL |
| **Backend frameworks** | ASP.NET Core 6/8 (Web API, Minimal API), Entity Framework Core, Dapper, MediatR, FluentValidation, Mapster/AutoMapper, SignalR |
| **Frontend** | React 17/18, TypeScript, JavaScript (ES2022), React Hooks, React Router, TanStack Query, Redux Toolkit, Zustand, React Hook Form, Vite, Tailwind CSS, MUI, Ant Design |
| **Architecture** | Clean Architecture, CQRS, Repository/Unit of Work, DDD (aggregates, domain events), Outbox pattern, REST, OpenAPI-generated clients, gRPC (basic) |
| **Data** | SQL Server, MySQL/MariaDB, PostgreSQL, Redis, EF migrations, query tuning & indexing |
| **Messaging** | RabbitMQ, Kafka (consumer groups, at-least-once handling), MassTransit |
| **Testing** | xUnit, NUnit, Moq, FluentAssertions, Testcontainers, WebApplicationFactory integration tests · Jest, React Testing Library, Playwright (basic) |
| **DevOps** | Docker, Docker Compose, Azure DevOps Pipelines, GitHub Actions, IIS & Linux deployment, Serilog + Seq, basic Kubernetes |
| **Cloud** | Azure (App Service, Static Web Apps, Blob Storage, Service Bus, Key Vault), AWS S3 |
| **Tools** | Git, Jira, Swagger/OpenAPI, Postman, SonarQube, ESLint/Prettier, Figma (hand-off), Rider / Visual Studio / VS Code |

---

## 4. Personal Project

### Vocabulary & Habit Tracker — *open source*
> **Stack:** .NET 8, EF Core, MariaDB, RabbitMQ/Kafka · React + TypeScript, TanStack Query, IndexedDB
> **Repo:** `github.com/nguyenvana-dev/vocab-api`

- Clean Architecture solution (Domain / Application / Infrastructure / Api) with CQRS via MediatR, Mapster
  mapping, soft delete, and a spaced-repetition review scheduler.
- React flashcard client with an offline-capable review queue (IndexedDB), optimistic updates, and a
  keyboard-driven study mode.
- Domain events dispatched through a transactional outbox with a background processor; message bus abstraction
  swappable between RabbitMQ and Kafka by configuration.
- ~85% test coverage on the Application layer.

---

## 5. Education

**B.Sc. Software Engineering** — University of Information Technology, VNU-HCM · 2018 – 2022
GPA 3.4/4.0 · Graduation project: distributed file-sync service in C#

---

## 6. Certifications

- Microsoft Certified: Azure Developer Associate (AZ-204) — 2025
- Microsoft Certified: Azure Fundamentals (AZ-900) — 2023

---

## 7. Languages

- **Vietnamese** — native
- **English** — professional working proficiency (daily written communication with overseas clients; comfortable in stand-ups)

---

*Sample resume template — replace all names, dates, metrics, and links with your own. Keep metrics concrete
(numbers, before/after) and drop any bullet you can't discuss for 5 minutes in an interview.*
