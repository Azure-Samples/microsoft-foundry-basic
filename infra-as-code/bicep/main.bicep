targetScope = 'resourceGroup'

@description('The region in which this architecture is deployed. Should match the region of the resource group.')
@minLength(1)
param location string = resourceGroup().location

@description('This is the base name for each Azure resource name (6-8 chars)')
@minLength(6)
@maxLength(20)
param baseName string

@description('Assign your user some roles to support fluid access when working in the Foundry portal and its dependencies.')
@maxLength(36)
@minLength(36)
param yourPrincipalId string

@description('Display / login name used for the Postgres Entra admin entry for the deployer. Purely cosmetic — actual auth is by object ID.')
@minLength(1)
param yourPrincipalDisplayName string = 'deployer'

@description('Microsoft identity application client ID. Configure the app registration for organizational and personal Microsoft accounts.')
param microsoftClientId string = ''

@description('Optional allowed audience for Microsoft identity tokens. Defaults to api://<microsoftClientId>.')
param microsoftAllowedAudience string = ''

@description('Google OAuth application client ID.')
param googleClientId string = ''

@description('Key Vault secret name containing the Microsoft identity application client secret.')
param microsoftClientSecretName string = 'microsoft-login-client-secret'

@description('Key Vault secret name containing the Google OAuth application client secret.')
param googleClientSecretName string = 'google-login-client-secret'

@description('Retain PostgreSQL while user profiles are migrated and verified. Set false only after the migration cutover gate passes.')
param retainPostgres bool = true

@description('Deploy private endpoints and App Service VNet integration. Disable for public endpoints secured by TLS, managed identity, and RBAC.')
param enablePrivateNetworking bool = true

param aiAgentName string = 'translator-agent'
param aiAgentVersion string = '1'

@description('Provision the billable Standard v2 AI gateway. Traffic routing is controlled separately.')
param enableAiGateway bool = false

@description('Route HTTP Foundry project calls through the gateway after app deployment and smoke testing. Requires enableAiGateway.')
param routeAiThroughGateway bool = false

param enableGatewayDepartmentAttribution bool = false
param gatewayFinanceSubscriptionId string = ''

@description('API Management administrator email; required when enableAiGateway is true.')
param aiGatewayPublisherEmail string = ''

@description('Set to true to opt-out of deployment telemetry.')
param telemetryOptOut bool = false

// Customer Usage Attribution Id
var varCuaid = '6aa4564a-a8b7-4ced-8e57-1043a41f4747'

var appName = 'app-${baseName}'
var enableAiGatewayDeployment = enableAiGateway && enablePrivateNetworking

// ---- New resources ----

@description('This is the log sink for all Azure Diagnostics in the workload.')
resource logAnalyticsWorkspace 'Microsoft.OperationalInsights/workspaces@2025-02-01' = {
  name: contains(baseName, '-') ? 'log-${baseName}' : 'log-workload'
  location: location
  properties: {
    sku: {
      name: 'PerGB2018'
    }
    retentionInDays: 30
    forceCmkForQuery: false
    workspaceCapping: {
      dailyQuotaGb: 10 // Production readiness change: In production, tune this value to ensure operational logs are collected, but a reasonable cap is set.
    }
    publicNetworkAccessForIngestion: enablePrivateNetworking ? 'Disabled' : 'Enabled'
    publicNetworkAccessForQuery: 'Enabled'
  }
}

@description('User-assigned managed identity for the App Service. Created here (not inside web-app.bicep) so data-plane resources (Postgres, Key Vault, Web PubSub) can grant it roles before the web app itself is deployed. This breaks the otherwise-circular dependency where web-app needs Postgres FQDN in appsettings and Postgres needs the MI principal ID as an Entra admin.')
resource appServiceManagedIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2024-11-30' = {
  name: contains(baseName, '-') ? 'id-${baseName}' : 'id-${appName}'
  location: location
}

// Deploy the Microsoft Foundry account and Foundry Agent Service components.

@description('Deploy Microsoft Foundry with Foundry Agent Service capability. No projects yet deployed.')
module deployFoundry 'ai-foundry.bicep' = {
  scope: resourceGroup()
  name: 'foundryDeploy'
  params: {
    location: location
    baseName: baseName
    logAnalyticsWorkspaceName: logAnalyticsWorkspace.name
    foundryPortalUserPrincipalId: yourPrincipalId
  }
}

@description('Deploy the Foundry project into the Foundry account. This is the project is the home of the Foundry Agent Service.')
module deployFoundryProject 'ai-foundry-project.bicep' = {
  scope: resourceGroup()
  name: 'foundryProjectDeploy'
  params: {
    location: location
    existingFoundryName: deployFoundry.outputs.foundryName
    existingWebApplicationInsightsResourceName: deployApplicationInsights.outputs.applicationInsightsName
  }
}

