# PatchHound

Self-hosted, multi-tenant vulnerability management platform with Microsoft Defender ingestion, role-based access, SLA-driven remediation tracking, real-time notifications, and audit trails.

## Architecture

```
+-----------------------+         +-----------------------+
| Frontend (Vite/React) | <-----> | PatchHound API (.NET 10)   |
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
                                    | PatchHound Worker (.NET)  |
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

2. Fill required values in `.env` (minimum):
- `POSTGRES_PASSWORD`
- `AZURE_AD_CLIENT_ID`, `AZURE_AD_AUDIENCE`
- `SESSION_SECRET` (minimum 32 characters)
- `ENTRA_CLIENT_SECRET`

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
dotnet build PatchHound.slnx
dotnet test PatchHound.slnx -v minimal
```

Run API + Worker locally (migrations are applied automatically on startup):

```bash
dotnet run --project src/PatchHound.Api
dotnet run --project src/PatchHound.Worker
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

For local frontend auth/API integration, set runtime env vars before `npm run dev`:

```bash
export API_BASE_URL=http://localhost:8080/api
export SESSION_SECRET=replace-with-32+chars
export ENTRA_CLIENT_ID=...
export ENTRA_CLIENT_SECRET=...
export ENTRA_TENANT_ID=common
export ENTRA_REDIRECT_URI=http://localhost:3000/auth/callback
export ENTRA_SCOPES="openid profile email"
export FRONTEND_ORIGIN=http://localhost:3000
```

## Configuration Reference

Reference defaults are in `.env.example`. Docker Compose consumes this file directly.

- Database: `POSTGRES_DB`, `POSTGRES_USER`, `POSTGRES_PASSWORD`
- API auth: `AZURE_AD_CLIENT_ID`, `AZURE_AD_TENANT_ID`, `AZURE_AD_AUDIENCE`
- API CORS/logout URL: `FRONTEND_ORIGIN`
- OpenBao: `OPENBAO_ADDR`, `OPENBAO_INTERNAL_ADDR`, `OPENBAO_TOKEN`, `OPENBAO_KV_MOUNT`
- SMTP: `SMTP_HOST`, `SMTP_PORT`, `SMTP_USERNAME`, `SMTP_PASSWORD`
- Frontend server runtime: `API_BASE_URL`, `SESSION_SECRET`, `ENTRA_CLIENT_ID`, `ENTRA_CLIENT_SECRET`, `ENTRA_TENANT_ID`, `ENTRA_REDIRECT_URI`, `ENTRA_SCOPES`, `FRONTEND_ORIGIN`

Notes:
- `VITE_*` variables are not used by the current frontend server runtime.
- API and Worker execute `Database.Migrate()` at startup, so the DB user must have migration privileges.

## OpenBao Token Setup

PatchHound uses OpenBao for tenant source secrets and global enrichment source secrets. The API and worker need an `OPENBAO_TOKEN` with read/write access to:

- `patchhound/data/tenants/*`
- `patchhound/data/system/enrichment-sources/*`

1. Start and initialize OpenBao:

```bash
docker compose up -d openbao
docker compose exec openbao bao operator init
docker compose exec openbao bao operator unseal
docker compose exec openbao bao login
```

2. Enable the KV mount used by PatchHound:

```bash
docker compose exec openbao bao secrets enable -path=patchhound kv-v2
```

3. Create a policy and token for PatchHound:

```bash
docker compose exec openbao sh -c 'cat >/tmp/patchhound-policy.hcl <<EOF
path "patchhound/data/tenants/*" {
  capabilities = ["create", "update", "read"]
}
path "patchhound/data/system/enrichment-sources/*" {
  capabilities = ["create", "update", "read"]
}
EOF
bao policy write patchhound /tmp/patchhound-policy.hcl
bao token create -policy=patchhound'
```

4. Put the generated token in `.env`:

```bash
OPENBAO_TOKEN=replace-with-generated-token
```

5. Restart the backend services:

```bash
docker compose up -d --build api worker
```

If `OPENBAO_TOKEN` is missing or lacks access to one of the required paths, tenant or enrichment secret writes will fail and the worker will not be able to read protected credentials.

If you run `bao` outside the container or before the container environment is refreshed, use the HTTP address explicitly:

```bash
docker compose exec openbao bao status -address=http://127.0.0.1:8200
docker compose exec openbao bao operator init -address=http://127.0.0.1:8200
```

## Entra ID Application Configuration

Configure one Microsoft Entra app registration for local and Docker development:

1. Create an app registration in Entra ID.
2. Set redirect URI (Web): `http://localhost:3000/auth/callback`.
3. Create a client secret and store it in `.env` as `ENTRA_CLIENT_SECRET`.
4. Expose an API / configure audience used by backend token validation (`AZURE_AD_AUDIENCE`).
5. Add delegated permissions for sign-in (`openid`, `profile`, `email`) and grant admin consent if required by tenant policy.
6. Use matching IDs in `.env`:
- `AZURE_AD_CLIENT_ID` and `ENTRA_CLIENT_ID`: app/client ID
- `AZURE_AD_TENANT_ID` and `ENTRA_TENANT_ID`: tenant ID or `common`
- `ENTRA_REDIRECT_URI`: `http://localhost:3000/auth/callback`

Recommended for local development:
- Keep `AZURE_AD_TENANT_ID=common` if multi-tenant sign-in is required.
- Ensure `FRONTEND_ORIGIN` matches browser URL exactly (`http://localhost:3000`).

## Setup Wizard

On first startup, the frontend redirects to `/setup` until initialization is complete.
The wizard creates:

- Initial tenant
- Initial Global Admin user and role assignment
- Tenant settings payload

## Troubleshooting

- `createServerFn(...).validator is not a function`:
Use `.inputValidator(...)` in TanStack Start server functions (already required in this repo version).

- Login redirect loop or auth callback failure:
Verify `ENTRA_REDIRECT_URI`, `ENTRA_CLIENT_ID`, `ENTRA_CLIENT_SECRET`, and `FRONTEND_ORIGIN` are set and consistent.

- API returns 401 for valid sign-in:
Check `AZURE_AD_AUDIENCE` and token `aud` claim alignment; confirm API app registration audience value.

- Frontend cannot reach API:
For local run, set `API_BASE_URL=http://localhost:8080/api`. In Docker, frontend uses `http://api:8080/api`.

- Database startup/migration errors:
Confirm Postgres is running and credentials in `.env` match `POSTGRES_*`; DB user must be allowed to run migrations.

- Session/cookie errors:
Set `SESSION_SECRET` to a strong value with at least 32 characters.

## Design and Standards

- Design: `docs/plans/2026-03-06-vulnerability-management-design.md`
- Code standards: `docs/plans/2026-03-06-code-standards.md`
- Implementation plan: `docs/plans/2026-03-06-patchhound-implementation-plan.md`
