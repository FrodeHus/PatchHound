# Tenant AI Integration Plan

## Goal

Introduce tenant-scoped AI configuration for report generation and future AI-assisted workflows.

The first supported providers are:

- Ollama
- Azure OpenAI
- OpenAI API

The configuration must:

- be managed in the UI
- be tenant-specific
- support a configurable system prompt
- support recommended runtime settings
- safely store secrets
- be auditable and testable

This plan assumes no backward-compatibility constraints for the AI configuration model. Existing global `AiProviderOptions` should be removed once the tenant-based path is active.

---

## Product Direction

### User intent

Who:

- tenant admins configuring AI behavior
- security operators generating vulnerability reports

What they need:

- choose an AI provider per tenant
- safely store credentials
- tune model behavior without code changes
- configure the system prompt
- validate connectivity before use
- generate reports from vulnerability detail using the tenant’s active AI profile

### UX principle

This should feel like operational infrastructure configuration, not a chatbot screen.

The interface should optimize for:

- clarity
- safe defaults
- validation before activation
- compactness
- explicit provider differences

### Visual direction

Use the existing settings/admin language:

- compact cards
- segmented provider selection
- minimal prose
- advanced settings collapsed

Avoid:

- one giant form containing all provider fields
- raw JSON as the primary editing surface
- exposing secrets after save
- mixing global and tenant AI settings on the same page

---

## Architecture

### Split

AI configuration should follow the same catalog/projection rule the rest of the app is moving toward:

- provider types are global concepts
- provider configuration is tenant-scoped
- generated reports are tenant-scoped artifacts

### Core entities

Add:

#### `TenantAiProfile`

Tenant-scoped AI configuration profile.

Suggested fields:

- `Id`
- `TenantId`
- `Name`
- `ProviderType`
- `IsDefault`
- `IsEnabled`
- `Model`
- `SystemPrompt`
- `Temperature`
- `TopP`
- `MaxOutputTokens`
- `TimeoutSeconds`
- `BaseUrl`
- `DeploymentName`
- `ApiVersion`
- `KeepAlive`
- `SecretRef`
- `LastValidatedAt`
- `LastValidationStatus`
- `LastValidationError`
- `CreatedAt`
- `UpdatedAt`

Notes:

- keep `ProviderType` constrained to known values
- use nullable provider-specific fields where appropriate
- keep `SecretRef` in DB, not the secret itself

#### `TenantAiProfileValidationStatus`

Use an enum:

- `Unknown`
- `Valid`
- `Invalid`

Optional, but recommended for clean UI and filtering.

### Existing entities to update

#### `AIReport`

Add a lightweight configuration snapshot so reports are auditable.

Recommended additions:

- `TenantAiProfileId`
- `ProviderType`
- `ProfileName`
- `Model`
- `SystemPromptHash`
- `Temperature`
- `MaxOutputTokens`

This preserves what generated the report even if tenant settings change later.

---

## Provider Model

### Supported providers

#### 1. Ollama

Tenant fields:

- `BaseUrl`
- `Model`
- `SystemPrompt`
- `Temperature`
- `TopP`
- `MaxOutputTokens`
- `TimeoutSeconds`
- `KeepAlive`

Secret handling:

- usually no secret required
- support optional secret later if a secured proxy is used

Recommended defaults:

- base URL: `http://localhost:11434`
- temperature: `0.2`
- max output tokens: `1200`
- timeout seconds: `60`

#### 2. Azure OpenAI

Tenant fields:

- `Endpoint`
- `DeploymentName`
- `ApiVersion`
- `Model` for display/reference
- `SystemPrompt`
- `Temperature`
- `TopP`
- `MaxOutputTokens`
- `TimeoutSeconds`
- `SecretRef` for API key

Recommended defaults:

- api version: explicit, not inferred
- temperature: `0.2`
- max output tokens: `1200`
- timeout seconds: `60`

#### 3. OpenAI API

Tenant fields:

- `BaseUrl` defaulting to the OpenAI API
- `Model`
- `SystemPrompt`
- `Temperature`
- `TopP`
- `MaxOutputTokens`
- `TimeoutSeconds`
- `SecretRef` for API key

Recommended defaults:

- base URL defaulted, not user-required
- temperature: `0.2`
- max output tokens: `1200`
- timeout seconds: `60`

### Provider abstraction

Current interface:

```csharp
public interface IAiReportProvider
{
    string ProviderName { get; }
    Task<string> GenerateReportAsync(
        VulnerabilityDefinition vulnerabilityDefinition,
        IReadOnlyList<Asset> affectedAssets,
        CancellationToken ct
    );
}
```

This should be replaced with a tenant-config-aware contract.

Recommended shape:

```csharp
public interface IAiReportProvider
{
    TenantAiProviderType ProviderType { get; }

    Task<string> GenerateReportAsync(
        AiReportGenerationRequest request,
        TenantAiProfileResolved profile,
        CancellationToken ct
    );

    Task<AiProviderValidationResult> ValidateAsync(
        TenantAiProfileResolved profile,
        CancellationToken ct
    );
}
```

