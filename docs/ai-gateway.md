# AI Gateway Monitoring

The optional Azure API Management Standard v2 gateway provides a custom HTTP project proxy. It uses a dedicated outbound VNet integration subnet to reach the private Foundry endpoint. The `foundry-project` proxy accepts only Entra tokens for the application's managed identity and the Foundry audience. The departmental cutover also requires an APIM subscription key, stripped before forwarding to Foundry. Local developer identities are intentionally not accepted by that proxy. The user restored the Foundry-managed association after the 2026-09-18 cleanup; it is retained alongside the application proxy.

`AIProjectEndpoint` remains the direct Foundry endpoint. The application uses `AIGatewayProjectEndpoint` for its HTTP `AIProjectClient` only. Realtime WebSocket audio, Translator and Speech remain direct. This is not a comprehensive gateway for all model traffic or all internal agent tool calls.

## Deployment

Standard v2 incurs a recurring charge even when idle, in addition to telemetry ingestion, model usage and network charges. Confirm current regional pricing before enabling it. No gateway is provisioned by default. The networking template reserves a dedicated subnet and NSG even when the gateway is disabled.

Set these values in your local deployment environment file:

```dotenv
ENABLE_AI_GATEWAY=true
AI_GATEWAY_PUBLISHER_EMAIL=your-operations-email@example.com
ROUTE_AI_THROUGH_GATEWAY=false
```

Run the existing infrastructure deployment workflow. The script performs what-if followed by deployment; it does not pause between them. For a reviewed rollout, run what-if separately and inspect changes to existing resources before deploying. Direct Bicep callers must not set `routeAiThroughGateway=true` unless `enableAiGateway=true`; the shell script enforces this combination. Provisioning does not deploy application code.

After provisioning:

1. Deploy the updated application containing `EffectiveAIProjectEndpoint` support while routing remains off.
2. In Application Insights, select **Usage and estimated costs > Custom metrics (Preview) > With dimensions**. The API diagnostic already enables metric emission. This portal setting is a required manual step.
3. Verify that an unauthenticated request to the gateway project API is rejected with HTTP 401. Verify authenticated project reads and a small streamed Responses call from the actual App Service container using its managed identity. Do not print bearer tokens or use the SCM container as a substitute for the application's identity environment.
4. Confirm private backend connectivity and telemetry ingestion. Compare completed response usage with emitted prompt/completion metrics, including a streamed response. Treat missing metrics as a rollout blocker, not as zero usage.
5. Set `ROUTE_AI_THROUGH_GATEWAY=true` and redeploy infrastructure. Confirm a real app conversation produces gateway request and token telemetry and that realtime still connects directly.

To roll back routing, keep `ENABLE_AI_GATEWAY=true`, set `ROUTE_AI_THROUGH_GATEWAY=false`, and redeploy. This removes the optional app setting and restores direct HTTP calls. The gateway continues to incur charges. Setting `ENABLE_AI_GATEWAY=false` in an incremental ARM deployment does not delete an existing gateway; removal requires a separately reviewed deletion.

## Foundry Portal Integration

Enabling the gateway also provisions an inbound private endpoint and the `privatelink.azure-api.net` DNS zone linked to the application VNet. Private endpoint charges apply. APIM public access remains enabled; adding the private endpoint does not disable public ingress.

The standalone proxy does not require registration as a Foundry AI Gateway. After the earlier cleanup, the user manually restored the association for Foundry portal visibility. Preserve this association and the generated product/subscription pair ending in `m407glmhdg`. Native traffic remains unverified; portal enrollment does not replace routing and telemetry validation. The application's explicit proxy remains active.

The `ai-gateway-monitoring.bicep` module supplies inherited Application Insights and Azure Monitor diagnostics, with no payload/header/client-IP capture and masked query parameters. Its service-level policy retains default forwarding only; token emission is configured on the `foundry-project` API itself. Review existing service policies before deploying this module to a different gateway. The workbook selects only the application's `foundry-project` proxy. Do not assume realtime/audio coverage from successful text tests.

