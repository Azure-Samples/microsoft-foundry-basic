targetScope = 'resourceGroup'

@description('The region in which this architecture is deployed. Should match the region of the resource group.')
@minLength(1)
param location string = resourceGroup().location

@description('The existing Microsoft Foundry account. This project will become a child resource of this account.')
@minLength(2)
param existingFoundryName string

@description('The existing Application Insights instance to log token usage in this project.')
@minLength(1)
param existingWebApplicationInsightsResourceName string

// ---- Existing resources ----

resource applicationInsights 'Microsoft.Insights/components@2020-02-02' existing = {
  name: existingWebApplicationInsightsResourceName
}

// ---- New resources ----

@description('Existing Microsoft Foundry account. The project will be created as a child resource of this account.')
resource foundry 'Microsoft.CognitiveServices/accounts@2025-06-01' existing  = {
  name: existingFoundryName

  resource project 'projects' = {
    name: 'projchat'
    location: location
    identity: {
      type: 'SystemAssigned'
    }
    properties: {
      description: 'Private reasoning services for AI Translator.'
      displayName: 'AI Translator'
    }

    @description('Connect this project to application insights for visualization of token usage.')
    resource applicationInsightsConnection 'connections' = {
      name:'appInsights-connection'
      properties: {
        authType: 'ApiKey'
        category: 'AppInsights'
        credentials: {
          key: applicationInsights.properties.ConnectionString
        }
        isSharedToAll: false
        target: applicationInsights.id
        metadata: {
          ApiType: 'Azure'
          ResourceId: applicationInsights.id
          location: applicationInsights.location
        }
      }
    }

  }
}

// ---- Outputs ----

output aiAgentProjectName string = foundry::project.name
