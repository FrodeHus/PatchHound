#!/usr/bin/env bash
set -euo pipefail

BAO_ADDR='http://127.0.0.1:8200'
KV_MOUNT='patchhound'
INIT_DIR='.openbao-init'
INIT_FILE="$INIT_DIR/init.json"

bao() { docker compose exec -e "BAO_ADDR=$BAO_ADDR" openbao bao "$@"; }

cyan()   { printf '\033[36m%s\033[0m\n' "$*"; }
yellow() { printf '\033[33m%s\033[0m\n' "$*"; }
green()  { printf '\033[32m%s\033[0m\n' "$*"; }

set_env_value() {
    local file="$1" key="$2" value="$3"
    if grep -qE "^${key}=" "$file"; then
        sed -i "s|^${key}=.*|${key}=${value}|" "$file"
    else
        echo "${key}=${value}" >> "$file"
    fi
}

read_env_value() {
    local file="$1" key="$2"
    grep -E "^${key}=" "$file" | head -1 | cut -d= -f2- || true
}

json_field() {
    python3 -c "import sys,json; print(json.load(sys.stdin)$1)"
}

# ── 1. Build containers ──────────────────────────────────────────────────────
echo; cyan "==> Building containers..."
docker compose build

# ── 2. Start OpenBao only ────────────────────────────────────────────────────
echo; cyan "==> Starting OpenBao container..."
docker compose up -d openbao

# Wait until the HTTP listener is accepting connections.
# Use the sys/health endpoint — it responds with a non-connection-error HTTP
# status regardless of init/seal state, so curl exits 0 as soon as the server
# is up. We do NOT use `bao status` here: it exits 1 when uninitialized, which
# is indistinguishable from "server not yet started".
echo "    Waiting for OpenBao HTTP listener..."
max_wait=60
elapsed=0
until curl -sf --max-time 2 "$BAO_ADDR/v1/sys/health" -o /dev/null 2>/dev/null \
      || curl -s  --max-time 2 "$BAO_ADDR/v1/sys/health" -o /dev/null 2>/dev/null; do
    sleep 2
    elapsed=$((elapsed + 2))
    if [ "$elapsed" -ge "$max_wait" ]; then
        echo "ERROR: OpenBao did not start within ${max_wait}s." >&2
        exit 1
    fi
done
echo "    OpenBao is up."

# ── 3. Initialize ────────────────────────────────────────────────────────────
mkdir -p "$INIT_DIR"
chmod 700 "$INIT_DIR"

# Check init state via the API (not bao status).
init_state=$(curl -s "$BAO_ADDR/v1/sys/init" | python3 -c "import sys,json; print('yes' if json.load(sys.stdin).get('initialized') else 'no')" 2>/dev/null || echo no)

if [ "$init_state" = "yes" ]; then
    yellow "    OpenBao already initialized."
    if [ ! -f "$INIT_FILE" ]; then
        echo "ERROR: OpenBao is initialized but $INIT_FILE is missing. Recreate the openbao_data volume to start fresh." >&2
        exit 1
    fi
else
    echo; cyan "==> Initializing OpenBao..."
    bao operator init -format=json > "$INIT_FILE"
    chmod 600 "$INIT_FILE"
    echo "    Init output saved to $INIT_FILE"
fi

root_token=$(json_field "['root_token']"          < "$INIT_FILE")
unseal_key0=$(json_field "['unseal_keys_b64'][0]" < "$INIT_FILE")
unseal_key1=$(json_field "['unseal_keys_b64'][1]" < "$INIT_FILE")
unseal_key2=$(json_field "['unseal_keys_b64'][2]" < "$INIT_FILE")

# ── 4. Unseal ────────────────────────────────────────────────────────────────
seal_state=$(curl -s "$BAO_ADDR/v1/sys/seal-status" | python3 -c "import sys,json; print('yes' if json.load(sys.stdin).get('sealed') else 'no')" 2>/dev/null || echo yes)

if [ "$seal_state" = "no" ]; then
    yellow "    OpenBao already unsealed."
