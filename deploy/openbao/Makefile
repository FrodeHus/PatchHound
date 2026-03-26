COMPOSE    := docker compose
BAO        := $(COMPOSE) exec -e BAO_ADDR=http://127.0.0.1:8200 openbao bao
KV_MOUNT   := patchhound
OUTPUT_DIR := .openbao-init
INIT_FILE  := $(OUTPUT_DIR)/init.json
POLICY_FILE := $(OUTPUT_DIR)/patchhound-policy.hcl

.PHONY: all init unseal login enable-kv policy token clean status

all: init unseal login enable-kv policy token
	@echo ""
	@echo "============================================"
	@echo "  OpenBao setup complete"
	@echo "============================================"
	@echo ""
	@echo "Root token:"
	@jq -r '.root_token' $(INIT_FILE)
	@echo ""
	@echo "Unseal keys:"
	@jq -r '.unseal_keys_b64[]' $(INIT_FILE) | nl -ba
	@echo ""
	@echo "Application token (set as OPENBAO_TOKEN in .env):"
	@cat $(OUTPUT_DIR)/app-token.txt
	@echo ""

$(OUTPUT_DIR):
	@mkdir -p $(OUTPUT_DIR)

## Initialize OpenBao (generates root token + unseal keys)
init: $(OUTPUT_DIR)
	@if $(BAO) status -format=json 2>/dev/null | jq -e '.initialized == true' >/dev/null 2>&1; then \
		echo "OpenBao is already initialized — skipping."; \
		if [ ! -f $(INIT_FILE) ]; then \
			echo "ERROR: $(INIT_FILE) not found. If you lost the init output you must recreate the volume."; \
			exit 1; \
		fi; \
	else \
		echo "Initializing OpenBao..."; \
		$(BAO) operator init -format=json > $(INIT_FILE); \
		chmod 600 $(INIT_FILE); \
		echo "Init output saved to $(INIT_FILE)"; \
	fi

## Unseal using the first 3 keys from init output
unseal: init
	@if $(BAO) status -format=json 2>/dev/null | jq -e '.sealed == false' >/dev/null 2>&1; then \
		echo "OpenBao is already unsealed — skipping."; \
	else \
		echo "Unsealing OpenBao..."; \
		for i in 0 1 2; do \
			key=$$(jq -r ".unseal_keys_b64[$$i]" $(INIT_FILE)); \
			$(BAO) operator unseal "$$key" >/dev/null; \
		done; \
		echo "OpenBao unsealed."; \
	fi

## Login with root token
login: unseal
	@echo "Logging in with root token..."
	@$(BAO) login -no-print $$(jq -r '.root_token' $(INIT_FILE))

## Enable KV v2 secrets engine
enable-kv: login
	@if $(BAO) secrets list -format=json 2>/dev/null | jq -e '."$(KV_MOUNT)/"' >/dev/null 2>&1; then \
		echo "KV mount '$(KV_MOUNT)' already enabled — skipping."; \
	else \
		echo "Enabling KV v2 at $(KV_MOUNT)/..."; \
		$(BAO) secrets enable -path=$(KV_MOUNT) kv-v2; \
	fi

## Write the PatchHound policy
policy: enable-kv $(OUTPUT_DIR)
	@echo "Writing patchhound policy..."
	@printf '%s\n' \
		'path "$(KV_MOUNT)/data/tenants/*" {' \
		'  capabilities = ["create", "update", "read"]' \
		'}' \
		'' \
		'path "$(KV_MOUNT)/data/system/enrichment-sources/*" {' \
		'  capabilities = ["create", "update", "read"]' \
		'}' \
		'' \
		'path "$(KV_MOUNT)/data/system/notification-services/*" {' \
		'  capabilities = ["create", "update", "read"]' \
		'}' > $(POLICY_FILE)
	@$(COMPOSE) cp $(POLICY_FILE) openbao:/tmp/patchhound-policy.hcl
	@$(BAO) policy write patchhound /tmp/patchhound-policy.hcl

## Create an application token with the patchhound policy
token: policy $(OUTPUT_DIR)
	@echo "Creating application token..."
	@$(BAO) token create -policy=patchhound -format=json | jq -r '.auth.client_token' > $(OUTPUT_DIR)/app-token.txt
	@chmod 600 $(OUTPUT_DIR)/app-token.txt
	@echo "Application token saved to $(OUTPUT_DIR)/app-token.txt"

## Print current seal status
status:
	@$(BAO) status

## Remove local init artifacts (does NOT touch the OpenBao data volume)
clean:
	rm -rf $(OUTPUT_DIR)
