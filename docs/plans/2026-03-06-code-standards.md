# Code Standards Specification

**Date:** 2026-03-06
**Priority:** Maintainability and Security

## 1. General Principles

- **YAGNI** — Do not build what is not needed yet.
- **Single Responsibility** — Each class, function, and component does one thing.
- **Explicit over implicit** — Prefer clarity over cleverness.
- **Fail fast** — Validate at boundaries, throw early on invalid state.
- **Immutable by default** — Use `readonly`, `const`, and immutable data structures where possible.

## 2. .NET Backend Standards

### 2.1 Project Structure

```
src/
  Vigil.Api/           # Web API host
  Vigil.Worker/        # Ingestion worker host
  Vigil.Core/          # Domain models, interfaces, business logic
  Vigil.Infrastructure/# EF Core, external service clients, email
  Vigil.Tests/         # Unit + integration tests
```

- Core has zero dependencies on Infrastructure or API projects.
- Infrastructure implements interfaces defined in Core.
- API and Worker depend on Core and Infrastructure.

### 2.2 Naming Conventions

| Element | Convention | Example |
|---------|-----------|---------|
| Namespace | PascalCase, matches folder | `Vigil.Core.Vulnerabilities` |
| Class / Record | PascalCase | `RemediationTask` |
| Interface | `I` prefix + PascalCase | `IVulnerabilitySource` |
| Method | PascalCase | `GetByTenantAsync` |
| Parameter / Local | camelCase | `tenantId` |
| Private field | `_camelCase` | `_dbContext` |
| Constant | PascalCase | `MaxRetryCount` |
| Enum member | PascalCase | `Severity.Critical` |

### 2.3 Coding Practices

- **Async all the way** — All I/O-bound methods are `async Task<T>`. Never use `.Result` or `.Wait()`.
- **CancellationToken** — Pass `CancellationToken` through all async method chains.
- **Nullable reference types** — Enabled project-wide (`<Nullable>enable</Nullable>`). No suppression operators (`!`) unless justified with a comment.
- **Records for DTOs** — Use `record` types for request/response DTOs and value objects.
- **No public setters on domain entities** — Use methods to enforce invariants.
- **Result pattern for business operations** — Return `Result<T>` or similar instead of throwing exceptions for expected failure cases. Reserve exceptions for unexpected errors.
- **No magic strings** — Use constants or enums for status values, role names, claim types.
- **Dependency injection** — Constructor injection only. No service locator pattern.
- **Configuration** — Use `IOptions<T>` pattern. No direct `IConfiguration` reads outside startup.

### 2.4 Security

- **Input validation** — Validate all API input using FluentValidation or Data Annotations. Reject invalid input at the controller level before it reaches business logic.
- **Parameterized queries** — EF Core handles this by default. Never concatenate SQL strings.
- **Tenant isolation** — Global query filters on all tenant-scoped entities via EF Core. Every query automatically filters by the current user's accessible tenants.
- **Authorization** — Use policy-based authorization (`[Authorize(Policy = "SecurityAnalyst")]`). Define policies in a central location.
- **Secret management** — No secrets in code or config files. Use environment variables or a secret manager. Docker Compose uses `.env` files (gitignored).
- **CORS** — Restrict to the frontend origin. No wildcards in production.
- **Rate limiting** — Apply rate limiting middleware to all API endpoints.
- **Audit logging** — Implemented as EF Core interceptor or middleware. Automatic, not opt-in per endpoint.
- **Dependency scanning** — Use `dotnet list package --vulnerable` in CI. No known-vulnerable packages in production.

### 2.5 Entity Framework Core

- **Migrations** — Code-first migrations. Each migration has a descriptive name. Never edit a migration after it has been applied to any environment.
- **DbContext lifetime** — Scoped (one per request). Worker uses `IDbContextFactory` for explicit lifetime control.
- **Query patterns** — Use `AsNoTracking()` for read-only queries. Use projections (`Select`) instead of loading full entities when only a subset of fields is needed.
- **No lazy loading** — Explicit includes only. Lazy loading causes N+1 queries.
- **Global query filters** — Apply tenant filtering at the DbContext level.

### 2.6 Error Handling

- **Problem Details** — All API errors return RFC 9457 Problem Details JSON.
- **Global exception handler** — Middleware catches unhandled exceptions, logs them, and returns a generic 500 response. Never leak stack traces or internal details.
- **Structured logging** — Use `ILogger<T>` with structured log messages (`_logger.LogInformation("Processing vulnerability {VulnerabilityId}", id)`). No string interpolation in log calls.
- **Log levels** — `Information` for business events, `Warning` for recoverable issues, `Error` for failures that need attention, `Debug`/`Trace` for development.

### 2.7 Testing

- **Unit tests** — Test business logic in Core. Mock external dependencies.
- **Integration tests** — Use `WebApplicationFactory` with a test PostgreSQL container (Testcontainers).
- **Test naming** — `MethodName_Scenario_ExpectedResult` (e.g., `AssignTask_NoOwner_FallsBackToDefaultTeam`).
- **No test logic in production code** — No `if (Environment.IsTest())` patterns.
- **Arrange-Act-Assert** — Every test follows this structure.

## 3. TypeScript / Frontend Standards

### 3.1 Project Structure

```
frontend/
  src/
    routes/              # TanStack Router route definitions
    components/
      ui/                # shadcn/ui components (generated)
      features/          # Feature-specific components
      layout/            # Shell, sidebar, nav
    hooks/               # Custom React hooks
    api/                 # TanStack Query hooks + API client
    lib/                 # Utilities, types, constants
    types/               # Shared TypeScript types
```

### 3.2 Naming Conventions

