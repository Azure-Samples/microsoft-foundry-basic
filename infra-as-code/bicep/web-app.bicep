targetScope = 'resourceGroup'

/*
  Deploy a web app with a managed identity, diagnostic, and a private endpoint
*/

@description('This is the base name for each Azure resource name (6-8 chars)')
@minLength(6)
@maxLength(8)
param baseName string

@description('The region in which this architecture is deployed. Should match the region of the resource group.')
@minLength(1)
param location string = resourceGroup().location

@description('The name of the workload\'s existing Log Analytics workspace.')
@minLength(4)
param logAnalyticsWorkspaceName string

@description('The name of the existing Azure Application Insights instance that the Azure Web App will be using.')
@minLength(1)
param existingWebApplicationInsightsResourceName string

@description('The name of the existing Microsoft Foundry instance that the Azure Web App code will be calling for Foundry Agent Service agents.')
@minLength(2)
param existingFoundryResourceName string

@description('The name of the existing Foundry project name.')
@minLength(2)
param existingFoundryProjectName string

@description('The name of the existing Azure AI Translator account. The web app calls it via managed identity for personal chat translation.')
@minLength(2)
param existingTranslatorResourceName string

@description('The name of the existing Azure AI Speech account. The web app relays meeting audio to Speech Translation with an Entra-issued token.')
@minLength(2)
param existingSpeechResourceName string

@description('The name of the existing Azure Web PubSub account. The web app issues per-participant client access URLs for meeting turn fan-out.')
@minLength(3)
param existingWebPubSubResourceName string

@description('The URI of the existing Key Vault, e.g. https://kv-xxxx.vault.azure.net/. The web app fetches join-token signing key from here.')
@minLength(2)
param existingKeyVaultUri string

@description('FQDN of the existing PostgreSQL server, e.g. pg-xxxx.postgres.database.azure.com. App connects with Npgsql using an Entra token.')
@minLength(2)
param existingPostgresFqdn string

@description('Application database name on the PostgreSQL server.')
@minLength(1)
param existingPostgresDatabase string

@description('Optional: Entra ID application (client) ID used to enable App Service Easy Auth v2. Leave empty to skip Easy Auth wiring; you can configure it in the Portal or from a follow-up deployment once the app registration exists.')
param entraAppClientId string = ''

@description('Optional: allowed audience for Easy Auth tokens. Defaults to api://<entraAppClientId>. Only used when entraAppClientId is provided.')
param entraAppAllowedAudience string = ''

@description('The name of the pre-created user-assigned managed identity that the App Service will run under. Created in main.bicep so data-plane resources can grant it roles before the web app exists.')
@minLength(3)
param existingAppServiceManagedIdentityName string

// variables
var appName = 'app-${baseName}'
var enableEasyAuth = !empty(entraAppClientId)

// ---- Existing resources ----

@description('Existing Application Insights instance. Logs from the web app will be sent here.')
resource applicationInsights 'Microsoft.Insights/components@2020-02-02' existing = {
  name: existingWebApplicationInsightsResourceName
}

resource logWorkspace 'Microsoft.OperationalInsights/workspaces@2025-02-01' existing = {
  name: logAnalyticsWorkspaceName
}

@description('Built-in Role: [Azure AI User](https://learn.microsoft.com/azure/ai-foundry/concepts/rbac-azure-ai-foundry?pivots=fdp-project#azure-ai-user)')
resource azureAiUserRole 'Microsoft.Authorization/roleDefinitions@2022-04-01' existing = {
  name: '53ca6127-db72-4b80-b1b0-d745d6d5456d'
  scope: subscription()
}

@description('Built-in role: Cognitive Services User (Entra-based data-plane calls to Translator and Speech).')
resource cognitiveServicesUserRole 'Microsoft.Authorization/roleDefinitions@2022-04-01' existing = {
  name: 'a97b65f3-24c7-4388-baec-2e87135dc908'
  scope: subscription()
}

@description('Built-in role: Web PubSub Service Owner (full data-plane, includes issuing client access URLs).')
resource webPubSubServiceOwnerRole 'Microsoft.Authorization/roleDefinitions@2022-04-01' existing = {
  name: '12cf5a90-567b-43ae-8102-96cf46c7d9b4'
  scope: subscription()
}