// Deploy the shared observability + translator/speech/data plane services.

@description('Deploy Application Insights. Used by the Azure Web App to monitor the deployed application and connected to the Foundry project.')
module deployApplicationInsights 'application-insights.bicep' = {
  scope: resourceGroup()
  name: 'applicationInsightsDeploy'
  params: {
    location: location
    baseName: baseName
    logAnalyticsWorkspaceName: logAnalyticsWorkspace.name
  }
}

@description('Azure AI Translator — used for personal chat text translation.')
module deployTranslator 'translator.bicep' = {
  scope: resourceGroup()
  name: 'translatorDeploy'
  params: {
    location: location
    baseName: baseName
    logAnalyticsWorkspaceName: logAnalyticsWorkspace.name
    enablePrivateNetworking: enablePrivateNetworking
  }
}

@description('Azure AI Speech — used for streaming Speech Translation in meetings and Neural TTS on clients.')
module deploySpeech 'speech.bicep' = {
  scope: resourceGroup()
  name: 'speechDeploy'
  params: {
    location: location
    baseName: baseName
    logAnalyticsWorkspaceName: logAnalyticsWorkspace.name
    enablePrivateNetworking: enablePrivateNetworking
  }
}

@description('Azure Web PubSub — real-time fan-out of translated meeting turns to per-participant per-language groups.')
module deployWebPubSub 'web-pubsub.bicep' = {
  scope: resourceGroup()
  name: 'webPubSubDeploy'
  params: {
    location: location
    baseName: baseName
    logAnalyticsWorkspaceName: logAnalyticsWorkspace.name
    enablePrivateNetworking: enablePrivateNetworking
  }
}

@description('Key Vault — stores the join-token signing key.')
module deployKeyVault 'key-vault.bicep' = {
  scope: resourceGroup()
  name: 'keyVaultDeploy'
  params: {
    location: location
    baseName: baseName
    logAnalyticsWorkspaceName: logAnalyticsWorkspace.name
    deployerPrincipalId: yourPrincipalId
    appServicePrincipalId: appServiceManagedIdentity.properties.principalId
    enablePrivateNetworking: enablePrivateNetworking
  }
}

@description('PostgreSQL retained temporarily as the migration source. Disable only after Table Storage migration verification and rollback validation.')
module deployPostgres 'postgres.bicep' = if (retainPostgres) {
  scope: resourceGroup()
  name: 'postgresDeploy'
  params: {
    location: location
    baseName: baseName
    logAnalyticsWorkspaceName: logAnalyticsWorkspace.name
    deployerPrincipalId: yourPrincipalId
    deployerPrincipalDisplayName: yourPrincipalDisplayName
    appServicePrincipalId: appServiceManagedIdentity.properties.principalId
    appServicePrincipalClientId: appServiceManagedIdentity.properties.clientId
  }
}

// Deploy the Azure Web App resources for the chat UI.

@description('Azure Table Storage for provider-aware user profiles and invitations.')
module deployTableStorage 'storage-tables.bicep' = {
  name: 'tableStorageDeploy'
  params: {
    baseName: baseName
    location: location
    logAnalyticsWorkspaceName: logAnalyticsWorkspace.name
    appServicePrincipalId: appServiceManagedIdentity.properties.principalId
    enablePrivateNetworking: enablePrivateNetworking
  }
}

module deployPrivateNetwork 'private-network.bicep' = if (enablePrivateNetworking) {
  name: 'privateNetworkDeploy'
  params: {
    baseName: baseName
    location: location
    foundryName: deployFoundry.outputs.foundryName
    speechName: deploySpeech.outputs.speechName
    translatorName: deployTranslator.outputs.translatorName
    keyVaultName: deployKeyVault.outputs.keyVaultName
    postgresName: retainPostgres ? deployPostgres!.outputs.pgServerName : ''
    storageAccountName: deployTableStorage.outputs.storageAccountName
    webPubSubName: deployWebPubSub.outputs.webPubSubName
    applicationInsightsName: deployApplicationInsights.outputs.applicationInsightsName
    logAnalyticsWorkspaceName: logAnalyticsWorkspace.name
  }
}

@description('Optional authenticated AI gateway with private outbound connectivity and model usage telemetry.')
module deployAiGateway 'ai-gateway.bicep' = if (enableAiGatewayDeployment) {
  name: 'aiGatewayDeploy'
  params: {
    baseName: baseName
    location: location
    publisherEmail: aiGatewayPublisherEmail
    subnetId: deployPrivateNetwork!.outputs.aiGatewaySubnetId
    projectEndpoint: deployFoundryProject.outputs.projectEndpoint
    projectName: deployFoundryProject.outputs.aiAgentProjectName
    appClientId: appServiceManagedIdentity.properties.clientId
    applicationInsightsName: deployApplicationInsights.outputs.applicationInsightsName
    logAnalyticsWorkspaceName: logAnalyticsWorkspace.name
    requireDepartmentSubscriptions: enableGatewayDepartmentAttribution
    financeSubscriptionId: gatewayFinanceSubscriptionId
  }
}