#### `AiReportGenerationRequest`

Contains:

- vulnerability definition
- affected assets
- tenant metadata if needed
- optional report purpose in the future

#### `TenantAiProfileResolved`

Contains:

- tenant profile fields
- resolved secret values from the secret store

Do not pass raw unresolved DB entities into providers.

---

## Secret Handling

Reuse the existing secret model already used for ingestion sources.

### Secret storage

Store only `SecretRef` in DB.

Recommended secret paths:

- `tenants/{tenantId}/ai/{profileId}`

Recommended secret keys:

- OpenAI: `apiKey`
- Azure OpenAI: `apiKey`
- Ollama: none initially

### Rules

- never echo saved secrets back to the client
- never return secrets in tenant detail payloads
- allow blank secret on update to mean “keep existing secret”
- clear secret only through an explicit action later if needed

---

## Backend Design

### New services

#### `TenantAiProfileService`

Responsibilities:

- CRUD for AI profiles
- enforce one default profile per tenant
- validate provider-specific required fields

#### `TenantAiConfigurationResolver`

Responsibilities:

- load the tenant’s default AI profile
- resolve secrets via `ISecretStore`
- return a provider-ready resolved config

#### `AiProviderValidationService`

Responsibilities:

- run lightweight provider validation
- persist last validation status and error

#### `AiReportService`

Refactor current service so it:

- resolves tenant profile instead of taking a free-form provider name
- selects provider by `ProviderType`
- passes resolved config into provider
- stores configuration snapshot in `AIReport`

### Existing code to change

#### Replace global options usage

Current:

- `AiProviderOptions`
- provider classes depend on `IOptions<AiProviderOptions>`

Target:

- remove global provider options as the runtime source of truth
- provider classes use resolved tenant profile input instead

#### Replace hard-coded provider name request

Current:

- `GenerateAiReportRequest(string ProviderName)`
- frontend sends arbitrary provider string

Target:

- default path: no provider name required
- report generation uses tenant default AI profile
- optional future override:
  - `TenantAiProfileId`

### Validation behavior

Provide a tenant profile validation endpoint that:

- checks required fields
- checks secret presence
- performs a lightweight call when possible
- records validation result
- returns sanitized error text only

Validation should be required before setting a profile as default.

---

## API Design

### Tenant AI settings endpoints

Add a dedicated settings surface:

- `GET /api/settings/ai`
- `POST /api/settings/ai/profiles`
- `PUT /api/settings/ai/profiles/{id}`
- `POST /api/settings/ai/profiles/{id}/validate`
- `POST /api/settings/ai/profiles/{id}/set-default`
- `DELETE /api/settings/ai/profiles/{id}` or disable in place

Recommended DTO shape:

#### list/detail DTO

- `id`
- `name`
- `providerType`
- `isDefault`
- `isEnabled`
- `model`
- `systemPrompt`
- `temperature`
- `topP`
- `maxOutputTokens`
- `timeoutSeconds`
- `baseUrl`
- `deploymentName`
- `apiVersion`
- `keepAlive`
- `hasSecret`
- `lastValidatedAt`
- `lastValidationStatus`
- `lastValidationError`

#### save request DTO

- all non-secret fields
- secret field included only on create/update requests

### AI report generation endpoint

Current:

- `POST /api/vulnerabilities/{id}/ai-report`
- body includes `providerName`

Target:

- `POST /api/vulnerabilities/{id}/ai-report`
- body:
  - empty for default profile
  - optional `tenantAiProfileId` later

Recommended initial request:

```csharp
public record GenerateAiReportRequest(Guid? TenantAiProfileId);
```

Behavior:

- if no profile id is supplied, use the tenant default
- if supplied later, validate it belongs to the same tenant

---

## Frontend Design

### Primary configuration location

Tenant-scoped settings page.

Recommended route:

- `/_authed/settings/ai`

If you want to keep settings compact initially:

- add an `AI` section/tab under existing settings route first

### UI structure

#### Header

- title: `AI Configuration`
- description: “Configure how this tenant generates AI reports and recommendations.”

#### Left rail: profiles

Compact profile list:

- profile name
- provider pill
- default badge
- enabled/disabled state
- validation status

Actions:

- add profile
- edit
- set default
- validate

#### Main panel: profile editor

Layout:

1. provider selection pills
2. basic profile fields
3. provider-specific fields
4. prompt editor
5. advanced runtime settings
6. validation + save footer

### Prompt editor

System prompt should be first-class.

Recommended controls:

- large textarea
- `Use recommended prompt`
- optional `Reset`
- character count

Do not hide the system prompt behind advanced settings.

### Advanced settings

Collapsed by default.

Recommended advanced fields:

- temperature
- top-p
- max output tokens
- timeout seconds
- keep-alive for Ollama

### AI report generation UI