## Department Access

The isolated `ai-gateway-access.bicep` module and `ai-gateway-access.dev.bicepparam` provision the application-owned product separately from Foundry-managed objects. The dev deployment `ai-gateway-department-access` succeeded on 2026-09-18 after what-if showed only four additions: a product, its API membership and two subscriptions.

| Object | Stable ID | Display name |
| --- | --- | --- |
| Application product | `prd-aitr-dev-swe-001` | AI Translator - Department Access - Dev |
| Finance subscription | `sub-aitr-finance-dev-swe-001` | AI Translator - Finance - Dev |
| HR subscription | `sub-aitr-hr-dev-swe-001` | AI Translator - HR - Dev |
| Unallocated subscription | `sub-aitr-unallocated-dev-swe-001` | AI Translator - Unallocated - Dev |

The product contains only `foundry-project`, requires subscriptions and approval for new subscription requests, and has both initial subscriptions active with tracing disabled. They are standalone subscriptions, not Entra groups or APIM developer accounts; department owners and user membership have not been assigned. APIM generated four distinct keys, a primary/secondary pair per subscription. Their presence and uniqueness were verified without printing or storing their values in repository files, deployment outputs or chat.

### Selected Department Identity

The user selected the following existing Entra group on 2026-09-18. Reuse its immutable object ID; do not create a replacement group or change membership.

| Department label | Entra group object ID | Target APIM subscription |
| --- | --- | --- |
| Finance Dep | `0af088f2-ca45-41dd-9b37-86050b885891` | `sub-aitr-finance-dev-swe-001` |

The group was verified as a Security group in tenant `acaa000d-7d61-417a-99c0-c5d791278c97`. It is assigned the `Department.Finance` app role (`b858965d-18d0-4702-8380-3ccf2c093d91`) on enterprise application `0380c29b-fbfc-43bc-9b3e-29854c29ba13`, application client ID `80196f58-a8b6-407d-a156-10e6c54d5596`. The refreshed browser sign-in was verified to contain that role. No group membership was changed. No HR group has been selected; HR attribution is not enabled.

The backend selects the Finance key only from the trusted App Service Easy Auth principal with the configured tenant, object ID and exactly one department role. Missing, malformed or conflicting department claims, background calls and Development impersonation are Unallocated. Do not enable this configuration on a host that accepts spoofed `X-MS-CLIENT-PRINCIPAL` headers without Easy Auth. Group-based app-role assignment applies to direct members, not nested memberships; users must refresh their sign-in after role assignment changes.

### Credentials and Enforcement

Finance and Unallocated primary keys are stored in Key Vault as `apim-finance-subscription-key` and `apim-unallocated-subscription-key`. The app uses versionless Key Vault references resolved by its existing managed identity; both reference statuses were verified as `Resolved`. HR keys remain unused in APIM. Keys are never supplied to browsers and the built-in `master` subscription is not used for application traffic. Rotate keys with coordinated Key Vault updates and refresh App Service references before retiring the old key.

The app was deployed as `d628f98b-f19a-4ba3-8f69-16ea096e9322` and its deployed assembly hash matched the tested package. `GatewayDepartment__Enabled=true` activates per-request key selection in the shared Foundry SDK HTTP transport. The API now has `subscriptionRequired: true` and still requires the application's managed identity. The API policy strips both key header and query parameter before forwarding, and emits `Department` and `Subscription ID` token dimensions. The workbook filters Finance Dep, Unallocated and historical unattributed data. Rates remain manually configured estimates, not invoices; realtime audio, Speech and Translator remain outside this product's coverage.

