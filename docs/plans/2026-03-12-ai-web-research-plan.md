# AI Web Research Plan

## Goal

Allow tenant-configured AI features to enrich reports and dashboard summaries with recent external context from the web.

Initial scope:

- support `Risk Change Brief` AI augmentation
- support tenant AI profiles for:
  - OpenAI
  - Azure OpenAI
  - Ollama
- make web research configurable per tenant
- keep deterministic product data as the primary source of truth

This plan extends the existing tenant AI profile model rather than introducing a separate global AI setting.

## Product Direction

### User intent

Who:

- tenant admins configuring AI behavior
- security analysts reading AI-augmented summaries

What they need:

- decide whether AI may use external web research
- understand which providers can do native web search and which need PatchHound-managed augmentation
- get one short, useful external-context summary without turning the dashboard into an AI surface

### UX principle

External research should be:

- optional
- explicitly enabled
- bounded
- attributable
- non-blocking

Deterministic product data must always render first.

## Provider Capability Matrix

### OpenAI

Use provider-native web search when enabled.

Official docs:

- OpenAI Responses API web search tool:
  https://platform.openai.com/docs/guides/tools-web-search

Recommended mode:

- `ProviderNative`

### Ollama

Ollama documents web search capability, but it is not a safe assumption for local tenant-hosted Ollama instances that PatchHound already supports for offline/private use.

Official docs:

- Ollama web search:
  https://docs.ollama.com/capabilities/web-search

Recommended mode:

- `PatchHoundManaged`

Reason:

- local/private Ollama should still be able to benefit from web-enriched prompting
- native support is not guaranteed across local/runtime variants

### Azure OpenAI

Treat Azure OpenAI as requiring PatchHound-managed research for this feature.

I did not find a clean direct equivalent to OpenAI’s native general-purpose `web_search` tool for the current PatchHound use case. Azure’s guidance is more compatible with external retrieval/orchestration than a built-in provider-native web tool path.

Recommended mode:

- `PatchHoundManaged`

## Architecture

## New tenant-level settings

Extend `TenantAiProfile` with web research policy fields.

Suggested additions:

- `WebResearchMode`
  - `Disabled`
  - `ProviderNative`
  - `PatchHoundManaged`
- `AllowExternalResearch`
  - boolean
- `IncludeCitations`
  - boolean
- `MaxResearchSources`
  - int
- `AllowedDomains`
  - serialized string / JSON list

Recommended defaults:

- `AllowExternalResearch = false`
- `WebResearchMode = Disabled`
- `IncludeCitations = true`
- `MaxResearchSources = 5`

## New enums

Add:

- `TenantAiWebResearchMode`

Suggested values:

- `Disabled`
- `ProviderNative`
- `PatchHoundManaged`

## New backend services

### `ITenantAiResearchService`

Responsible for producing a compact research bundle.

Suggested contract:

```csharp
public interface ITenantAiResearchService
{
    Task<Result<AiResearchBundle>> ResearchAsync(
        Guid tenantId,
        TenantAiProfileResolved profile,
        AiResearchRequest request,
        CancellationToken ct
    );
}
```

### `AiResearchRequest`

Suggested fields:

- `Prompt`
- `AllowedDomains`
- `MaxSources`
- `TimeWindowHint`
- `ContextType`
  - `RiskChangeBrief`
  - future:
    - `VulnerabilityReport`
    - `SoftwareReport`

### `AiResearchBundle`

Compact, provider-neutral research output.

Suggested fields:

- `Query`
- `GeneratedAt`
- `Sources`
  - `Title`
  - `Url`
  - `Snippet`
  - `PublishedAt`
- `RenderedSourceContext`

The important output is a concise research bundle that can be embedded into the AI prompt without requiring downstream provider-specific logic.

## Research execution model

### Mode 1: `ProviderNative`

Used for OpenAI.

Flow:

1. resolve tenant AI profile
2. if research is enabled and mode is `ProviderNative`
3. let the provider use native web search
4. require concise output and citations where supported

### Mode 2: `PatchHoundManaged`

Used for Azure OpenAI and Ollama by default.

Flow:

1. PatchHound performs a web research step server-side
2. PatchHound builds `AiResearchBundle`
3. AI provider receives:
   - deterministic product payload
   - bounded research source bundle
   - instructions to use only supplied external sources

