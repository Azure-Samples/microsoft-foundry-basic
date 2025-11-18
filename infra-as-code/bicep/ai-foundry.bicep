targetScope = 'resourceGroup'

@description('The region in which this architecture is deployed. Should match the region of the resource group.')
@minLength(1)
param location string = resourceGroup().location

@description('This is the base name for each Azure resource name (6-8 chars)')
@minLength(6)
@maxLength(8)
param baseName string

@description('The name of the workload\'s existing Log Analytics workspace.')
@minLength(4)
param logAnalyticsWorkspaceName string

@description('Your principal ID. Allows you to access the Foundry portal for post-deployment verification of functionality.')
@maxLength(36)
@minLength(36)
param foundryPortalUserPrincipalId string

var foundryName = 'aif${baseName}'

// ---- Existing resources ----

@description('Existing: Built-in Cognitive Services User role.')
resource cognitiveServicesUserRole 'Microsoft.Authorization/roleDefinitions@2022-04-01' existing = {
  name: 'a97b65f3-24c7-4388-baec-2e87135dc908'
  scope: subscription()
}

@description('Existing: Log sink for Azure Diagnostics.')
resource logAnalyticsWorkspace 'Microsoft.OperationalInsights/workspaces@2025-02-01' existing = {
  name: logAnalyticsWorkspaceName
}

// ---- New resources ----

@description('Deploy foundry (account) with Foundry Agent Service capability.')
resource foundry 'Microsoft.CognitiveServices/accounts@2025-06-01' = {
  name: foundryName
  location: location
  kind: 'AIServices'
  sku: {
    name: 'S0'
  }
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    customSubDomainName: foundryName
    allowProjectManagement: true // Microsoft Foundry account + projects
    disableLocalAuth: true
    networkAcls: {
      bypass: 'AzureServices'
      ipRules: []
      defaultAction: 'Allow'
      virtualNetworkRules: []
    }
    publicNetworkAccess: 'Enabled'
  }

  @description('Models are managed at the account level. Deploy the GPT model that will be used for the Foundry agent logic.')
  resource model 'deployments' = {
    name: 'agent-model'
    sku: {
      capacity: 50
      name: 'GlobalStandard'
    }
    properties: {
      model: {
        format: 'OpenAI'
        name: 'gpt-4o'
        version: '2024-11-20'  // Use a model version available in your region.
      }
      versionUpgradeOption: 'NoAutoUpgrade' // Production deployments should not auto-upgrade models.  Testing compatibility is important.
    }
  }
}

// Role assignments

@description('Assign yourself to have access to the Foundry portal.')
resource cognitiveServicesUser 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(foundry.id, cognitiveServicesUserRole.id, foundryPortalUserPrincipalId)
  scope: foundry
  properties: {
    roleDefinitionId: cognitiveServicesUserRole.id
    principalId: foundryPortalUserPrincipalId
    principalType: 'User'
  }
}

// Azure diagnostics

@description('Enable logging on the Foundry account.')
resource azureDiagnostics 'Microsoft.Insights/diagnosticSettings@2021-05-01-preview' = {
  name: 'default'
  scope: foundry
  properties: {
    workspaceId: logAnalyticsWorkspace.id
    logs: [
      {
        category: 'Audit'
        enabled: true
        retentionPolicy: {
          enabled: false
          days: 0
        }
      }
      {
        category: 'RequestResponse'
        enabled: true
        retentionPolicy: {
          enabled: false
          days: 0
        }
      }
      {
        category: 'AzureOpenAIRequestUsage'
        enabled: true
        retentionPolicy: {
          enabled: false
          days: 0
        }
      }
      {
        category: 'Trace'
        enabled: true
        retentionPolicy: {
          enabled: false
          days: 0
        }
      }
    ]
  }
}

// ---- Outputs ----

@description('The name of the Microsoft Foundry account.')
output foundryName string = foundry.name
