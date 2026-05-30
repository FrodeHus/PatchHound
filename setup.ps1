#!/usr/bin/env pwsh
#Requires -Version 7

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$BAO_ADDR = 'http://127.0.0.1:8200'
$KV_MOUNT = 'patchhound'
$INIT_DIR = '.openbao-init'
$INIT_FILE = "$INIT_DIR/init.json"

function Invoke-Bao {
    param([string[]]$Args)
    docker compose exec -e "BAO_ADDR=$BAO_ADDR" openbao bao @Args
}

# ── 1. Build containers ──────────────────────────────────────────────────────
Write-Host "`n==> Building containers..." -ForegroundColor Cyan
docker compose build

# ── 2. Start OpenBao only ────────────────────────────────────────────────────
Write-Host "`n==> Starting OpenBao container..." -ForegroundColor Cyan
docker compose up -d openbao

Write-Host "    Waiting for OpenBao to be ready..."
$maxWait = 60
$elapsed = 0
$status = $null
do {
    Start-Sleep -Seconds 2
    $elapsed += 2
    # Ignore exit code — bao status exits 1 for both "not yet up" and "uninitialized".
    # Valid JSON in stdout is the only reliable signal the server is accepting requests.
    $statusJson = docker compose exec openbao bao status -address=$BAO_ADDR -format=json 2>$null
    $status = $statusJson | ConvertFrom-Json -ErrorAction SilentlyContinue
} while (-not $status -and $elapsed -lt $maxWait)

if (-not $status) {
    Write-Error "OpenBao did not become ready within ${maxWait}s."
}

# ── 3. Initialize ────────────────────────────────────────────────────────────
New-Item -ItemType Directory -Force -Path $INIT_DIR | Out-Null

if ($status.initialized) {
    Write-Host "    OpenBao already initialized." -ForegroundColor Yellow
    if (-not (Test-Path $INIT_FILE)) {
        Write-Error "OpenBao is initialized but $INIT_FILE is missing. Recreate the openbao_data volume to start fresh."
    }
    $init = Get-Content $INIT_FILE | ConvertFrom-Json
} else {
    Write-Host "`n==> Initializing OpenBao..." -ForegroundColor Cyan
    $initJson = Invoke-Bao 'operator', 'init', '-format=json'
    $initJson | Set-Content -Path $INIT_FILE
    $init = $initJson | ConvertFrom-Json
    Write-Host "    Init output saved to $INIT_FILE"
}

$rootToken = $init.root_token
$unsealKeys = $init.unseal_keys_b64

# ── 4. Unseal ────────────────────────────────────────────────────────────────
if ($status -and -not $status.sealed) {
    Write-Host "    OpenBao already unsealed." -ForegroundColor Yellow
} else {
    Write-Host "`n==> Unsealing OpenBao..." -ForegroundColor Cyan
    foreach ($key in $unsealKeys[0..2]) {
        Invoke-Bao 'operator', 'unseal', $key | Out-Null
    }
    Write-Host "    Unsealed."
}

# ── 5. Login with root token ─────────────────────────────────────────────────
Write-Host "`n==> Logging in with root token..." -ForegroundColor Cyan
Invoke-Bao 'login', '-no-print', $rootToken

# ── 6. Enable KV v2 ──────────────────────────────────────────────────────────
$secretsList = Invoke-Bao 'secrets', 'list', '-format=json' | ConvertFrom-Json -ErrorAction SilentlyContinue
if ($secretsList -and $secretsList."$KV_MOUNT/") {
    Write-Host "    KV mount '$KV_MOUNT' already enabled." -ForegroundColor Yellow
} else {
    Write-Host "`n==> Enabling KV v2 at $KV_MOUNT/..." -ForegroundColor Cyan
    Invoke-Bao 'secrets', 'enable', "-path=$KV_MOUNT", 'kv-v2'
}

# ── 7. Write policy ───────────────────────────────────────────────────────────
Write-Host "`n==> Writing PatchHound policy..." -ForegroundColor Cyan
$policyHcl = @"
path "$KV_MOUNT/*" {
  capabilities = ["create", "update", "read", "delete"]
}
"@
$policyFile = "$INIT_DIR/patchhound-policy.hcl"
$policyHcl | Set-Content -Path $policyFile