Validation: all 53 .NET tests passed, including normal/streaming calls through the actual SDK transport and isolation between successive Finance/non-Finance requests on the cached agent. The live App Service managed-identity probe passed on 2026-09-19: Finance and Unallocated each completed normal and streamed Responses calls with HTTP 200. Each department reported 1,032 input, 20 output and 1,052 total tokens, matching gateway metrics exactly at 11:28 UTC. Missing-key and missing-identity checks both returned HTTP 401. Gateway DNS resolved to private address `10.42.1.25`.

All three workbook queries were executed with Finance selected. The request query uses the verified `ApimSubscriptionId` workspace column; Finance shows two successful calls and the deliberate missing-identity rejection. With rates blank, the cost query returns `PricingConfigured=false` and no dollar estimate. Normal startup (`dotnet /home/site/wwwroot/chatui.dll`) was restored and the platform startup probe passed. The database-backed profile request timed out before deployment; the final browser check encountered an expired sign-in session. Complete UI workflow verification remains unconfirmed, separate from the successful gateway probes.

For future full IaC deployments, set `enableAiGateway=true`, `routeAiThroughGateway=true`, `enableGatewayDepartmentAttribution=true` and `gatewayFinanceSubscriptionId=sub-aitr-finance-dev-swe-001`. Provision the access subscriptions, Key Vault secrets and Entra app-role assignment first; the main template does not create those identity bindings or secret values. The default flags remain off for new deployments. Do not redeploy the full network just to update attribution; live subnet NSG attachments must be preserved.

Rollback order: set only the proxy API's `subscriptionRequired` to false, then set `GatewayDepartment__Enabled=false` in App Service. Keep the direct realtime endpoint, native Foundry association and group memberships unchanged. The shared policy is `infra-as-code/bicep/ai-gateway-policy.xml`; its tenant, app MI and Finance subscription placeholders must be substituted before applying it directly.

Friendly metadata was also applied to the application API and existing Foundry-generated API/product/subscription. Generated IDs, URLs, scopes, policies and association links were preserved. Foundry may overwrite its managed display metadata during reconciliation or re-enrollment; these cosmetic changes are not managed by the access module.

Deploy department configuration separately, after reviewing what-if; do not redeploy the full networking template just to add departments:

```powershell
az deployment group what-if --subscription e7ff8a97-0379-4120-a316-2e4b84a181a5 --resource-group rg-ai-translator-dev-swe-001 --template-file infra-as-code/bicep/ai-gateway-access.bicep --parameters infra-as-code/bicep/ai-gateway-access.dev.bicepparam --mode Incremental
az deployment group create --subscription e7ff8a97-0379-4120-a316-2e4b84a181a5 --resource-group rg-ai-translator-dev-swe-001 --name ai-gateway-department-access --template-file infra-as-code/bicep/ai-gateway-access.bicep --parameters infra-as-code/bicep/ai-gateway-access.dev.bicepparam --mode Incremental --query properties.provisioningState
```

Incremental deployments do not delete subscriptions removed from the parameter list. Offboarding requires explicitly suspending or deleting the relevant subscription. Coordinate any later redeployment with operational subscription state and key rotation.

## Reports and Logs

The gateway module deploys a shared workbook named **AI Translator - gateway usage and cost**, bound to the actual deployment resource IDs. Open **Application Insights > Workbooks**, or **Azure Monitor > Workbooks**, and select it. The workbook includes requests, failures, throttling, latency, token categories and a configurable text-token cost estimate.

| Data | Location |
| --- | --- |
| Gateway request metadata | Linked Log Analytics workspace, `ApiManagementGatewayLogs` |
| Token metrics | Application Insights, `customMetrics`; metric namespace `AITranslator.Gateway` |
| Correlated gateway requests and backend dependencies | Application Insights, `requests` and `dependencies` |
| Direct model usage, including traffic outside the gateway | Foundry account's existing `AzureOpenAIRequestUsage` diagnostic category |
| Actual invoiced or accrued Azure charges | Azure Cost Management, scoped to the subscription/resource group |

