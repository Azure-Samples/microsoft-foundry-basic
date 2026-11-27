## Plan: Consumer Authentication and Table Storage

Replace tenant-only Entra/PostgreSQL user persistence with direct Microsoft personal plus Google App Service authentication and managed-identity Azure Table Storage. Preserve the current invite-only security boundary, stable user GUIDs, API contracts, and all unrelated in-progress gateway/microphone work. PostgreSQL remains only for the one-time migration, then is removed after reconciliation.

**Steps**

### Phase 1 — Identity and storage contracts
1. Introduce normalized external identity (`Provider`, normalized `Issuer`, stable `Subject`) and update development identities while retaining existing Microsoft object IDs as the stable Microsoft subject.
2. Add `Azure.Data.Tables`, Table Storage options, profile/invitation entities, and a repository with point lookup, bounded directory search, profile update, invitation claim, and idempotent upsert operations.
3. Replace EF Core usage in `EasyAuthUserContext`, `MeController`, `UsersController`, and `ConversationsController`; preserve current response models and `User.Id` values.
4. Add application-side protection for API and WebSocket routes because multiple direct providers require the public static login selector to load before authentication.

### Phase 2 — Provider experience and authorization
5. Normalize Easy Auth claims for Microsoft (`oid`, fallback `sub`) and Google (`sub`), with provider separation and no email-based profile merging.
6. Add Microsoft and Google sign-in choices and sign-out behavior to the existing UI; unauthenticated API responses remain 401/403 and uninvited authenticated users remain rejected.
7. Preserve invite-only operation with operator-created invitations. Invitation claiming uses a provider-authenticated email only to claim an explicit invitation; the profile key remains provider plus stable subject.
8. Preserve training membership for migrated Microsoft identities and add a provider-aware membership form for new consumer identities.

### Phase 3 — Migration and infrastructure
9. Add idempotent operator commands that (a) create invitations using managed identity and protected JSON input and (b) migrate PostgreSQL users to Table Storage while preserving GUIDs and reporting only aggregate counts.
10. Add a Standard LRS StorageV2 account and `users`/`userinvites` tables, disable shared-key access, grant the App Service managed identity `Storage Table Data Contributor`, and add table private endpoint/DNS wiring consistent with the current VNet.
11. Configure App Service Authentication v2 with direct Microsoft and Google providers. Keep OAuth secrets in Key Vault-backed app settings. Use application-controlled login selection rather than a single provider redirect.
12. Update deployment scripts for provider IDs/secret names and the migration/cutover sequence.
13. Remove PostgreSQL Bicep, private endpoint/DNS, app settings, EF/Npgsql/Pgvector packages, migrations, and bootstrap paths only after the migration command and reconciliation checks are complete in the implementation.

### Phase 4 — Tests and documentation
14. Add claim-normalization, provider separation, invitation, Table repository, migration idempotency, authorization-boundary, and stable-GUID tests. Update existing fixtures without altering unrelated test intent.
15. Update `README.md`, `docs/translator-design.md`, and `docs/implementation.md` with the final architecture, provider setup, invitation operations, migration/rollback sequence, and cost impact.
16. Run targeted .NET tests, the full .NET suite, Node browser tests, Bicep builds, and scoped whitespace/diff validation.

**Relevant files**
- `website/chatui/Program.cs` — dependency registration, operator commands, route protection.
- `website/chatui/Auth/EasyAuthUserContext.cs` — provider-aware claim normalization and invite-only lookup.
- `website/chatui/Auth/ResolvedPrincipal.cs` — provider/issuer/subject identity.
- `website/chatui/Data/` — Table entities, repository, invitations, and migration tooling.
- `website/chatui/Controllers/MeController.cs` — Table profile updates.
- `website/chatui/Controllers/UsersController.cs` — bounded Table directory search.
- `website/chatui/Controllers/ConversationsController.cs` — Table profile lookup.
- `website/chatui/wwwroot/index.html` and `website/chatui/wwwroot/app.js` — provider selector and sign-out.
- `website/chatui/chatui.csproj` — Azure Tables dependencies and PostgreSQL removal.
- `infra-as-code/bicep/main.bicep` — storage module, wiring, and PostgreSQL removal.
- `infra-as-code/bicep/web-app.bicep` — storage settings, RBAC, and two Easy Auth providers.
- `infra-as-code/bicep/private-network.bicep` — Table endpoint private networking.
- `scripts/deploy-infra.env` and `scripts/deploy-infra.sh` — provider and migration parameters.
- `website/chatui.Tests/` — authentication, persistence, migration, and regression tests.

