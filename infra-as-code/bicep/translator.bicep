targetScope = 'resourceGroup'

/*
  Azure AI Translator (Cognitive Services) account.
  Used for fast text translation on the personal chat path.
  Access is via managed identity; local (key) auth is disabled.
*/

@description('The region in which this architecture is deployed. Should match the region of the resource group.')
@minLength(1)
param location string = resourceGroup().location

@description('This is the base name for each Azure resource name (6-8 chars)')
@minLength(6)
@maxLength(20)
param baseName string

@description('The name of the workload\'s existing Log Analytics workspace.')
@minLength(4)
param logAnalyticsWorkspaceName string

@description('Use private endpoints and disable public network access.')
param enablePrivateNetworking bool = true

var translatorName = contains(baseName, '-') ? 'trans-${baseName}' : 'trans${baseName}'

// ---- Existing resources ----

resource logAnalyticsWorkspace 'Microsoft.OperationalInsights/workspaces@2025-02-01' existing = {
  name: logAnalyticsWorkspaceName
}

// ---- New resources ----

@description('Azure AI Translator account. Custom subdomain enables Entra ID authentication with the Translator SDK.')
resource translator 'Microsoft.CognitiveServices/accounts@2025-06-01' = {
  name: translatorName
  location: location
  kind: 'TextTranslation'
  sku: {
    name: 'S1'
  }
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    customSubDomainName: translatorName
    disableLocalAuth: true
    publicNetworkAccess: enablePrivateNetworking ? 'Disabled' : 'Enabled'
  }
}

@description('Diagnostic settings for the Translator account.')
resource azureDiagnostics 'Microsoft.Insights/diagnosticSettings@2021-05-01-preview' = {
  name: 'default'
  scope: translator
  properties: {
    workspaceId: logAnalyticsWorkspace.id
    logs: [
      {
        category: 'Audit'
        enabled: true
      }
      {
        category: 'RequestResponse'
        enabled: true
      }
    ]
    metrics: [
      {
        category: 'AllMetrics'
        enabled: true
      }
    ]
  }
}

// ---- Outputs ----

@description('The name of the Azure AI Translator account.')
output translatorName string = translator.name

@description('The Translator endpoint (Entra ID enabled).')
output translatorEndpoint string = translator.properties.endpoint

@description('The Azure region of the Translator resource. Required by the SDK when authenticating with an Entra token.')
output translatorRegion string = translator.location
