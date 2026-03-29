# Adding an Ingestion Source

PatchHound keeps external ingestion logic in the infrastructure layer.

## Typical Steps

1. Add the source implementation under `src/PatchHound.Infrastructure/VulnerabilitySources`.
2. Define any source-specific options under `src/PatchHound.Infrastructure/Options`.
3. Register the source and its dependencies in the application startup path.
4. Add tests under `tests/PatchHound.Tests/Infrastructure`.
5. Update documentation if the new source requires extra configuration.

## Design Expectations

- Normalize incoming data before it reaches domain workflows.
- Keep external API clients isolated from business logic.
- Prefer explicit mapping and validation over dynamic payload handling.
- Make retry and failure behavior visible in logs.
