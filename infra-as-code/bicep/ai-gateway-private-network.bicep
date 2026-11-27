targetScope = 'resourceGroup'

param baseName string
param location string = resourceGroup().location
param gatewayName string
param networkName string

resource gateway 'Microsoft.ApiManagement/service@2024-05-01' existing = {
  name: gatewayName
}

resource network 'Microsoft.Network/virtualNetworks@2024-05-01' existing = {
  name: networkName

  resource endpointSubnet 'subnets' existing = {
    name: 'private-endpoints'
  }
}

resource zone 'Microsoft.Network/privateDnsZones@2024-06-01' = {
  name: 'privatelink.azure-api.net'
  location: 'global'
}

resource link 'Microsoft.Network/privateDnsZones/virtualNetworkLinks@2024-06-01' = {
  parent: zone
  name: 'link-${baseName}'
  location: 'global'
  properties: {
    registrationEnabled: false
    virtualNetwork: {
      id: network.id
    }
  }
}

resource endpoint 'Microsoft.Network/privateEndpoints@2024-05-01' = {
  name: 'pe-apim-${baseName}'
  location: location
  properties: {
    subnet: {
      id: network::endpointSubnet.id
    }
    privateLinkServiceConnections: [
      {
        name: 'apim-gateway'
        properties: {
          privateLinkServiceId: gateway.id
          groupIds: ['Gateway']
        }
      }
    ]
  }
}

resource zoneGroup 'Microsoft.Network/privateEndpoints/privateDnsZoneGroups@2024-05-01' = {
  parent: endpoint
  name: 'default'
  properties: {
    privateDnsZoneConfigs: [
      {
        name: 'apim'
        properties: {
          privateDnsZoneId: zone.id
        }
      }
    ]
  }
}

output privateEndpointId string = endpoint.id