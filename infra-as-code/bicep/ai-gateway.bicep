targetScope = 'resourceGroup'

param baseName string
param location string = resourceGroup().location
@minLength(3)
param publisherEmail string
param subnetId string
param projectEndpoint string
param projectName string
param appClientId string
param applicationInsightsName string
param logAnalyticsWorkspaceName string
param requireDepartmentSubscriptions bool = false
param financeSubscriptionId string = ''

resource insights 'Microsoft.Insights/components@2020-02-02' existing = {
  name: applicationInsightsName
}

resource logs 'Microsoft.OperationalInsights/workspaces@2025-02-01' existing = {
  name: logAnalyticsWorkspaceName
}

resource gateway 'Microsoft.ApiManagement/service@2024-05-01' = {
  name: 'apim-${baseName}'
  location: location
  sku: {
    name: 'StandardV2'
    capacity: 1
  }
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    publisherName: 'AI Translator'
    publisherEmail: publisherEmail
    publicNetworkAccess: 'Enabled'
    virtualNetworkType: 'External'
    virtualNetworkConfiguration: {
      subnetResourceId: subnetId
    }
  }
}

var metricsPublisherRoleId = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '3913510d-42f4-4e42-8a64-420c390055eb')

resource metricsPublisher 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(insights.id, gateway.id, metricsPublisherRoleId)
  scope: insights
  properties: {
    principalId: gateway.identity.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: metricsPublisherRoleId
  }
}

resource logger 'Microsoft.ApiManagement/service/loggers@2024-05-01' = {
  parent: gateway
  name: 'application-insights'
  properties: {
    loggerType: 'applicationInsights'
    resourceId: insights.id
    credentials: {
      connectionString: insights.properties.ConnectionString
      identityClientId: 'SystemAssigned'
    }
  }
  dependsOn: [metricsPublisher]
}

resource api 'Microsoft.ApiManagement/service/apis@2024-05-01' = {
  parent: gateway
  name: 'foundry-project'
  properties: {
    displayName: 'AI Translator - Project API'
    description: 'Application HTTP project proxy. Requires the AI Translator managed identity. Realtime audio, Speech and Translator use separate endpoints.'
    path: 'api/projects/${projectName}'
    protocols: ['https']
    serviceUrl: projectEndpoint
    subscriptionRequired: requireDepartmentSubscriptions
    type: 'http'
  }
}

resource operations 'Microsoft.ApiManagement/service/apis/operations@2024-05-01' = [for method in ['GET', 'POST', 'DELETE', 'PATCH']: {
  parent: api
  name: toLower(method)
  properties: {
    displayName: method
    method: method
    urlTemplate: '/*'
  }
}]

resource policy 'Microsoft.ApiManagement/service/apis/policies@2024-05-01' = {
  parent: api
  name: 'policy'
  properties: {
    format: 'rawxml'
    value: replace(replace(replace(loadTextContent('ai-gateway-policy.xml'), '__TENANT_ID__', subscription().tenantId), '__APP_CLIENT_ID__', appClientId), '__FINANCE_SUBSCRIPTION_ID__', financeSubscriptionId)
  }
}

var messageLogging = {
  headers: []
  body: {
    bytes: 0
  }
}

resource diagnostics 'Microsoft.ApiManagement/service/apis/diagnostics@2024-05-01' = {
  parent: api
  name: 'applicationinsights'
  properties: {
    loggerId: logger.id
    alwaysLog: 'allErrors'
    sampling: {
      samplingType: 'fixed'
      percentage: 100
    }
    metrics: true
    logClientIp: false
    httpCorrelationProtocol: 'W3C'
    verbosity: 'information'
    frontend: {
      request: messageLogging
      response: messageLogging
    }
    backend: {
      request: messageLogging
      response: messageLogging
    }
  }
}

resource monitorDiagnostics 'Microsoft.ApiManagement/service/apis/diagnostics@2024-05-01' = {
  parent: api
  name: 'azuremonitor'
  properties: {
    loggerId: '${gateway.id}/loggers/azuremonitor'
    alwaysLog: 'allErrors'
    sampling: {
      samplingType: 'fixed'
      percentage: 100
    }
    logClientIp: false
    verbosity: 'information'
    frontend: {
      request: messageLogging
      response: messageLogging
    }
    backend: {
      request: messageLogging
      response: messageLogging
    }
  }
}

resource resourceDiagnostics 'Microsoft.Insights/diagnosticSettings@2021-05-01-preview' = {
  name: 'default'
  scope: gateway
  properties: {
    workspaceId: logs.id
    logAnalyticsDestinationType: 'Dedicated'
    logs: [
      {
        category: 'GatewayLogs'
        enabled: true
      }
    ]
  }
}

output projectEndpoint string = '${gateway.properties.gatewayUrl}/api/projects/${projectName}'
output gatewayName string = gateway.name

module monitoring 'ai-gateway-monitoring.bicep' = {
  name: 'aiGatewayMonitoringDeploy'
  params: {
    gatewayName: gateway.name
  }
  dependsOn: [logger]
}

resource workbook 'Microsoft.Insights/workbooks@2023-06-01' = {
  name: guid(gateway.id, 'usage-workbook')
  location: location
  kind: 'shared'
  properties: {
    displayName: 'AI Translator - gateway usage and cost'
    category: 'workbook'
    sourceId: insights.id
    serializedData: replace(replace(replace(string(loadJsonContent('../dashboards/ai-gateway-workbook.json')), '__INSIGHTS_ID__', insights.id), '__WORKSPACE_ID__', logs.id), '__FINANCE_SUBSCRIPTION_ID__', financeSubscriptionId)
  }
}

output workbookId string = workbook.id