**Verification**
1. `dotnet test website/chatui.Tests/chatui.Tests.csproj` passes with Microsoft, Google, invitation, and migration coverage.
2. `node --test website/chatui.Tests/talk-connection.test.cjs` remains green.
3. `dotnet publish website/chatui/chatui.csproj -c Release` succeeds without EF/Npgsql/Pgvector runtime dependencies.
4. Bicep builds succeed for `infra-as-code/bicep/main.bicep` and parameter files.
5. A dry-run deployment shows creation of Table Storage/provider configuration and removal of PostgreSQL only after operator migration verification.
6. Deployed smoke tests validate both provider redirects, an invited Microsoft user, an invited Google user, rejection of an uninvited identity, language persistence, and user search.

**Decisions**
- Direct App Service Microsoft and Google providers, not Entra External ID.
- Invite-only authorization; no public self-registration.
- Microsoft and Google identities remain separate even with matching emails.
- Existing Microsoft workforce `oid` values are migrated as Microsoft stable subjects so current users retain access and GUIDs.
- AZD template is `none`; retain the repository's existing Bicep/deployment-script workflow.
- Google OAuth credentials are operator-supplied and never generated or committed.

**Further Considerations**
1. PostgreSQL deletion is a separate deployment cutover after row-count/profile verification; implementation supplies the safe path but does not delete live data automatically.
2. Table scans are acceptable for the current small invite-only directory. If the directory grows materially, add a dedicated search index rather than Cosmos DB prematurely.

## Production deployment extension

### Phase 5 — Public-secured production infrastructure
17. Deploy to tenant `871d492f-64dd-4e37-9958-f6c19fb0118d`, subscription `164328f9-1cf3-4f78-8cd0-728122021b45`, resource group `rg-aitr-prod-swe-001`, in Sweden Central.
18. Parameterize the existing Bicep so private networking remains the default while this deployment disables private endpoints, App Service VNet integration, NAT Gateway, and AI Gateway.
19. Keep all service endpoints HTTPS-only. Disable shared-key/local authentication where supported and grant the App Service user-assigned managed identity only the required Foundry, Cognitive Services, Web PubSub, Table Storage, and Key Vault roles.
20. Create an approved multitenant Microsoft identity registration supporting organizational and personal Microsoft accounts. Store its credential only in Key Vault. Defer Google until its client credentials are supplied securely.
21. Do not provision PostgreSQL in the new tenant. Import profiles only if an existing deployment is selected as a migration source.

### Phase 6 — Managed Prompt Voice
22. Define `translator-voice` in `azure.yaml` as a canonical managed `kind: voice` service backed by `gpt-realtime`, 24-kHz PCM, Azure Speech transcription, server VAD, and `createResponse: false`.
23. Add an authenticated ASP.NET WebSocket bridge at `/api/speech/voice-agent` using managed identity and the `https://ai.azure.com/.default` scope.
24. Add a persistent `foundry-voice` browser recognition mode that uses 24-kHz capture and maps Voice Agent transcription events to the existing browser translation/TTS pipeline.
25. Preserve `s2s-realtime` as the default and rollback path. Do not treat agent response audio or transcripts as user speech.

### Phase 7 — Validation and deployment
26. Run the full .NET suite, all Node browser audio tests, Release publish, Bicep formatting/builds, and `azd ai agent doctor` against the `prod` environment.
27. Run Azure deployment validation and resource-group what-if. Review destructive changes, policy failures, quota, provider registration, and role-assignment permissions before provisioning.
28. Provision resources, deploy the web application and Voice Agent, seed Key Vault secrets, create initial invitations, and verify authentication, Table RBAC, microphone capture, chat translation, meeting fan-out, managed Voice, and rollback.
29. Record the deployed URL, agent version, validation evidence, and any deferred Google setup without exposing credentials.

**Production verification gates**
1. No PostgreSQL, private endpoint, VNet integration, NAT Gateway, API Management, or ACR resource appears in the approved what-if.
2. The public site requires provider authentication for protected application operations; an authenticated but uninvited identity is rejected.
3. Storage shared-key access is disabled and the application can read/write profiles through managed identity.
4. `foundry-voice` produces transcription from microphone audio, while switching back to `s2s-realtime` works without redeployment.
5. Resource names follow `{type}-aitr-prod-swe-001`, with `staitrprodswe001` for the storage account.
