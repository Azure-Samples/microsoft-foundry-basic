targetScope = 'resourceGroup'

@description('The region in which this architecture is deployed. Should match the region of the resource group.')
@minLength(1)
param location string = resourceGroup().location

@description('This is the base name for each Azure resource name (6-8 chars)')
@minLength(6)
@maxLength(8)
param baseName string

@description('Assign your user some roles to support fluid access when working in the Foundry portal and its dependencies.')
@maxLength(36)
@minLength(36)
param yourPrincipalId string

@description('Display / login name used for the Postgres Entra admin entry for the deployer. Purely cosmetic — actual auth is by object ID.')
@minLength(1)
param yourPrincipalDisplayName string = 'deployer'

@description('Optional: Entra ID application (client) ID used to enable App Service Easy Auth v2. Leave empty to skip Easy Auth wiring for now.')
param entraAppClientId string = ''

@description('Optional: allowed audience for Easy Auth tokens. Defaults to api://<entraAppClientId>.')
param entraAppAllowedAudience string = ''

@description('Set to true to opt-out of deployment telemetry.')
param telemetryOptOut bool = false

// Customer Usage Attribution Id
var varCuaid = '6aa4564a-a8b7-4ced-8e57-1043a41f4747'

var appName = 'app-${baseName}'

// ---- New resources ----

@description('This is the log sink for all Azure Diagnostics in the workload.')
resource logAnalyticsWorkspace 'Microsoft.OperationalInsights/workspaces@2025-02-01' = {
  name: 'log-workload'
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
    publicNetworkAccessForIngestion: 'Enabled'
    publicNetworkAccessForQuery: 'Enabled'
  }
}

@description('User-assigned managed identity for the App Service. Created here (not inside web-app.bicep) so data-plane resources (Postgres, Key Vault, Web PubSub) can grant it roles before the web app itself is deployed. This breaks the otherwise-circular dependency where web-app needs Postgres FQDN in appsettings and Postgres needs the MI principal ID as an Entra admin.')
resource appServiceManagedIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2024-11-30' = {
  name: 'id-${appName}'
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

@description('Deploy the Bing account for Internet grounding data to be used by agents in the Foundry Agent Service.')
module deployBingAccount 'bing-grounding.bicep' = {
  scope: resourceGroup()
  name: 'bingAccountDeploy'
}

@description('Deploy the Foundry project into the Foundry account. This is the project is the home of the Foundry Agent Service.')
module deployFoundryProject 'ai-foundry-project.bicep' = {
  scope: resourceGroup()
  name: 'foundryProjectDeploy'
  params: {
    location: location
    existingFoundryName: deployFoundry.outputs.foundryName
    existingBingAccountName: deployBingAccount.outputs.bingAccountName
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
  }
}

@description('PostgreSQL Flexible Server with pgvector — application data, per-user history, and meeting transcripts.')
module deployPostgres 'postgres.bicep' = {
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

@description('Deploy the web app for the front end demo UI. The web application will call into the Foundry Agent Service, Translator, Speech, Web PubSub, and Postgres — all via managed identity.')
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
    existingPostgresFqdn: deployPostgres.outputs.pgServerFqdn
    existingPostgresDatabase: deployPostgres.outputs.pgDatabaseName
    existingAppServiceManagedIdentityName: appServiceManagedIdentity.name
    entraAppClientId: entraAppClientId
    entraAppAllowedAudience: entraAppAllowedAudience
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

@description('Post-deploy hint: connect to Postgres with your Entra token to run the CREATE EXTENSION vector + migrations.')
output postDeployHintPostgres string = 'PGPASSWORD=$(az account get-access-token --resource-type oss-rdbms --query accessToken -o tsv) psql "host=${deployPostgres.outputs.pgServerFqdn} dbname=${deployPostgres.outputs.pgDatabaseName} user=${yourPrincipalDisplayName} sslmode=require" -c "CREATE EXTENSION IF NOT EXISTS vector;"'