| Element | Convention | Example |
|---------|-----------|---------|
| Component file | PascalCase | `VulnerabilityList.tsx` |
| Hook file | camelCase with `use` prefix | `useVulnerabilities.ts` |
| Utility file | camelCase | `formatDate.ts` |
| Type / Interface | PascalCase | `Vulnerability`, `RemediationTask` |
| Function | camelCase | `formatSeverity` |
| Constant | SCREAMING_SNAKE_CASE | `MAX_PAGE_SIZE` |
| CSS class | Tailwind utilities only | No custom CSS classes |
| Route file | kebab-case | `vulnerability-detail.tsx` |

### 3.3 TypeScript Practices

- **Strict mode** — `"strict": true` in `tsconfig.json`. No `any` types unless explicitly justified with a comment.
- **Prefer `type` over `interface`** — Use `type` for object shapes. Use `interface` only when declaration merging is needed.
- **Discriminated unions** — Use discriminated unions for state that can be in multiple forms (loading/error/success).
- **Exhaustive checks** — Use `never` type in switch/if-else to ensure all cases are handled.
- **No type assertions** — Avoid `as` casts. Use type guards or parsing (e.g., Zod) at boundaries.
- **Zod for runtime validation** — Validate all API responses with Zod schemas. Never trust external data shapes.

### 3.4 React Practices

- **Functional components only** — No class components.
- **Custom hooks for logic** — Extract reusable logic into custom hooks. Components should focus on rendering.
- **TanStack Query for server state** — No `useState` + `useEffect` for data fetching. All server state goes through TanStack Query.
- **Colocation** — Keep components, hooks, and types close to where they're used. Only promote to shared locations when reused.
- **No prop drilling** — Use React Context or TanStack Query for shared state. Limit context to small, focused providers.
- **Memoization** — Only use `useMemo` and `useCallback` when there is a measured performance issue. Do not pre-optimize.
- **Error boundaries** — Wrap route-level components in error boundaries. Show user-friendly error states.

### 3.5 Security (Frontend)

- **No raw HTML injection** — If rendering HTML is required (e.g., AI reports in markdown), use a sanitization library (DOMPurify) and render via a dedicated markdown component. Never set inner HTML directly.
- **No secrets in frontend code** — API keys, tokens, etc. are never in frontend bundles.
- **CSRF** — Rely on Entra ID token-based auth (Bearer tokens). No cookies for auth = no CSRF risk.
- **Content Security Policy** — Configure CSP headers on the API that serves the frontend.
- **Dependency scanning** — `npm audit` in CI. No known-vulnerable packages.
- **Input sanitization** — Sanitize user input before sending to API. The API also validates, but defense in depth.

### 3.6 TanStack Query Patterns

- **Query keys** — Use factory pattern for consistent query key generation:
  ```typescript
  const vulnerabilityKeys = {
    all: ['vulnerabilities'] as const,
    lists: () => [...vulnerabilityKeys.all, 'list'] as const,
    list: (filters: VulnerabilityFilters) => [...vulnerabilityKeys.lists(), filters] as const,
    details: () => [...vulnerabilityKeys.all, 'detail'] as const,
    detail: (id: string) => [...vulnerabilityKeys.details(), id] as const,
  }
  ```
- **Mutations** — Use `useMutation` with `onSuccess` invalidation of related queries.
- **Optimistic updates** — Use for status changes and other quick operations to keep UI responsive.
- **Error handling** — Global error handler on the QueryClient for auth errors (redirect to login on 401).

### 3.7 Styling

- **Tailwind CSS only** — No custom CSS files. All styling via Tailwind utility classes.
- **shadcn/ui as base** — Use shadcn/ui components as the design system foundation. Customize via Tailwind config, not by overriding component internals.
- **Responsive** — All views must work on desktop. Tablet support is a nice-to-have. Mobile is not required initially.
- **Dark mode** — Support via Tailwind's `dark:` variant. Use CSS variables for theming.
- **Consistency** — Use design tokens (colors, spacing, typography) defined in `tailwind.config.ts`. No hardcoded color values.

### 3.8 Testing (Frontend)

- **Vitest** — Test runner and assertion library.
- **React Testing Library** — Component tests. Test behavior, not implementation.
- **MSW (Mock Service Worker)** — Mock API responses in tests. No mocking fetch directly.
- **Test naming** — Describe behavior: `"shows overdue tasks in red"`, `"navigates to detail on row click"`.

## 4. Shared Standards

### 4.1 Git

- **Branch naming** — `feature/<short-description>`, `fix/<short-description>`, `chore/<short-description>`.
- **Commit messages** — Imperative mood, 50-char subject line. Body for context when needed.
- **No large files** — No binaries, database dumps, or generated files in git.
- **`.gitignore`** — Ignore `node_modules/`, `bin/`, `obj/`, `.env`, IDE files.

### 4.2 API Contract

- **OpenAPI spec** — Generated from the .NET API via Swashbuckle/NSwag. Frontend types can be generated from the spec.
- **Versioning** — URL-based versioning (`/api/v1/...`) when breaking changes are introduced.
- **Consistent response shapes** — All list endpoints return `{ items: T[], totalCount: number }`. All errors return Problem Details.

### 4.3 Documentation

- **Code comments** — Only when the "why" is not obvious. No comments that restate the code.
- **XML docs on public APIs** — All public service interfaces and API controllers have XML doc summaries.
- **README** — Setup instructions, architecture overview, development workflow.

### 4.4 CI Pipeline (when established)

- Build + test on every PR.
- `dotnet format --verify-no-changes` for backend formatting.
- ESLint + Prettier for frontend formatting.
- Dependency vulnerability scanning (`dotnet list package --vulnerable`, `npm audit`).
- No merge with failing tests or formatting issues.