docker compose cp $policyFile openbao:/tmp/patchhound-policy.hcl
Invoke-Bao 'policy', 'write', 'patchhound', '/tmp/patchhound-policy.hcl'

# ── 8. Create application token ───────────────────────────────────────────────
Write-Host "`n==> Creating PatchHound application token..." -ForegroundColor Cyan
$tokenJson = Invoke-Bao 'token', 'create', '-policy=patchhound', '-format=json' | ConvertFrom-Json
$appToken = $tokenJson.auth.client_token
$appToken | Set-Content -Path "$INIT_DIR/app-token.txt"
Write-Host "    Application token saved to $INIT_DIR/app-token.txt"

# ── 9. Stop OpenBao ───────────────────────────────────────────────────────────
Write-Host "`n==> Stopping OpenBao container..." -ForegroundColor Cyan
docker compose stop openbao

# ── 10. Print tokens ──────────────────────────────────────────────────────────
Write-Host ""
Write-Host "============================================" -ForegroundColor Green
Write-Host "  OpenBao setup complete" -ForegroundColor Green
Write-Host "============================================" -ForegroundColor Green
Write-Host ""
Write-Host "Root token:        $rootToken" -ForegroundColor Yellow
Write-Host "PatchHound token:  $appToken" -ForegroundColor Yellow
Write-Host ""

# ── 11. Create / update .env ──────────────────────────────────────────────────
if (-not (Test-Path '.env')) {
    Write-Host "==> Copying .env.example -> .env..." -ForegroundColor Cyan
    Copy-Item '.env.example' '.env'
}

function Set-EnvValue {
    param([string]$File, [string]$Key, [string]$Value)
    $content = Get-Content $File
    $pattern = "^$Key=.*$"
    if ($content -match $pattern) {
        $content = $content -replace $pattern, "$Key=$Value"
    } else {
        $content += "$Key=$Value"
    }
    $content | Set-Content -Path $File -NoNewline:$false
}

Set-EnvValue -File '.env' -Key 'OPENBAO_TOKEN' -Value $appToken

# ── 12. Prompt for Azure AD values ────────────────────────────────────────────
Write-Host "==> Azure AD configuration" -ForegroundColor Cyan
Write-Host "    Press Enter to keep the current value (shown in brackets)."
Write-Host ""

function Read-EnvValue {
    param([string]$File, [string]$Key)
    $line = Get-Content $File | Where-Object { $_ -match "^$Key=" } | Select-Object -First 1
    if ($line) { return $line.Substring($Key.Length + 1) }
    return ''
}

$currentClientId  = Read-EnvValue -File '.env' -Key 'AZURE_AD_CLIENT_ID'
$currentTenantId  = Read-EnvValue -File '.env' -Key 'AZURE_AD_TENANT_ID'
$currentAudience  = Read-EnvValue -File '.env' -Key 'AZURE_AD_AUDIENCE'

$clientId = Read-Host "  AZURE_AD_CLIENT_ID  [$currentClientId]"
if ([string]::IsNullOrWhiteSpace($clientId)) { $clientId = $currentClientId }

$tenantId = Read-Host "  AZURE_AD_TENANT_ID  [$currentTenantId]"
if ([string]::IsNullOrWhiteSpace($tenantId)) { $tenantId = $currentTenantId }

$audience = Read-Host "  AZURE_AD_AUDIENCE    [$currentAudience]"
if ([string]::IsNullOrWhiteSpace($audience)) { $audience = $currentAudience }

Set-EnvValue -File '.env' -Key 'AZURE_AD_CLIENT_ID' -Value $clientId
Set-EnvValue -File '.env' -Key 'AZURE_AD_TENANT_ID' -Value $tenantId
Set-EnvValue -File '.env' -Key 'AZURE_AD_AUDIENCE'  -Value $audience

Write-Host ""
Write-Host "==> .env updated." -ForegroundColor Green
Write-Host ""
Write-Host "Next steps:" -ForegroundColor Cyan
Write-Host "  docker compose up -d --build"
Write-Host ""
