targetScope = 'resourceGroup'

/*
  Deploy a web app with a managed identity, diagnostic, and a private endpoint
*/

@description('This is the base name for each Azure resource name (6-8 chars)')
@minLength(6)
@maxLength(20)
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

@description('Azure Table Storage service endpoint.')
param tableStorageEndpoint string

@description('Table containing provider-aware user profiles.')
param usersTableName string = 'users'

@description('Table containing explicit user invitations.')
param userInvitesTableName string = 'userinvites'

@description('Microsoft identity application client ID. The app registration must allow organizational and personal Microsoft accounts.')
param microsoftClientId string = ''

@description('Optional allowed audience for Microsoft identity tokens. Defaults to api://<microsoftClientId>.')
param microsoftAllowedAudience string = ''

@description('Google OAuth application client ID.')
param googleClientId string = ''

@description('Key Vault secret containing the Microsoft identity application client secret.')
param microsoftClientSecretName string = 'microsoft-login-client-secret'

@description('Key Vault secret containing the Google OAuth application client secret.')
param googleClientSecretName string = 'google-login-client-secret'

param trainingMemberObjectId string = ''
@description('Integrate App Service with the supplied virtual network subnet.')
param enableVnetIntegration bool = true
param appServiceSubnetId string = ''
param aiAgentName string = 'translator-agent'
param aiAgentVersion string = '1'

@description('The name of the pre-created user-assigned managed identity that the App Service will run under. Created in main.bicep so data-plane resources can grant it roles before the web app exists.')
@minLength(3)
param existingAppServiceManagedIdentityName string

// variables
var appName = 'app-${baseName}'
var enableEasyAuth = !empty(microsoftClientId) || !empty(googleClientId)
var authProviderSettings = union(
  empty(microsoftClientId) ? {} : {
    MICROSOFT_PROVIDER_AUTHENTICATION_SECRET: '@Microsoft.KeyVault(SecretUri=${existingKeyVaultUri}secrets/${microsoftClientSecretName})'
  },
  empty(googleClientId) ? {} : {
    GOOGLE_PROVIDER_AUTHENTICATION_SECRET: '@Microsoft.KeyVault(SecretUri=${existingKeyVaultUri}secrets/${googleClientSecretName})'
  }
)

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
  name: contains(baseName, '-') ? 'asp-${baseName}' : 'asp-${appName}${uniqueString(subscription().subscriptionId)}'
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
    virtualNetworkSubnetId: enableVnetIntegration ? appServiceSubnetId : null
    httpsOnly: true
    #disable-next-line BCP037 // This is a valid property, just not part of the schema https://github.com/Azure/bicep-types-az/issues/2204
    sshEnabled: false
    autoGeneratedDomainNameLabelScope: 'SubscriptionReuse'
    publicNetworkAccess: 'Enabled'
    keyVaultReferenceIdentity: appServiceManagedIdentity.id
    endToEndEncryptionEnabled: true
    hostNamesDisabled: false
    clientAffinityEnabled: false
    siteConfig: {
      ftpsState: 'Disabled'
      vnetRouteAllEnabled: false
      http20Enabled: false
      publicNetworkAccess: 'Enabled'
      alwaysOn: true
      webSocketsEnabled: true
      linuxFxVersion: 'DOTNETCORE|10.0'
      netFrameworkVersion: null
      windowsFxVersion: null
    }
  }
  dependsOn: []

  @description('Default configuration for the web app.')
  resource appsettings 'config' = {
    name: 'appsettings'
    properties: union({
      APPLICATIONINSIGHTS_CONNECTION_STRING: applicationInsights.properties.ConnectionString
      AZURE_CLIENT_ID: appServiceManagedIdentity.properties.clientId
      AZURE_TENANT_ID: subscription().tenantId
      ASPNETCORE_ENVIRONMENT: 'Production'
      DevAuth__Enabled: 'false'
      TrainingGuidedEnabled: 'true'
      TrainingMemberObjectIds__0: trainingMemberObjectId
      ApplicationInsightsAgent_EXTENSION_VERSION: '~3'
      AIProjectEndpoint: foundry::project.properties.endpoints['AI Foundry API']
      AIAgentId: aiAgentName
      AIAgentVersion: aiAgentVersion
      Translator__Endpoint: 'https://${translator.properties.customSubDomainName}.cognitiveservices.azure.com/'
      Translator__Region: translator.location
      Speech__Endpoint: 'https://${speech.properties.customSubDomainName}.cognitiveservices.azure.com/'
      Speech__Region: speech.location
      Speech__ResourceId: speech.id
      WebPubSub__Endpoint: 'https://${webPubSub.properties.hostName}'
      WebPubSub__Hub: 'meetings'
      WebPubSub__ClientTokenLifetimeMinutes: '60'
      KeyVault__Uri: existingKeyVaultUri
      KeyVault__JoinTokenSigningKeySecretName: 'join-token-signing-key'
      JoinToken__Audience: 'meeting-join'
      JoinToken__LifetimeHours: '24'
      TableStorage__Endpoint: tableStorageEndpoint
      TableStorage__UsersTableName: usersTableName
      TableStorage__UserInvitesTableName: userInvitesTableName
      XDT_MicrosoftApplicationInsights_Mode: 'Recommended'
    }, authProviderSettings)
  }

  @description('Enable App Service Easy Auth v2 provider endpoints. The application keeps its login page public and authorizes protected routes itself.')
  resource authV2 'config' = if (enableEasyAuth) {
    name: 'authsettingsV2'
    properties: {
      platform: {
        enabled: true
        runtimeVersion: '~1'
      }
      globalValidation: {
        requireAuthentication: false
        unauthenticatedClientAction: 'AllowAnonymous'
      }
      identityProviders: union(empty(microsoftClientId) ? {} : {
        azureActiveDirectory: {
          enabled: true
          registration: {
            openIdIssuer: '${environment().authentication.loginEndpoint}common/v2.0'
            clientId: microsoftClientId
            clientSecretSettingName: 'MICROSOFT_PROVIDER_AUTHENTICATION_SECRET'
          }
          validation: {
            allowedAudiences: [
              empty(microsoftAllowedAudience) ? 'api://${microsoftClientId}' : microsoftAllowedAudience
            ]
          }
          login: {
            disableWWWAuthenticate: false
          }
        }
      }, empty(googleClientId) ? {} : {
        google: {
          enabled: true
          registration: {
            clientId: googleClientId
            clientSecretSettingName: 'GOOGLE_PROVIDER_AUTHENTICATION_SECRET'
          }
          login: {
            scopes: [
              'openid'
              'profile'
              'email'
            ]
          }
        }
      })
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

output appHostname string = webApp.properties.defaultHostName

@description('The principal ID of the App Service user-assigned managed identity. Used by other modules to grant data-plane roles (e.g. Postgres Entra admin).')
output appServicePrincipalId string = appServiceManagedIdentity.properties.principalId

@description('The client ID of the App Service user-assigned managed identity. Used as the Entra principal name on Postgres.')
output appServicePrincipalClientId string = appServiceManagedIdentity.properties.clientId
