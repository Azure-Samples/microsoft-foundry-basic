# Foundry agents

Versioned source of truth for the Foundry Agent Service agents used by the
translator workload. Files here are **not** deployed automatically by Bicep —
Foundry agent lifecycle is a data-plane operation on top of the Foundry
project, so applying an agent version is a post-deploy step.

## `translator-agent.json`

Reasoning agent used by the .NET `FoundryAgentService` for two tasks:

1. **Addressee hint** — inspects the recent meeting turns and participant list
   to guess which participant the newest turn is aimed at. Consumed by the UI
   only for emphasis; delivery of translated turns is not gated by this.
2. **Meeting summary** — produces a compact 3–6-sentence summary of the
   meeting in a requested BCP-47 language.

The system prompt requires strict-JSON responses for both tasks so the
service can deserialise without extra parsing.

## Applying a new agent version

Until CI/CD in Azure DevOps lands (`cicd-ado` todo), roll out changes manually:

1. In the Foundry portal (Build → Agents), create or edit the agent named
   `translator-agent`.
2. Set model to `agent-model` (defined in `infra-as-code/bicep/ai-foundry.bicep`).
3. Paste the `instructions` field from `translator-agent.json`.
4. Leave knowledge / tools empty (no Bing grounding needed for these tasks).
5. Save. Copy the returned agent id + version and set them on the App Service:

   ```bash
   az webapp config appsettings set \
     -g $RESOURCE_GROUP -n app-$BASE_NAME \
     --settings AIAgentId=<agent-id> AIAgentVersion=<version>
   ```

Alternative REST bootstrap:

```bash
FOUNDRY_NAME="aif${BASE_NAME}"
FOUNDRY_PROJECT_NAME="projchat"
FOUNDRY_AGENTS_URL="https://${FOUNDRY_NAME}.services.ai.azure.com/api/projects/${FOUNDRY_PROJECT_NAME}/agents?api-version=2025-11-15-preview"

az rest -u "$FOUNDRY_AGENTS_URL" -m POST \
  --resource "https://ai.azure.com" \
  --headers "Content-Type=application/json" \
  --body @agents/translator-agent.json
```