else
    echo; cyan "==> Unsealing OpenBao (3 of 5 keys)..."
    bao operator unseal "$unseal_key0" >/dev/null
    bao operator unseal "$unseal_key1" >/dev/null
    bao operator unseal "$unseal_key2" >/dev/null
    echo "    Unsealed."
fi

# ── 5. Login with root token ─────────────────────────────────────────────────
echo; cyan "==> Logging in with root token..."
bao login -no-print "$root_token"

# ── 6. Enable KV v2 ──────────────────────────────────────────────────────────
if bao secrets list -format=json 2>/dev/null | python3 -c "import sys,json; d=json.load(sys.stdin); exit(0 if '${KV_MOUNT}/' in d else 1)" 2>/dev/null; then
    yellow "    KV mount '$KV_MOUNT' already enabled."
else
    echo; cyan "==> Enabling KV v2 at $KV_MOUNT/..."
    bao secrets enable -path="$KV_MOUNT" kv-v2
fi

# ── 7. Write policy ───────────────────────────────────────────────────────────
echo; cyan "==> Writing PatchHound policy..."
policy_file="$INIT_DIR/patchhound-policy.hcl"
cat > "$policy_file" <<EOF
path "${KV_MOUNT}/*" {
  capabilities = ["create", "update", "read", "delete"]
}
EOF

docker compose cp "$policy_file" openbao:/tmp/patchhound-policy.hcl
bao policy write patchhound /tmp/patchhound-policy.hcl

# ── 8. Create application token ───────────────────────────────────────────────
echo; cyan "==> Creating PatchHound application token..."
app_token=$(bao token create -policy=patchhound -format=json | python3 -c "import sys,json; print(json.load(sys.stdin)['auth']['client_token'])")
echo "$app_token" > "$INIT_DIR/app-token.txt"
chmod 600 "$INIT_DIR/app-token.txt"
echo "    Application token saved to $INIT_DIR/app-token.txt"

# ── 9. Stop OpenBao ───────────────────────────────────────────────────────────
echo; cyan "==> Stopping OpenBao container..."
docker compose stop openbao

# ── 10. Print summary ─────────────────────────────────────────────────────────
all_keys=$(python3 -c "
import json
d = json.load(open('$INIT_FILE'))
for i, k in enumerate(d['unseal_keys_b64'], 1):
    print(f'  Key {i}: {k}')
")

echo
green "============================================"
green "  OpenBao setup complete"
green "============================================"
echo
yellow "Unseal keys (keep these safe):"
yellow "$all_keys"
echo
yellow "Root token:        $root_token"
yellow "PatchHound token:  $app_token"
echo

# ── 11. Create / update .env ──────────────────────────────────────────────────
if [ ! -f .env ]; then
    cyan "==> Copying .env.example -> .env..."
    cp .env.example .env
fi

set_env_value .env OPENBAO_TOKEN "$app_token"

# ── 12. Prompt for Azure AD values ────────────────────────────────────────────
cyan "==> Azure AD configuration"
echo "    Press Enter to keep the current value (shown in brackets)."
echo

current_client_id=$(read_env_value .env AZURE_AD_CLIENT_ID)
current_tenant_id=$(read_env_value .env AZURE_AD_TENANT_ID)
current_audience=$(read_env_value  .env AZURE_AD_AUDIENCE)

read -r -p "  AZURE_AD_CLIENT_ID  [${current_client_id}]: " client_id
read -r -p "  AZURE_AD_TENANT_ID  [${current_tenant_id}]: " tenant_id
read -r -p "  AZURE_AD_AUDIENCE    [${current_audience}]: " audience

[ -z "$client_id" ] && client_id="$current_client_id"
[ -z "$tenant_id" ] && tenant_id="$current_tenant_id"
[ -z "$audience"  ] && audience="$current_audience"

set_env_value .env AZURE_AD_CLIENT_ID "$client_id"
set_env_value .env AZURE_AD_TENANT_ID "$tenant_id"
set_env_value .env AZURE_AD_AUDIENCE  "$audience"

echo
green "==> .env updated."
echo
cyan "Next steps:"
echo "  docker compose up -d --build"
echo
