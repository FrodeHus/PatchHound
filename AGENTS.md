# Repository Guidelines

## Project Structure & Module Organization
- `src/` contains .NET projects:
- `src/PatchHound.Api` REST API + auth + hubs.
- `src/PatchHound.Core` domain entities, enums, interfaces, business services.
- `src/PatchHound.Infrastructure` EF Core, repositories, integrations (AI, SMTP, Defender).
- `src/PatchHound.Worker` background workers (ingestion, SLA checks).
- `tests/PatchHound.Tests` contains backend unit/integration tests (xUnit).
- `frontend/` is the TanStack Start app (React + Vite). Key folders:
- `frontend/src/api` server functions (`*.functions.ts`) and schemas (`*.schemas.ts`).
- `frontend/src/routes` route files.
- `frontend/src/components` UI feature modules.
- `docs/plans` holds design/implementation notes.

## Build, Test, and Development Commands
- Backend build: `dotnet build PatchHound.slnx`
- Backend tests: `dotnet test PatchHound.slnx -v minimal`
- Frontend setup: `cd frontend && npm install`
- Frontend dev server: `npm run dev`
- Frontend lint: `npm run lint`
- Frontend tests: `npm test`
- Frontend production build: `npm run build`
- Full stack via Docker: `docker compose up -d --build`

## Coding Style & Naming Conventions
- C#: 4-space indentation, `PascalCase` for types/methods, `camelCase` for locals/parameters, one class per file.
- TypeScript/React: 2-space indentation, `PascalCase` components, `camelCase` variables/functions.
- Keep API contracts in `*.schemas.ts` (Zod) and server calls in `*.functions.ts`.
- Prefer explicit imports and small, feature-scoped components.
- Run `npm run lint` before opening a PR.

## Testing Guidelines
- Backend: xUnit tests in `tests/PatchHound.Tests`, file pattern `*Tests.cs`.
- Frontend: Vitest (`npm test`), place tests near related code or feature folder.
- Add/adjust tests for behavior changes in services, repositories, and API/server functions.
- No enforced coverage gate currently; prioritize meaningful assertions for changed code paths.

## Commit & Pull Request Guidelines
- Follow Conventional Commits (seen in history): `feat: ...`, `fix: ...`, `chore: ...`.
- Keep commits scoped and atomic; avoid mixing unrelated frontend/backend refactors.
- PRs should include:
- concise summary and motivation,
- impacted areas (`src/PatchHound.Api/...`, `frontend/src/...`),
- test evidence (command output summary),
- screenshots/GIFs for UI changes.
- Link related issue/task when available.

## Security & Configuration Tips
- Copy `.env.example` to `.env`; never commit secrets.
- Validate required env vars for API auth, SMTP, and Defender integrations before deployment.
