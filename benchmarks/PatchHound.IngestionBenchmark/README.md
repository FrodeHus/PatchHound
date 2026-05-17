# PatchHound ingestion benchmark

Standalone perf harness that spins up an ephemeral Postgres in Docker, seeds staged
ingestion data at configurable scale, runs the bulk-path ingestion pipeline, and
prints per-stage timings. Not invoked by `dotnet test`.

## Run

    dotnet run --project benchmarks/PatchHound.IngestionBenchmark -- \
      --tenants=1 --devices=1000 --vulns-per-device=20 --software-per-device=5 --runs=3

## Flags

| Flag                       | Default | Meaning |
|----------------------------|---------|---------|
| `--tenants=N`              | 1       | Tenant count |
| `--devices=N`              | 100     | Devices per tenant |
| `--vulns-per-device=N`     | 10      | Staged vulnerabilities per device |
| `--software-per-device=N`  | 5       | Installed software rows per device |
| `--runs=N`                 | 1       | Repeat ingestion against the same tenants. Re-runs exercise UPSERT / reobserve / resolve paths. |

## Requirements

- Docker (Testcontainers boots Postgres in a container).
- Postgres image `postgres:16-alpine` (~80 MB) pulled on first run.

## Stages measured

Device merge, Process staged, Exposure derivation, Episode sync, Software projection,
plus Total. Seed time is captured separately and excluded from totals.
