# Create an Ingestion Source

This guide is the dedicated walkthrough for adding a new ingestion source to PatchHound.

## Where the Code Lives

- Source integrations: `src/PatchHound.Infrastructure/VulnerabilitySources`
- Source-specific options: `src/PatchHound.Infrastructure/Options`
- Domain models and contracts: `src/PatchHound.Core`
- Tests: `tests/PatchHound.Tests/Infrastructure`

## Implementation Checklist

1. Add the source implementation under `src/PatchHound.Infrastructure/VulnerabilitySources`.
2. Model any required configuration in `src/PatchHound.Infrastructure/Options`.
3. Register the source and supporting services in the application startup path.
4. Normalize external payloads before they enter domain workflows.
5. Add tests for mapping, validation, and failure behavior.
6. Document any new environment variables or operational prerequisites.

## Design Rules

- Keep API clients and payload mapping isolated from business logic.
- Prefer explicit contracts over dynamic parsing.
- Log failures with enough context to debug ingestion issues.
- Make retries and partial-failure behavior predictable.

## Validation

Before opening a pull request, verify:

```bash
dotnet build PatchHound.slnx
dotnet test PatchHound.slnx -v minimal
```

If the source also affects frontend configuration or admin flows, run the frontend checks as well.