Current `AiReportTab` is too raw:

- free-form provider string input
- no tenant config awareness

Replace it with:

- current active profile summary
- `Generate AI report` button
- optional future profile override select
- generated report metadata:
  - provider
  - model
  - generated at

Recommended tab copy:

- “Uses this tenant’s default AI profile.”

---

## Recommended Default Prompt

Seed each new profile with a recommended default system prompt.

Recommended structure:

1. role
2. grounding rules
3. prioritization rules
4. output structure
5. tone constraints

Suggested content direction:

- act as a PatchHound vulnerability analysis assistant
- rely only on provided vulnerability and asset context
- do not invent missing facts
- prioritize exploitability, blast radius, and criticality
- provide concise markdown with stable sections

Recommended sections:

- Executive Summary
- Technical Analysis
- Affected Tenant Context
- Prioritization
- Recommended Actions

Store the full prompt as tenant-editable text.

---

## Suggested Improvements

### 1. Multiple profiles per tenant

Recommendation:

- support multiple named profiles
- require exactly one default

Examples:

- `Default analysis`
- `Executive summary`
- `Deep technical`

This is better than a single tenant-wide config because it avoids repainting the schema later.

### 2. Validation before default

Do not let invalid or untested profiles become the default.

### 3. Configuration snapshot in reports

Each `AIReport` should preserve:

- provider type
- profile name
- model
- prompt hash
- runtime settings used

This makes reports auditable and reproducible.

### 4. Token and timeout caps

Apply server-side caps even if the UI allows editing:

- prevent runaway cost or latency
- keep long-running requests bounded

### 5. Usage telemetry

Not required in phase 1, but recommended next:

- request count
- failure count
- validation failures
- last successful use

### 6. Structured outputs later

Start with markdown output, but keep the provider request model flexible enough for structured outputs later.

---

## Implementation Phases

### Phase 1: Data model and backend contract

Add:

- `TenantAiProfile`
- validation status enum
- `AIReport` config snapshot fields

Refactor:

- `AiReportService`
- `IAiReportProvider`
- remove global runtime reliance on `AiProviderOptions`

### Phase 2: Provider implementations

Implement:

- `OllamaAiProvider`
- `AzureOpenAiProvider`
- `OpenAiProvider`

Each provider must support:

- generate report
- validate configuration

### Phase 3: Tenant settings API

Add:

- profile CRUD
- validation endpoint
- default selection endpoint

Persist secrets through `ISecretStore`.

### Phase 4: Frontend settings UI

Add:

- AI settings page or section
- profile list
- provider-specific editor
- prompt editor
- advanced settings
- validation action

### Phase 5: Vulnerability AI report UI

Replace the raw provider-name input in:

- `frontend/src/components/features/vulnerabilities/AiReportTab.tsx`

Use:

- default tenant AI profile
- report generation button
- generated report metadata

### Phase 6: Cleanup

Remove:

- raw provider string entry in frontend
- old global `AiProviderOptions` runtime path
- unused provider stubs
- any dead settings form code like the current placeholder AI form if it becomes redundant

---

## Repo Mapping

### Backend files likely to change

- `src/PatchHound.Core/Interfaces/IAiReportProvider.cs`
- `src/PatchHound.Core/Services/AiReportService.cs`
- `src/PatchHound.Core/Entities/AIReport.cs`
- `src/PatchHound.Api/Controllers/VulnerabilitiesController.cs`
- `src/PatchHound.Infrastructure/DependencyInjection.cs`
- `src/PatchHound.Infrastructure/AiProviders/AzureOpenAiProvider.cs`
- `src/PatchHound.Infrastructure/AiProviders/OpenAiProvider.cs`
- `src/PatchHound.Infrastructure/AiProviders/OllamaAiProvider.cs`
- new settings controller/service files for tenant AI profiles

### Frontend files likely to change

- `frontend/src/components/features/vulnerabilities/AiReportTab.tsx`
- `frontend/src/api/vulnerabilities.functions.ts`
- `frontend/src/api/vulnerabilities.schemas.ts`
- `frontend/src/routes/_authed/settings/index.tsx`
- new AI settings API/schema files
- new AI settings components

### Tests to add

Backend:

- provider selection by tenant default profile
- provider validation
- secret resolution
- settings CRUD
- report generation snapshot persistence

Frontend:

- AI settings form behavior
- provider-specific field switching
- validation state rendering
- AI report tab using tenant default profile

---

## Recommended Decisions

These are the recommended defaults unless product requirements change:

- multiple named profiles per tenant, one default
- no per-report profile override in phase 1
- system prompt editable per profile
- validation required before default activation
- secrets always stored in secret store, never in DB
- markdown output first, structured outputs later

---

## Next Step

Start with PR 1:

- add `TenantAiProfile`
- refactor `IAiReportProvider` and `AiReportService`
- add tenant config resolution and provider validation interfaces

Then build the tenant settings UI on top of that backend contract.
