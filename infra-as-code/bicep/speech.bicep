targetScope = 'resourceGroup'

/*
  Azure AI Speech (Cognitive Services) account.
  Used for streaming Speech Translation in meetings (STT + MT multi-target)
  and for Neural TTS playback on the client. Access via managed identity;
  local (key) auth is disabled.
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

var speechName = contains(baseName, '-') ? 'speech-${baseName}' : 'speech${baseName}'

// ---- Existing resources ----

resource logAnalyticsWorkspace 'Microsoft.OperationalInsights/workspaces@2025-02-01' existing = {
  name: logAnalyticsWorkspaceName
}

// ---- New resources ----

@description('Azure AI Speech account. Custom subdomain enables Entra ID auth with the Speech SDK.')
resource speech 'Microsoft.CognitiveServices/accounts@2025-06-01' = {
  name: speechName
  location: location
  kind: 'SpeechServices'
  sku: {
    name: 'S0'
  }
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    customSubDomainName: speechName
    disableLocalAuth: true
    publicNetworkAccess: 'Disabled'
  }
}

@description('Diagnostic settings for the Speech account.')
resource azureDiagnostics 'Microsoft.Insights/diagnosticSettings@2021-05-01-preview' = {
  name: 'default'
  scope: speech
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

@description('The name of the Azure AI Speech account.')
output speechName string = speech.name

@description('The Speech endpoint (region-based, Entra ID enabled).')
output speechEndpoint string = speech.properties.endpoint

@description('The Speech region. Required by the Speech SDK together with a Bearer token from Entra ID.')
output speechRegion string = speech.location

@description('Fully qualified resource ID of the Speech account. Used to scope Entra tokens (aud=https://cognitiveservices.azure.com).')
output speechResourceId string = speech.id
