# Setting Up an AI Profile

PatchHound uses tenant AI profiles for AI-assisted vulnerability analysis, risk-change summaries, and enrichment jobs that need a language model. Each tenant should have one enabled, validated default profile before AI report generation is used in production.

Profiles are configured in the PatchHound web UI under:

```text
Admin -> Platform -> AI
```

## Choose a Provider

PatchHound supports these profile providers:

- **Ollama**: local or self-hosted model runtime.
- **OpenAI**: direct OpenAI API or an OpenAI-compatible gateway.
- **Azure OpenAI**: Azure-hosted model deployments.

For local or self-hosted operation, use **Ollama**. This keeps prompts and responses inside your own environment, assuming the Ollama host is also under your control.

## Recommended Baseline Settings

Use conservative runtime settings for vulnerability analysis. The goal is stable, factual output rather than creative prose.

| Setting | Recommended value | Notes |
| --- | --- | --- |
| Temperature | `0.2` | Keeps output deterministic. |
| Top P | `1` | Leave broad unless you need tighter sampling. |
| Max output tokens | `1200` | Enough for concise vulnerability analysis. Increase for longer reports. |
| Timeout seconds | `60` | Local large models may need more, such as `120` or `180`. |
| Response format | `Free text` | Use `JSON` only for workflows that parse strict JSON. |
| Context window (`num_ctx`) | `8192` | Useful for PatchHound prompts with vulnerability and tenant context. Increase only if the host has enough memory. |
| Keep alive | `5m` | Keeps the model warm after requests. |

Keep the default PatchHound system prompt unless you have a specific operating requirement. It includes guardrails for treating vulnerability data as data, not instructions.

## Run a Local Model with Ollama

Install Ollama on the host that will run the model:

```bash
curl -fsSL https://ollama.com/install.sh | sh
```

Start Ollama:

```bash
ollama serve
```

In another terminal, pull and test the recommended Q5_K_M GGUF model:

```bash
ollama pull hf.co/unsloth/Qwen3.6-35B-A3B-MTP-GGUF:UD-Q5_K_M
ollama run hf.co/unsloth/Qwen3.6-35B-A3B-MTP-GGUF:UD-Q5_K_M
```

`UD-Q5_K_M` is a sensible local-analysis default because it preserves more quality than smaller 4-bit quantizations while still being much more practical than full precision. Use a lower quantization only when memory pressure matters more than answer quality.

Large Qwen models need substantial RAM or VRAM. If the model is too slow or fails to load, use a smaller model with the same profile settings before changing PatchHound.

## Expose Ollama to PatchHound

If PatchHound and Ollama run on the same host, the default Ollama URL is:

```text
http://localhost:11434
```

If PatchHound runs in Docker and Ollama runs on the host machine, use the host-reachable address from the container. On Docker Desktop this is commonly:

```text
http://host.docker.internal:11434
```

On Linux Docker hosts, configure a reachable host name or use the host's LAN address, for example:

```text
http://192.168.1.20:11434
```

Bind Ollama only to trusted interfaces. If you expose Ollama beyond localhost, place it behind network controls or a secured reverse proxy.

## Create the PatchHound Ollama Profile

In `Admin -> Platform -> AI`, create a new profile with:

| Field | Value |
| --- | --- |
| Provider | `Ollama` |
| Profile name | `Local Qwen analysis` |
| Model | `hf.co/unsloth/Qwen3.6-35B-A3B-MTP-GGUF:UD-Q5_K_M` |
| Base URL | `http://localhost:11434`, `http://host.docker.internal:11434`, or the reachable Ollama URL |
| Keep alive | `5m` |
| Context window (`num_ctx`) | `8192` |
| Temperature | `0.2` |
| Top P | `1` |
| Max output tokens | `1200` |
| Timeout seconds | `120` for this large local model |
| Response format | `Free text` |
| API key | Leave blank |
| Enabled | Checked |
| Default | Checked if this should be the tenant's production AI profile |

Save the profile, then select **Validate**. Validation sends a small prompt through the configured provider and records the result on the profile.

You can also select **List models** after saving the profile. PatchHound calls Ollama's model list endpoint and lets you select one of the models already available on the Ollama host.

## Troubleshooting

If validation says the model was not found, run the pull command on the Ollama host:

```bash
ollama pull hf.co/unsloth/Qwen3.6-35B-A3B-MTP-GGUF:UD-Q5_K_M
```

If PatchHound cannot reach Ollama, verify the profile Base URL from the PatchHound API container or host. For Docker deployments, `localhost` inside a container means the container itself, not the physical host.

If responses time out, increase **Timeout seconds**, lower **Max output tokens**, or use a smaller model. Very large local models can be slow on CPU-only hosts.

If prompts are truncated or reports miss context, set **Context window (`num_ctx`)** to at least `8192`. Higher values require more memory.

## References

- Ollama CLI: https://docs.ollama.com/cli
- Ollama API: https://docs.ollama.com/api
- Ollama Modelfile and parameters: https://docs.ollama.com/modelfile
