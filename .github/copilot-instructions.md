# Copilot Instructions

## Azure Resource Naming Convention

When creating Azure resources (resource groups, storage accounts, AI services, etc.), follow this naming pattern:

```
{type}-{project}-{env}-{region}-{instance}
```

| Segment    | Description                          | Examples                        |
|------------|--------------------------------------|---------------------------------|
| `type`     | Resource type abbreviation           | `rg`, `st`, `ai`, `kv`, `cosmos` |
| `project`  | Project or workload name             | `minion`                        |
| `env`      | Environment                          | `dev`, `stg`, `prod`            |
| `region`   | Azure region short code              | `swe` (Sweden Central), `eus`   |
| `instance` | Numeric instance identifier          | `001`, `002`, `003`             |

**Examples:**
- Resource group: `rg-minion-dev-swe-003`
- Storage account: `stminiondevswe003` (no hyphens — storage account limitation)
- Key Vault: `kv-minion-dev-swe-003`

Always use lowercase. For resources that don't allow hyphens (e.g. storage accounts), concatenate segments without separators.

## Authorization for Azure Resources

Use Entra managed identities or service principals for authentication. Assign the least privilege necessary to service principals or managed identities. For local development, use `az login` to authenticate with your user account, which should have appropriate permissions on the Azure resources.
example:

credential = DefaultAzureCredential(), not connection strings or keys.

## This project uses Azure DevOps.

Always check whether the Azure DevOps MCP server has a tool relevant to the user's request before answering from general knowledge.
Default organization: ContosoMortgage123.
When the project is not specified, ask once and remember for the rest of the conversation.
When creating work items, always set area path, iteration, and assignee if known.
When creating PRs, link the related work item in the description.

## Python virtual environments
Always use the existing `.venv` in the project root.
Do not create a new virtual environment.
Do not install Python packages directly with `pip install <package>`.
If a dependency is needed, update the appropriate requirements file first, then install with `.venv/bin/python -m pip install -r requirements.txt`.




What to avoid:
Synchronous Azure SDK clients
requests library (use httpx if needed)
Hardcoded endpoints, keys, or container names