targetScope = 'subscription'

@description('Azure region for the production workload.')
param location string = 'swedencentral'

@description('Production resource group name.')
param resourceGroupName string = 'rg-aitr-prod-swe-001'

@description('Principal receiving deployment-time Foundry and Key Vault access.')
param deployerPrincipalId string

@description('Display name used by optional migration infrastructure.')
param deployerPrincipalDisplayName string = 'deployer'

@description('Microsoft identity application client ID. Set after the approved app registration is created.')
param microsoftClientId string = ''

resource workloadResourceGroup 'Microsoft.Resources/resourceGroups@2025-04-01' = {
  name: resourceGroupName
  location: location
}

module workload 'main.bicep' = {
  scope: workloadResourceGroup
  params: {
    location: location
    baseName: 'aitr-prod-swe-001'
    yourPrincipalId: deployerPrincipalId
    yourPrincipalDisplayName: deployerPrincipalDisplayName
    microsoftClientId: microsoftClientId
    retainPostgres: false
    enablePrivateNetworking: false
    enableAiGateway: false
    routeAiThroughGateway: false
    foundryVoiceEnabled: true
    foundryVoiceAgentName: 'agent-aitr-prod-swe-001'
  }
}

output resourceGroupName string = workloadResourceGroup.name
output appName string = workload.outputs.appName
output appHostname string = workload.outputs.appHostname
output foundryName string = workload.outputs.foundryName
output storageAccountName string = workload.outputs.storageAccountName
output keyVaultName string = workload.outputs.keyVaultName