Logs can take several minutes to arrive; tables may not exist before first ingestion. Gateway logging uses 100% sampling and can add ingestion cost. Payload bytes and header capture are disabled. Paths, identifiers, timing and status metadata can still be sensitive; apply appropriate workspace access and retention controls. Public monitoring queries remain enabled with Entra/RBAC authorization; public ingestion remains disabled.

Use this Application Insights Logs query to inspect actual metric names and dimensions after the first completed request:

```kusto
customMetrics
| where timestamp > ago(1h)
| where tostring(customDimensions['API ID']) endswith 'foundry-project'
| project timestamp, name, value, valueSum, customDimensions
| order by timestamp desc
```

The workbook expects metric names ending in `Prompt Tokens` and `Completion Tokens`. Confirm these against live telemetry before relying on the estimate; model/API versions can add or change categories. The chart exposes all token metric names so missing categories are visible. Do not add total tokens to prompt/completion tokens or add gateway usage to provider usage for the same calls.

## Cost Analysis Limits

Enter verified USD prices per million input and output tokens in the workbook. Blank rates or no usage samples produce no estimate, rather than a misleading zero-dollar result. This simple estimate is suitable only for a single known text model price tier. It treats all input as uncached and does not separately price cached input, reasoning, audio, agent tools or infrastructure. Interrupted streams may undercount tokens. Do not combine model tiers or use this estimate as a billing ledger.

Use Azure Cost Management to compare Foundry, API Management and Azure Monitor charges and establish a budget. Continue reviewing provider diagnostics for realtime usage. The older translator dashboard's character-based Foundry estimate is not replaced by this workbook and should not be interpreted as measured token cost.

## Validation Status

### Artifact Cleanup

On 2026-09-18, `projchat` was removed from the Foundry-managed gateway and confirmed Disabled. Both reciprocal ARM resource links were deleted, and the Foundry portal confirmed no gateway association. Cleanup then removed the generated API and backend named `ai-aitr-dev-swe-001`, plus both product/subscription pairs ending in `q15wjprj8j` and `pxwmxh938s`. The obsolete global token rule was removed from Azure and Bicep; the gateway module compiled without diagnostics.

Readback confirmed that `foundry-project` is the only API, no products or named backends remain, and `master` is the only subscription. The active API properties and policy exactly matched their pre-cleanup snapshots. Application endpoint settings and startup were unchanged, both monitoring loggers and diagnostics remained, and the private endpoint connection remained present. An unauthenticated proxy request returned HTTP 401 as expected. No application restart or authenticated model request was performed during cleanup. The APIM instance, workbook and private networking were retained; recurring infrastructure charges are unchanged.

### Current Cutover

On 2026-09-18, the gateway-aware application ZIP deployment completed with `RuntimeSuccessful` (deployment `39f0dc98-811a-41d4-8df1-065e727802f9`). The four endpoint-selection tests passed and the gateway Bicep compiled without diagnostics. The existing moderate OpenTelemetry.Api 1.14.0 advisory remains outside this change.

Live `AIGatewayProjectEndpoint` is now `https://apim-aitr-dev-swe-001.azure-api.net/api/projects/projchat`. `AIProjectEndpoint` remains the original Foundry endpoint, used by realtime controllers. Normal startup remains `dotnet /home/site/wwwroot/chatui.dll`. The workbook was published and read back with all three queries selecting `foundry-project`. The proxy Azure Monitor diagnostic now uses information verbosity, with client IP logging off and payload capture still zero bytes. Successful-request ingestion after that verbosity change has not yet been verified.

