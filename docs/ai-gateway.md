# AI Gateway Monitoring

The optional Azure API Management Standard v2 gateway provides a custom HTTP project proxy. It uses a dedicated outbound VNet integration subnet to reach the private Foundry endpoint. The `foundry-project` proxy accepts only Entra tokens for the application's managed identity and the Foundry audience. Local developer identities are intentionally not accepted by that proxy. The user restored the Foundry-managed association after the 2026-09-18 cleanup; it is retained alongside the application proxy.

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

The `ai-gateway-monitoring.bicep` module supplies inherited Application Insights and Azure Monitor diagnostics, with no payload/header/client-IP capture and masked query parameters. Its service-level policy retains default forwarding only; token emission is configured on the `foundry-project` API itself. Review existing service policies before deploying this module to a different gateway. Do not assume realtime/audio coverage from successful text tests.

