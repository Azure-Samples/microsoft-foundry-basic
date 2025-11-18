targetScope = 'resourceGroup'

@description('The region in which this architecture is deployed. Should match the region of the resource group.')
@minLength(1)
param location string = resourceGroup().location

@description('This is the base name for each Azure resource name (6-8 chars)')
@minLength(6)
@maxLength(8)
param baseName string

@description('Assign your user some roles to support fluid access when working in the Foundry portal and its dependencies.')
@maxLength(36)
@minLength(36)
param yourPrincipalId string

@description('Set to true to opt-out of deployment telemetry.')
param telemetryOptOut bool = false

// Customer Usage Attribution Id
var varCuaid = '6aa4564a-a8b7-4ced-8e57-1043a41f4747'

// ---- New resources ----

@description('This is the log sink for all Azure Diagnostics in the workload.')
resource logAnalyticsWorkspace 'Microsoft.OperationalInsights/workspaces@2025-02-01' = {
  name: 'log-workload'
  location: location
  properties: {
    sku: {
      name: 'PerGB2018'
    }
    retentionInDays: 30
    forceCmkForQuery: false
    workspaceCapping: {
      dailyQuotaGb: 10 // Production readiness change: In production, tune this value to ensure operational logs are collected, but a reasonable cap is set.
    }
    publicNetworkAccessForIngestion: 'Enabled'
    publicNetworkAccessForQuery: 'Enabled'
  }
}

// Deploy the Microsoft Foundry account and Foundry Agent Service components.

@description('Deploy Microsoft Foundry with Foundry Agent Service capability. No projects yet deployed.')
module deployFoundry 'ai-foundry.bicep' = {
  scope: resourceGroup()
  name: 'foundryDeploy'
  params: {
    location: location
    baseName: baseName
    logAnalyticsWorkspaceName: logAnalyticsWorkspace.name
    foundryPortalUserPrincipalId: yourPrincipalId
  }
}

@description('Deploy the Bing account for Internet grounding data to be used by agents in the Foundry Agent Service.')
module deployBingAccount 'bing-grounding.bicep' = {
  scope: resourceGroup()
  name: 'bingAccountDeploy'
}

@description('Deploy the Foundry project into the Foundry account. This is the project is the home of the Foundry Agent Service.')
module deployFoundryProject 'ai-foundry-project.bicep' = {
  scope: resourceGroup()
  name: 'foundryProjectDeploy'
  params: {
    location: location
    existingFoundryName: deployFoundry.outputs.foundryName
    existingBingAccountName: deployBingAccount.outputs.bingAccountName
    existingWebApplicationInsightsResourceName: deployApplicationInsights.outputs.applicationInsightsName
  }
}

// Deploy the Azure Web App resources for the chat UI.

@description('Deploy Application Insights. Used by the Azure Web App to monitor the deployed application and connected to the Foundry project.')
module deployApplicationInsights 'application-insights.bicep' = {
  scope: resourceGroup()
  name: 'applicationInsightsDeploy'
  params: {
    location: location
    baseName: baseName
    logAnalyticsWorkspaceName: logAnalyticsWorkspace.name
  }
}

@description('Deploy the web app for the front end demo UI. The web application will call into the Foundry Agent Service.')
module deployWebApp 'web-app.bicep' = {
  scope: resourceGroup()
  name: 'webAppDeploy'
  params: {
    location: location
    baseName: baseName
    logAnalyticsWorkspaceName: logAnalyticsWorkspace.name
    existingWebApplicationInsightsResourceName: deployApplicationInsights.outputs.applicationInsightsName
    existingFoundryResourceName: deployFoundry.outputs.foundryName
    existingFoundryProjectName: deployFoundryProject.outputs.aiAgentProjectName
  }
}

// Optional Deployment for Customer Usage Attribution
module customerUsageAttributionModule 'customerUsageAttribution/cuaIdResourceGroup.bicep' = if (!telemetryOptOut) {
  #disable-next-line no-loc-expr-outside-params // Only to ensure telemetry data is stored in same location as deployment. See https://github.com/Azure/ALZ-Bicep/wiki/FAQ#why-are-some-linter-rules-disabled-via-the-disable-next-line-bicep-function for more information
  name: 'pid-${varCuaid}-${uniqueString(resourceGroup().location)}'
  scope: resourceGroup()
  params: {}
}