The authenticated application smoke test at cutover was blocked: `/api/me` returned HTTP 500 because Npgsql timed out connecting to PostgreSQL at `10.42.1.10:5432`, before an agent request could be made. No database or network configuration was changed during this cutover. That failure was not rechecked during artifact cleanup and is not a current availability assessment. The earlier isolated managed-identity REST and streaming tests below remain valid, but they do not substitute for a deployed application SDK workflow. Application-level gateway and realtime verification have not been completed by this rollout. The override remains enabled; rollback requires deleting only `AIGatewayProjectEndpoint` from App Service settings, not replacing `AIProjectEndpoint`. Keep `ENABLE_AI_GATEWAY=true` and `ROUTE_AI_THROUGH_GATEWAY=true` in deployment inputs to preserve this live configuration on future infrastructure deployments; repository example defaults remain opt-in.

### Earlier Verification

Bicep compilation, deployment script syntax and the four HTTP endpoint-selection test cases passed locally. The standalone `ai-gateway-staged` Azure deployment succeeded in `rg-ai-translator-dev-swe-001`, provisioning `apim-aitr-dev-swe-001` (Standard v2), its policies, diagnostics and shared workbook. An unauthenticated project request returned HTTP 401. Azure readback confirmed the dedicated subnet, zero captured payload bytes, disabled client IP logging and enabled Application Insights metrics.

Earlier on 2026-09-18, Foundry associated `ai-aitr-dev-swe-001` with the existing `apim-aitr-dev-swe-001`; the portal reported `projchat` as Enabled. Foundry created API `ai-aitr-dev-swe-001` at a nonconflicting path. No token limits or quotas were configured. The monitoring module deployment succeeded and the workbook was republished for that API. Bicep compilation passed without diagnostics. This association and generated API were subsequently removed as recorded above.

Before the cutover, the application used only its original project endpoint. Authenticated tests through that endpoint completed: a model Responses call (18 tokens), an SSE response (HTTP 200, 21 tokens), and existing-agent calls (526 tokens each). However, APIM platform request counts and gateway/token logs did not show corresponding traffic during verification. Removing and re-adding `projchat` once restored Enabled status but did not establish traffic evidence. Treat Foundry-managed routing as unverified, not complete. Gateway charges continue.

The separate custom proxy passed an actual App Service managed-identity test on 2026-09-18 at approximately 10:35 UTC. Gateway DNS resolved to private IP `10.42.1.25`. Both normal and streamed agent Responses calls returned HTTP 200 and completed, each reporting 516 input and 10 output tokens. Application Insights recorded two HTTP 200 requests and dimensioned metrics for API ID `foundry-project`: 1,032 Prompt Tokens, 20 Completion Tokens and 1,052 Total Tokens, exactly matching response usage. This verifies private app-to-gateway connectivity and successful backend calls, but does not independently establish the backend's DNS route. Successful requests were not present in `ApiManagementGatewayLogs` with its then-error verbosity. The workbook selection and verbosity were subsequently changed as recorded above.

The original Python startup probe could not run because the .NET container lacks Python. Its replacement, `scripts/verify-ai-gateway.cs`, is a standalone .NET 10 probe with a 120-second deadline, no token or content logging, and a local `--self-test`. Publish it framework-dependent with `dotnet publish scripts/verify-ai-gateway.cs -c Release --no-self-contained -p:UseAppHost=false -o <output>`. The diagnostic wrapper uses a persistent once-only guard. After the successful test, live startup was restored to `dotnet /home/site/wwwroot/chatui.dll`; the restored container passed the platform startup probe. Normal application routing was not changed.

The isolated `ai-gateway-private` deployment also succeeded. Azure reports the `Gateway` private endpoint connection as Approved, its DNS A record as `10.42.1.25`, and the VNet DNS link as Completed. What-if showed four additions and no changes to existing resources. App-to-gateway private connectivity is now verified as described above. Foundry public access was subsequently enabled at the user's request, retaining Entra-only authentication and private endpoints. The project's Monitoring Reader assignment on Application Insights is verified and represented in Bicep.

Gateway project endpoint: `https://apim-aitr-dev-swe-001.azure-api.net/api/projects/projchat`.

The isolated deployment preserved existing subnet NSG attachments that are not represented in the current full-network template. Review that drift before any full infrastructure redeployment.