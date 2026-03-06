# Vigil

Self-hosted, multi-tenant vulnerability management platform with Microsoft Defender ingestion, role-based access, SLA-driven remediation tracking, real-time notifications, and audit trails.

## Architecture

```
+-----------------------+         +-----------------------+
| Frontend (Vite/React) | <-----> | Vigil API (.NET 10)   |
| TanStack Router/Query |  HTTPS  | REST + SignalR        |
+-----------------------+         +-----------+-----------+
                                              |
                                              | EF Core
                                              v
                                    +-----------------------+
                                    | PostgreSQL            |
                                    +-----------------------+
                                              ^
                                              |
                                    +---------+------------+
                                    | Vigil Worker (.NET)  |
                                    | Defender ingestion   |
                                    +----------------------+
```

## Prerequisites

- Docker + Docker Compose
- .NET SDK 10.0+
- Node.js 22+
- npm 10+

## Quick Start (Docker)

1. Copy environment file:

```bash
cp .env.example .env
```

2. Fill required secrets in `.env`.

3. Build and run:

```bash
docker compose build
docker compose up -d
```

4. Access services:

- Frontend: `http://localhost:3000`
- API: `http://localhost:8080`
- Postgres: `localhost:5432`

## Development Setup

### Backend

```bash
dotnet build Vigil.slnx
dotnet test Vigil.slnx -v minimal
```

### Frontend

```bash
cd frontend
npm install
npm run lint
npm run build
npm test
npm run dev
```

## Environment Variables

Reference defaults are in `.env.example`.

- Database: `POSTGRES_DB`, `POSTGRES_USER`, `POSTGRES_PASSWORD`
- API Auth: `AZURE_AD_CLIENT_ID`, `AZURE_AD_TENANT_ID`, `AZURE_AD_AUDIENCE`
- API CORS: `FRONTEND_ORIGIN`
- SMTP: `SMTP_HOST`, `SMTP_PORT`, `SMTP_USERNAME`, `SMTP_PASSWORD`
- Defender worker: `DEFENDER_CLIENT_ID`, `DEFENDER_CLIENT_SECRET`, `DEFENDER_TENANT_ID`, `DEFENDER_API_BASE_URL`, `DEFENDER_TOKEN_SCOPE`
- Frontend runtime build args: `VITE_API_URL`, `VITE_SIGNALR_URL`, `VITE_ENTRA_CLIENT_ID`, `VITE_ENTRA_AUTHORITY`, `VITE_API_SCOPES`

## Setup Wizard

On first startup, the frontend redirects to `/setup` until initialization is complete.
The wizard creates:

- Initial tenant
- Initial Global Admin user and role assignment
- Tenant settings payload

## Design and Standards

- Design: `docs/plans/2026-03-06-vulnerability-management-design.md`
- Code standards: `docs/plans/2026-03-06-code-standards.md`
- Implementation plan: `docs/plans/2026-03-06-vigil-implementation-plan.md`
