# Defender Machine Fields: exposureLevel, isAadJoined, machineTags

## Problem

The Defender API returns `machineTags`, `exposureLevel`, and `isAadJoined` for machines, but these fields are not captured in PatchHound. Additionally, `healthStatus` and `riskScore` are stored but not surfaced in the asset list view or available as filters.

## Data Model

### New columns on Asset entity

| Column | Type | Notes |
|---|---|---|
| `DeviceExposureLevel` | varchar(64), nullable | "Low", "Medium", "High", "None" |
| `DeviceIsAadJoined` | bool?, nullable | Entra ID join status |

### New entity: AssetTag

| Column | Type | Notes |
|---|---|---|
| `Id` | Guid, PK | |
| `AssetId` | Guid, FK → Asset | |
| `TenantId` | Guid | For global query filter |
| `Tag` | varchar(256) | Tag value |
| `Source` | varchar(64) | "Defender" (extensible for manual tags) |
| `CreatedAt` | DateTimeOffset | |

Indexes:
- Unique composite: `(AssetId, Tag)`
- Covering: `(TenantId, Tag)` for filter queries

### Ingestion pipeline

- Add `exposureLevel`, `isAadJoined`, `machineTags` to `DefenderMachineEntry`.
- Add `DeviceExposureLevel`, `DeviceIsAadJoined` to `IngestionAsset`.
- Add `MachineTags` (List<string>) to `IngestionAsset`.
- `Asset.UpdateDeviceDetails()` gains `exposureLevel` and `isAadJoined` parameters.
- `StagedAssetMergeService` syncs tags per asset per source: insert new tags, remove tags no longer present.

## API Changes

### AssetDto (list) — new fields

- `healthStatus` (string?) — already stored, newly surfaced
- `riskScore` (string?) — already stored, newly surfaced
- `exposureLevel` (string?) — new
- `tags` (string[]) — from AssetTag join

### AssetDetailDto (detail) — new fields

- `exposureLevel` (string?) — new
- `isAadJoined` (bool?) — new
- `tags` (string[]) — from AssetTag join

### AssetFilterQuery — new filter parameters

- `HealthStatus` (string?) — exact match
- `RiskScore` (string?) — exact match
- `ExposureLevel` (string?) — exact match
- `Tag` (string?) — substring match

## Frontend Changes

### Asset list table

- New columns: Health Status, Risk Score, Exposure Level, Tags (as badges).
- New filter controls for Health Status, Risk Score, Exposure Level, Tag.

### Asset detail (DeviceSection)

- Add Exposure Level and AAD Joined (Yes/No/Unknown) to device info grid.
- Add Tags row with badge rendering.

### Schema & server functions

- Update `assets.schemas.ts` with new fields on both list and detail schemas.
- Update `assets.functions.ts` to pass new filter parameters.