@description('Existing Foundry account. This account is where the agents hosted in Foundry Agent Service will be deployed. The web app code calls to these agents.')
resource foundry 'Microsoft.CognitiveServices/accounts@2025-06-01' existing = {
  name: existingFoundryResourceName

  resource project 'projects' existing = {
    name: existingFoundryProjectName
   }
}

@description('Existing Translator account.')
resource translator 'Microsoft.CognitiveServices/accounts@2025-06-01' existing = {
  name: existingTranslatorResourceName
}

@description('Existing Speech account.')
resource speech 'Microsoft.CognitiveServices/accounts@2025-06-01' existing = {
  name: existingSpeechResourceName
}

@description('Existing Web PubSub account.')
resource webPubSub 'Microsoft.SignalRService/webPubSub@2024-03-01' existing = {
  name: existingWebPubSubResourceName
}

// ---- New resources ----

@description('Managed Identity for App Service. Passed in from the top-level deployment to break the circular dependency with data-plane resources (Postgres, Key Vault) that need this identity to create role assignments before the web app is deployed.')
resource appServiceManagedIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2024-11-30' existing = {
  name: existingAppServiceManagedIdentityName
}

@description('Grant the App Service managed identity Azure AI user role permission so it can call into the Foundry-hosted agent.')
resource azureAiUserRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: foundry
  name: guid(foundry.id, appServiceManagedIdentity.id, azureAiUserRole.id)
  properties: {
    roleDefinitionId: azureAiUserRole.id
    principalType: 'ServicePrincipal'
    principalId: appServiceManagedIdentity.properties.principalId
  }
}

@description('Grant the App Service MI Cognitive Services User on Translator so it can call the Translator API without keys.')
resource translatorUserRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: translator
  name: guid(translator.id, appServiceManagedIdentity.id, cognitiveServicesUserRole.id)
  properties: {
    roleDefinitionId: cognitiveServicesUserRole.id
    principalType: 'ServicePrincipal'
    principalId: appServiceManagedIdentity.properties.principalId
  }
}

@description('Grant the App Service MI Cognitive Services User on Speech so it can request short-lived tokens for the browser Speech SDK and relay audio to Speech Translation.')
resource speechUserRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: speech
  name: guid(speech.id, appServiceManagedIdentity.id, cognitiveServicesUserRole.id)
  properties: {
    roleDefinitionId: cognitiveServicesUserRole.id
    principalType: 'ServicePrincipal'
    principalId: appServiceManagedIdentity.properties.principalId
  }
}

@description('Grant the App Service MI Web PubSub Service Owner so it can broadcast messages and issue per-participant client access URLs.')
resource webPubSubOwnerRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: webPubSub
  name: guid(webPubSub.id, appServiceManagedIdentity.id, webPubSubServiceOwnerRole.id)
  properties: {
    roleDefinitionId: webPubSubServiceOwnerRole.id
    principalType: 'ServicePrincipal'
    principalId: appServiceManagedIdentity.properties.principalId
  }
}

@description('Linux, B1 App Service Plan to host the chat web application.')
resource appServicePlan 'Microsoft.Web/serverfarms@2024-11-01' = {
  name: 'asp-${appName}${uniqueString(subscription().subscriptionId)}'
  location: location
  kind: 'linux'
  sku: {
    name: 'B1'  // az appservice list-locations --linux-workers-enabled --sku B1
    tier: 'Basic'
    capacity: 1
  }
  properties: {
    zoneRedundant: false
    reserved: true
  }
}