This keeps the experience consistent even when the provider cannot search the web directly.

## Search implementation strategy

For the initial implementation, PatchHound-managed research should be provider-agnostic and bounded.

Recommended first version:

- introduce an internal web search adapter layer
- allow one provider implementation behind that layer
- do not expose “search engine” choice in the tenant UI yet

Suggested internal abstraction:

```csharp
public interface IWebResearchProvider
{
    Task<Result<IReadOnlyList<WebResearchSource>>> SearchAsync(
        WebResearchQuery query,
        CancellationToken ct
    );
}
```

This keeps the tenant-facing AI model separate from the underlying search mechanism.

## AI prompt design

For dashboard use, AI should receive:

- deterministic `RiskChangeBrief` JSON
- optional `AiResearchBundle`
- strict instruction to summarize briefly
- instruction not to invent missing details
- instruction to cite or mention external context only if it appears in supplied sources

For `Risk Change Brief`, target output should be one sentence only.

Example:

- `3 critical issues appeared, with external reporting concentrated around VMware Spring exposure and active exploitation discussion.`

Not acceptable:

- multi-paragraph summary
- speculative threat claims
- unstated or uncited claims

## UX

### AI settings

Add a compact “Web research” section to the AI profile editor.

Fields:

- `Allow external web research`
- `Research mode`
- `Max sources`
- `Allowed domains`
- `Include citations`

Behavior:

- if provider is OpenAI:
  - allow `Provider native`
  - allow `PatchHound managed`
- if provider is Ollama:
  - default to `PatchHound managed`
- if provider is Azure OpenAI:
  - default to `PatchHound managed`

Do not show this section unless the AI profile is enabled.

### Dashboard

`Risk Change Brief` keeps deterministic content primary.

AI behavior:

- if AI profile is valid
- and external research is enabled
- and summary generation succeeds

Then show one short summary line.

If it fails:

- no card-level error state
- deterministic content still renders normally

## Data model updates

Update:

- [src/PatchHound.Core/Entities/TenantAiProfile.cs](/Users/frode.hus/src/github.com/frodehus/PatchHound/src/PatchHound.Core/Entities/TenantAiProfile.cs)
- corresponding EF config and migration
- frontend AI settings schema and form

## Service updates

Update:

- [src/PatchHound.Core/Interfaces/IAiReportProvider.cs](/Users/frode.hus/src/github.com/frodehus/PatchHound/src/PatchHound.Core/Interfaces/IAiReportProvider.cs)
- [src/PatchHound.Core/Services/TenantAiTextGenerationService.cs](/Users/frode.hus/src/github.com/frodehus/PatchHound/src/PatchHound.Core/Services/TenantAiTextGenerationService.cs)

Recommended additions:

- optional research bundle input for text generation
- provider-native capability hook where supported

## Risk Change Brief integration

Phase this feature into:

### Phase 1

- deterministic card only
- already implemented

### Phase 2

- optional AI text generation for `RiskChangeBrief`
- no web research yet

### Phase 3

- AI text generation with web research
- citations/source list available in drill-down view if desired

## Safety rules

- default to disabled
- never block deterministic dashboard rendering on AI or web search
- store only compact source metadata, not full articles
- respect domain allow-list when configured
- keep prompts explicit about source grounding
- do not let AI browse arbitrary internal URLs

## Suggested implementation order

1. Add `TenantAiProfile` web research fields and migration
2. Add frontend AI settings support for research mode and limits
3. Introduce `ITenantAiResearchService` and `IWebResearchProvider`
4. Add PatchHound-managed research bundle generation
5. Add OpenAI native web-search path
6. Add `Risk Change Brief` one-line AI summary generation
7. Optionally surface citations in the detail view

## Keep / Avoid

Keep:

- deterministic dashboard data as the main experience
- one-line AI summary only
- provider-agnostic tenant configuration language

Avoid:

- AI-only dashboard cards
- long markdown summaries on the dashboard
- treating all providers as having equal native research capability
- making web search mandatory for AI generation

## Sources

- OpenAI web search tool:
  https://platform.openai.com/docs/guides/tools-web-search
- Ollama web search:
  https://docs.ollama.com/capabilities/web-search
