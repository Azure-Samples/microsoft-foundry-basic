targetScope = 'resourceGroup'

/*
  Deploy an Azure Container Registry used as the deployment medium for the chat web app and the hosted agent.
*/

@description('This is the base name for each Azure resource name (6-8 chars)')
@minLength(6)
@maxLength(8)
param baseName string

@description('The region in which this architecture is deployed. Should match the region of the resource group.')
@minLength(1)
param location string = resourceGroup().location

@description('The name of the existing Foundry account that hosts the agent project pulling images from this registry.')
@minLength(2)
param existingFoundryName string

@description('The name of the existing Foundry project. Used to establish deployment ordering and to reference its managed identity.')
@minLength(2)
param existingFoundryProjectName string

var acrName = 'acr${baseName}'

@description('Azure Container Registry that hosts the container images for the chat web app and the hosted agent.')
resource acr 'Microsoft.ContainerRegistry/registries@2023-07-01' = {
  name: acrName
  location: location
  sku: {
    name: 'Basic'
  }
  properties: {
    adminUserEnabled: true
    publicNetworkAccess: 'Enabled'
    zoneRedundancy: 'Disabled'
  }
}

// ---- Existing resources ----

resource foundry 'Microsoft.CognitiveServices/accounts@2026-05-15-preview' existing = {
  name: existingFoundryName

  resource project 'projects' existing = {
    name: existingFoundryProjectName
  }
}

@description('Built-in Role: [Container Registry Repository Reader](https://learn.microsoft.com/azure/role-based-access-control/built-in-roles#container-registry-repository-reader)')
resource acrReaderRole 'Microsoft.Authorization/roleDefinitions@2022-05-01-preview' existing = {
  name: 'b93aa761-3e63-49ed-ac28-beffa264f7ac'
  scope: subscription()
}

@description('Built-in Role: [AcrPull](https://learn.microsoft.com/azure/role-based-access-control/built-in-roles#acrpull)')
resource acrPullRole 'Microsoft.Authorization/roleDefinitions@2022-05-01-preview' existing = {
  name: '7f951dda-4ed3-4680-a7ca-43fe172d538d'
  scope: subscription()
}

@description('Grant the Foundry project managed identity Container Registry Repository Reader permission so the hosted agent runtime can pull its image.')
resource acrReaderRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: acr
  name: guid(acr.id, foundry::project.id, acrReaderRole.id)
  properties: {
    roleDefinitionId: acrReaderRole.id
    principalType: 'ServicePrincipal'
    principalId: foundry::project.identity.principalId
  }
}

@description('Grant the Foundry project managed identity AcrPull permission so the hosted agent runtime can pull its image.')
resource acrPullRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: acr
  name: guid(acr.id, foundry::project.id, acrPullRole.id)
  properties: {
    roleDefinitionId: acrPullRole.id
    principalType: 'ServicePrincipal'
    principalId: foundry::project.identity.principalId
  }
}

// ---- Outputs ----

@description('The name of the container registry.')
output acrName string = acr.name

@description('The login server for the container registry, used to reference images and with az acr build.')
output acrLoginServer string = acr.properties.loginServer
