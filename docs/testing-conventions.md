# Testing Conventions

## Purpose

Keep tests high-signal, maintainable, and aligned with TDD by testing behavior at the right seam.

## Preferred test seams

- Domain service tests:
  - business rules
  - no HTTP
  - minimal persistence assumptions
- Infrastructure/provider tests:
  - external HTTP translation
  - EF-specific behavior
  - serialization/parsing
- Controller tests:
  - routing, authorization, filtering, DTO shape
  - one or two representative happy paths per endpoint
- Cross-service flow tests:
  - only critical end-to-end workflows
  - avoid duplicating lower-level assertions

## Rules

- Do not re-test provider HTTP behavior in controller tests.
- Do not re-test domain rules in controller tests if they are already covered in service tests.
- Do not keep generic pagination-only tests unless the pagination behavior itself is the feature under test.
- Prefer one theory with clear cases over many single-assertion facts for simple mapping or arithmetic logic.
- Keep one dominant responsibility per test file when possible.

## Test data conventions

- Reuse shared builders/factories from `tests/PatchHound.Tests/TestData` before creating local seed helpers.
- Prefer shared factories for:
  - tenant AI profiles
  - tenant software graphs
  - tenant vulnerability graphs
  - service provider / tenant-context wiring
- If a setup helper is only used once and is short, keep it local.
- If the same entity graph appears in multiple files, extract it.

## Deletion standard

Before removing a test, answer:

- what behavior does this prove?
- which remaining test proves that behavior at the correct seam?

If there is no clear replacement, do not delete it yet.

## Backend focus areas

- Keep `IngestionServiceTests` focused on critical merge/workflow paths.
- Keep Defender API tests focused on request/response and paging behavior.
- Keep source normalization tests focused on transformation logic, not full ingestion outcomes.
- Keep AI provider tests focused on provider-specific HTTP and error parsing.
- Keep AI service tests focused on provider selection, validation gating, and report generation flow.

## Frontend focus areas

- Prefer tests for:
  - route/search-state mapping
  - query-key and request shaping
  - important interactive components
  - major read-only detail sections with conditional rendering
- Do not over-invest in snapshot-style coverage for static markup.

## Practical guidance

- When adding a new feature, start with the narrowest meaningful seam.
- Add a broader integration test only if the feature crosses multiple layers in a way that lower-level tests would miss.
- When a file grows large from repeated setup, extract factories before adding more cases.
- When a file grows large from repeated assertions on simple input/output variants, convert to theories.
