targetScope = 'resourceGroup'

/*
  Azure Web PubSub — WebSocket fan-out for meeting turns.
  Backend publishes per-language payloads to per-participant groups;
  each client subscribes only to its own group so participants
  never see other participants' language streams.
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

var wpsName = 'wps-${baseName}'

// ---- Existing resources ----

resource logAnalyticsWorkspace 'Microsoft.OperationalInsights/workspaces@2025-02-01' existing = {
  name: logAnalyticsWorkspaceName
}

// ---- New resources ----

@description('Web PubSub service. Standard tier, 1 unit — enough for tens of concurrent small meetings.')
resource webPubSub 'Microsoft.SignalRService/webPubSub@2024-03-01' = {
  name: wpsName
  location: location
  sku: {
    name: 'Standard_S1'
    tier: 'Standard'
    capacity: 1
  }
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    disableLocalAuth: true
    publicNetworkAccess: 'Disabled'
    liveTraceConfiguration: {
      enabled: 'true'
      categories: [
        {
          name: 'ConnectivityLogs'
          enabled: 'true'
        }
        {
          name: 'MessagingLogs'
          enabled: 'true'
        }
      ]
    }
    networkACLs: {
      defaultAction: 'Allow'
      publicNetwork: {
        deny: ['ServerConnection', 'ClientConnection', 'RESTAPI', 'Trace']
      }
    }
  }
}

@description('Diagnostic settings for Web PubSub.')
resource azureDiagnostics 'Microsoft.Insights/diagnosticSettings@2021-05-01-preview' = {
  name: 'default'
  scope: webPubSub
  properties: {
    workspaceId: logAnalyticsWorkspace.id
    logs: [
      {
        categoryGroup: 'allLogs'
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

@description('The Web PubSub resource name.')
output webPubSubName string = webPubSub.name

@description('The Web PubSub hostname. The client access URL for a user is issued server-side using this and a short-lived Entra token.')
output webPubSubHostname string = webPubSub.properties.hostName
