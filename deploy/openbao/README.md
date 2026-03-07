# OpenBao

This stack now includes an `openbao` service in `docker-compose.yml`.

## Start

```bash
docker compose up -d openbao
```

OpenBao UI/API will be available at `http://localhost:8200`.

## Initialize

Run once for a new data volume:

```bash
docker compose exec openbao bao operator init
```

Store the generated unseal keys and initial root token safely.

## Unseal

Use at least three unseal keys from the init output:

```bash
docker compose exec openbao bao operator unseal
```

Repeat until the server is unsealed.

## Login

```bash
docker compose exec openbao bao login
```

Paste the initial root token from the init output.

## Enable KV

Create the KV v2 mount used for PatchHound secrets:

```bash
docker compose exec openbao bao secrets enable -path=patchhound kv-v2
```

## Application Access Token

Create a policy for PatchHound and issue a token for the API/worker:

```bash
docker compose exec openbao sh -c 'cat >/tmp/patchhound-policy.hcl <<EOF
path "patchhound/data/tenants/*" {
  capabilities = ["create", "update", "read"]
}
EOF
bao policy write patchhound /tmp/patchhound-policy.hcl
bao token create -policy=patchhound'
```

Set the resulting token in `.env` as `OPENBAO_TOKEN`.

## Example Secret

```bash
docker compose exec openbao bao kv put patchhound/tenants/<tenant-id>/sources/microsoft-defender \
  clientSecret="replace-me" \
  clientId="replace-me" \
  tenantId="replace-me"
```

## Notes

- The Compose setup uses file storage at `/openbao/file` via the `openbao_data` volume.
- TLS is disabled in this local self-hosting profile. Put OpenBao behind TLS before exposing it outside a trusted network.
- PatchHound now stores tenant source secrets in OpenBao and keeps only secret references in tenant settings.
- If `OPENBAO_TOKEN` is missing, tenant secret writes will fail and worker secret reads will fail for tenant-backed ingestion sources.
