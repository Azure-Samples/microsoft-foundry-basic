targetScope = 'resourceGroup'

param baseName string
param location string = resourceGroup().location
param foundryName string
param speechName string
param translatorName string
param keyVaultName string
param postgresName string
param webPubSubName string
param applicationInsightsName string
param logAnalyticsWorkspaceName string

resource network 'Microsoft.Network/virtualNetworks@2024-05-01' = {
  name: 'vnet-${baseName}'
  location: location
  properties: {
    addressSpace: {
      addressPrefixes: ['10.42.0.0/16']
    }
    subnets: [
      {
        name: 'app-service'
        properties: {
          addressPrefix: '10.42.0.0/26'
          delegations: [
            {
              name: 'app-service'
              properties: {
                serviceName: 'Microsoft.Web/serverFarms'
              }
            }
          ]
        }
      }
      {
        name: 'private-endpoints'
        properties: {
          addressPrefix: '10.42.1.0/24'
          privateEndpointNetworkPolicies: 'Disabled'
        }
      }
    ]
  }
}

var zoneNames = [
  'privatelink.cognitiveservices.azure.com'
  'privatelink.openai.azure.com'
  'privatelink.services.ai.azure.com'
  'privatelink.vaultcore.azure.net'
  'privatelink.postgres.database.azure.com'
  'privatelink.webpubsub.azure.com'
  'privatelink.monitor.azure.com'
  'privatelink.oms.opinsights.azure.com'
  'privatelink.ods.opinsights.azure.com'
  'privatelink.agentsvc.azure-automation.net'
  'privatelink.blob.core.windows.net'
]

resource zones 'Microsoft.Network/privateDnsZones@2024-06-01' = [for zoneName in zoneNames: {
  name: zoneName
  location: 'global'
}]

resource links 'Microsoft.Network/privateDnsZones/virtualNetworkLinks@2024-06-01' = [for (zoneName, index) in zoneNames: {
  parent: zones[index]
  name: 'link-${baseName}'
  location: 'global'
  properties: {
    registrationEnabled: false
    virtualNetwork: {
      id: network.id
    }
  }
}]

resource monitorScope 'Microsoft.Insights/privateLinkScopes@2021-07-01-preview' = {
  name: 'ampls-${baseName}'
  location: 'global'
  properties: {
    accessModeSettings: {
      ingestionAccessMode: 'PrivateOnly'
      queryAccessMode: 'PrivateOnly'
    }
  }
}

resource insights 'Microsoft.Insights/components@2020-02-02' existing = {
  name: applicationInsightsName
}

resource logs 'Microsoft.OperationalInsights/workspaces@2025-02-01' existing = {
  name: logAnalyticsWorkspaceName
}

resource insightsScope 'Microsoft.Insights/privateLinkScopes/scopedResources@2021-07-01-preview' = {
  parent: monitorScope
  name: 'application-insights'
  properties: {
    linkedResourceId: insights.id
  }
}

resource logsScope 'Microsoft.Insights/privateLinkScopes/scopedResources@2021-07-01-preview' = {
  parent: monitorScope
  name: 'log-analytics'
  properties: {
    linkedResourceId: logs.id
  }
  dependsOn: [insightsScope]
}

var endpoints = [
  {
    name: 'foundry'
    resourceId: resourceId('Microsoft.CognitiveServices/accounts', foundryName)
    groupId: 'account'
    zoneIndexes: [0, 1, 2]
  }
  {
    name: 'speech'
    resourceId: resourceId('Microsoft.CognitiveServices/accounts', speechName)
    groupId: 'account'
    zoneIndexes: [0]
  }
  {
    name: 'translator'
    resourceId: resourceId('Microsoft.CognitiveServices/accounts', translatorName)
    groupId: 'account'
    zoneIndexes: [0]
  }
  {
    name: 'vault'
    resourceId: resourceId('Microsoft.KeyVault/vaults', keyVaultName)
    groupId: 'vault'
    zoneIndexes: [3]
  }
  {
    name: 'postgres'
    resourceId: resourceId('Microsoft.DBforPostgreSQL/flexibleServers', postgresName)
    groupId: 'postgresqlServer'
    zoneIndexes: [4]
  }
  {
    name: 'pubsub'
    resourceId: resourceId('Microsoft.SignalRService/webPubSub', webPubSubName)
    groupId: 'webpubsub'
    zoneIndexes: [5]
  }
  {
    name: 'monitor'
    resourceId: monitorScope.id
    groupId: 'azuremonitor'
    zoneIndexes: [6, 7, 8, 9, 10]
  }
]

@batchSize(1)
resource privateEndpoints 'Microsoft.Network/privateEndpoints@2024-05-01' = [for endpoint in endpoints: {
  name: 'pe-${endpoint.name}-${baseName}'
  location: location
  properties: {
    subnet: {
      id: network.properties.subnets[1].id
    }
    privateLinkServiceConnections: [
      {
        name: endpoint.name
        properties: {
          privateLinkServiceId: endpoint.resourceId
          groupIds: [endpoint.groupId]
        }
      }
    ]
  }
  dependsOn: [logsScope]
}]

resource zoneGroups 'Microsoft.Network/privateEndpoints/privateDnsZoneGroups@2024-05-01' = [for (endpoint, index) in endpoints: {
  parent: privateEndpoints[index]
  name: 'default'
  properties: {
    privateDnsZoneConfigs: [for zoneIndex in endpoint.zoneIndexes: {
      name: replace(zoneNames[zoneIndex], '.', '-')
      properties: {
        privateDnsZoneId: zones[zoneIndex].id
      }
    }]
  }
}]

output appServiceSubnetId string = network.properties.subnets[0].id
output networkName string = network.name