@description('This is the web app that contains the chat UI application.')
resource webApp 'Microsoft.Web/sites@2024-11-01' = {
  name: appName
  location: location
  kind: 'app,linux'
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${appServiceManagedIdentity.id}': {}
    }
  }
  properties: {
    enabled: true
    serverFarmId: appServicePlan.id
    virtualNetworkSubnetId: null
    httpsOnly: true
    #disable-next-line BCP037 // This is a valid property, just not part of the schema https://github.com/Azure/bicep-types-az/issues/2204
    sshEnabled: false
    autoGeneratedDomainNameLabelScope: 'SubscriptionReuse'
    publicNetworkAccess: 'Enabled'
    keyVaultReferenceIdentity: null
    endToEndEncryptionEnabled: true
    hostNamesDisabled: false
    clientAffinityEnabled: false
    siteConfig: {
      ftpsState: 'Disabled'
      vnetRouteAllEnabled: false
      http20Enabled: false
      publicNetworkAccess: 'Enabled'
      alwaysOn: true
      linuxFxVersion: 'DOTNETCORE|10.0'
      netFrameworkVersion: null
      windowsFxVersion: null
    }
  }
  dependsOn: []

  @description('Default configuration for the web app.')
  resource appsettings 'config' = {
    name: 'appsettings'
    properties: {
      APPLICATIONINSIGHTS_CONNECTION_STRING: applicationInsights.properties.ConnectionString
      AZURE_CLIENT_ID: appServiceManagedIdentity.properties.clientId
      ApplicationInsightsAgent_EXTENSION_VERSION: '~3'
      AIProjectEndpoint: foundry::project.properties.endpoints['AI Foundry API']
      AIAgentId: 'Not yet set' // Will be set once the agent is created
      AIAgentVersion: 'Not yet set' // Will be set once the agent is created
      Translator__Endpoint: translator.properties.endpoint
      Translator__Region: translator.location
      SpeechEndpoint: speech.properties.endpoint
      SpeechRegion: speech.location
      SpeechResourceId: speech.id
      WebPubSubEndpoint: 'https://${webPubSub.properties.hostName}'
      WebPubSubHub: 'meetings'
      KeyVaultUri: existingKeyVaultUri
      JoinTokenSigningKeySecretName: 'join-token-signing-key'
      Postgres__Host: existingPostgresFqdn
      Postgres__Database: existingPostgresDatabase
      Postgres__Username: appServiceManagedIdentity.properties.clientId
      XDT_MicrosoftApplicationInsights_Mode: 'Recommended'
    }
  }

  @description('Enable App Service Easy Auth v2 with Entra ID. Only wired when an app-registration client ID is supplied.')
  resource authV2 'config' = if (enableEasyAuth) {
    name: 'authsettingsV2'
    properties: {
      platform: {
        enabled: true
        runtimeVersion: '~1'
      }
      globalValidation: {
        requireAuthentication: true
        unauthenticatedClientAction: 'RedirectToLoginPage'
        redirectToProvider: 'azureactivedirectory'
      }
      identityProviders: {
        azureActiveDirectory: {
          enabled: true
          registration: {
            openIdIssuer: '${environment().authentication.loginEndpoint}${subscription().tenantId}/v2.0'
            clientId: entraAppClientId
          }
          validation: {
            allowedAudiences: [
              empty(entraAppAllowedAudience) ? 'api://${entraAppClientId}' : entraAppAllowedAudience
            ]
          }
          login: {
            disableWWWAuthenticate: false
          }
        }
      }
      login: {
        tokenStore: {
          enabled: true
        }
      }
      httpSettings: {
        requireHttps: true
        forwardProxy: {
          convention: 'NoProxy'
        }
      }
    }
  }

  @description('Disable SCM publishing integration.')
  resource scm 'basicPublishingCredentialsPolicies' = {
    name: 'scm'
    properties: {
      allow: false
    }
  }

  @description('Disable FTP publishing integration.')
  resource ftp 'basicPublishingCredentialsPolicies' = {
    name: 'ftp'
    properties: {
      allow: false
    }
  }
}

@description('Enable App Service Azure Diagnostic')
resource azureDiagnostics 'Microsoft.Insights/diagnosticSettings@2021-05-01-preview' = {
  name: 'default'
  scope: webApp
  properties: {
    workspaceId: logWorkspace.id
    logs: [
      {
        category: 'AppServiceHTTPLogs'
        enabled: true
      }
      {
        category: 'AppServiceConsoleLogs'
        enabled: true
      }
      {
        category: 'AppServiceAppLogs'
        enabled: true
      }
      {
        category: 'AppServicePlatformLogs'
        enabled: true
      }
      {
        category: 'AppServiceAuditLogs'
        enabled: true
      }
      {
        category: 'AppServiceIPSecAuditLogs'
        enabled: true
      }
      {
        category: 'AppServiceAuthenticationLogs'
        enabled: true
      }
    ]
  }
}

// ---- Outputs ----

@description('The name of the app service plan.')
output appServicePlanName string = appServicePlan.name

@description('The name of the web app.')
output appName string = webApp.name

@description('The principal ID of the App Service user-assigned managed identity. Used by other modules to grant data-plane roles (e.g. Postgres Entra admin).')
output appServicePrincipalId string = appServiceManagedIdentity.properties.principalId

@description('The client ID of the App Service user-assigned managed identity. Used as the Entra principal name on Postgres.')
output appServicePrincipalClientId string = appServiceManagedIdentity.properties.clientId