@description('Deploy the web app with managed identity access to private workload services.')
module deployWebApp 'web-app.bicep' = {
  scope: resourceGroup()
  name: 'webAppDeploy'
  params: {
    location: location
    baseName: baseName
    logAnalyticsWorkspaceName: logAnalyticsWorkspace.name
    existingWebApplicationInsightsResourceName: deployApplicationInsights.outputs.applicationInsightsName
    existingFoundryResourceName: deployFoundry.outputs.foundryName
    existingFoundryProjectName: deployFoundryProject.outputs.aiAgentProjectName
    existingTranslatorResourceName: deployTranslator.outputs.translatorName
    existingSpeechResourceName: deploySpeech.outputs.speechName
    existingWebPubSubResourceName: deployWebPubSub.outputs.webPubSubName
    existingKeyVaultUri: deployKeyVault.outputs.keyVaultUri
    tableStorageEndpoint: deployTableStorage.outputs.tableEndpoint
    usersTableName: deployTableStorage.outputs.usersTableName
    userInvitesTableName: deployTableStorage.outputs.userInvitesTableName
    existingAppServiceManagedIdentityName: appServiceManagedIdentity.name
    microsoftClientId: microsoftClientId
    microsoftAllowedAudience: microsoftAllowedAudience
    googleClientId: googleClientId
    microsoftClientSecretName: microsoftClientSecretName
    googleClientSecretName: googleClientSecretName
    trainingMemberObjectId: yourPrincipalId
    aiAgentName: aiAgentName
    aiAgentVersion: aiAgentVersion
    aiGatewayProjectEndpoint: enableAiGatewayDeployment && routeAiThroughGateway ? deployAiGateway!.outputs.projectEndpoint : ''
    gatewayDepartmentEnabled: enableAiGatewayDeployment && routeAiThroughGateway && enableGatewayDepartmentAttribution
    enableVnetIntegration: enablePrivateNetworking
    appServiceSubnetId: enablePrivateNetworking ? deployPrivateNetwork!.outputs.appServiceSubnetId : ''
  }
}

module deployAiGatewayPrivateNetwork 'ai-gateway-private-network.bicep' = if (enableAiGatewayDeployment) {
  name: 'aiGatewayPrivateNetworkDeploy'
  params: {
    baseName: baseName
    location: location
    gatewayName: deployAiGateway!.outputs.gatewayName
    networkName: deployPrivateNetwork!.outputs.networkName
  }
}

// Optional Deployment for Customer Usage Attribution
module customerUsageAttributionModule 'customerUsageAttribution/cuaIdResourceGroup.bicep' = if (!telemetryOptOut) {
  #disable-next-line no-loc-expr-outside-params // Only to ensure telemetry data is stored in same location as deployment. See https://github.com/Azure/ALZ-Bicep/wiki/FAQ#why-are-some-linter-rules-disabled-via-the-disable-next-line-bicep-function for more information
  name: 'pid-${varCuaid}-${uniqueString(resourceGroup().location)}'
  scope: resourceGroup()
  params: {}
}

// ---- Outputs ----

@description('Post-deploy hint: seed the JWT signing secret into Key Vault before the app can issue meeting join tokens.')
output postDeployHintKeyVault string = 'az keyvault secret set --vault-name ${deployKeyVault.outputs.keyVaultName} --name join-token-signing-key --value "$(openssl rand -base64 64)"'

@description('Post-deploy hint for the temporary PostgreSQL migration source.')
output postDeployHintPostgres string = retainPostgres ? 'PostgreSQL is retained for migration. Run the user migration tool and verify counts before setting retainPostgres=false.' : 'PostgreSQL deployment is disabled after migration cutover.'

output appName string = deployWebApp.outputs.appName
output appHostname string = deployWebApp.outputs.appHostname
output managedIdentityClientId string = appServiceManagedIdentity.properties.clientId
output managedIdentityPrincipalId string = appServiceManagedIdentity.properties.principalId
output foundryName string = deployFoundry.outputs.foundryName
output postgresName string = retainPostgres ? deployPostgres!.outputs.pgServerName : ''
output storageAccountName string = deployTableStorage.outputs.storageAccountName
output keyVaultName string = deployKeyVault.outputs.keyVaultName
output aiGatewayProjectEndpoint string = enableAiGatewayDeployment ? deployAiGateway!.outputs.projectEndpoint : ''

