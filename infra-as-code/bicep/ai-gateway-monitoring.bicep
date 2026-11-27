targetScope = 'resourceGroup'

param gatewayName string

resource gateway 'Microsoft.ApiManagement/service@2024-05-01' existing = {
  name: gatewayName
}

var messageLogging = {
  headers: []
  body: {
    bytes: 0
  }
  dataMasking: {
    queryParams: [{ mode: 'Hide', value: '*' }]
  }
}

resource diagnostics 'Microsoft.ApiManagement/service/diagnostics@2024-05-01' = {
  parent: gateway
  name: 'applicationinsights'
  properties: {
    loggerId: '${gateway.id}/loggers/application-insights'
    alwaysLog: 'allErrors'
    sampling: { samplingType: 'fixed', percentage: 100 }
    metrics: true
    logClientIp: false
    httpCorrelationProtocol: 'W3C'
    verbosity: 'information'
    frontend: { request: messageLogging, response: messageLogging }
    backend: { request: messageLogging, response: messageLogging }
  }
}

resource monitorDiagnostics 'Microsoft.ApiManagement/service/diagnostics@2024-05-01' = {
  parent: gateway
  name: 'azuremonitor'
  properties: {
    loggerId: '${gateway.id}/loggers/azuremonitor'
    alwaysLog: 'allErrors'
    sampling: { samplingType: 'fixed', percentage: 100 }
    logClientIp: false
    verbosity: 'error'
    frontend: { request: messageLogging, response: messageLogging }
    backend: { request: messageLogging, response: messageLogging }
  }
}

resource policy 'Microsoft.ApiManagement/service/policies@2024-05-01' = {
  parent: gateway
  name: 'policy'
  properties: {
    format: 'rawxml'
    value: '''
<policies>
  <inbound />
  <backend><forward-request timeout="240" buffer-response="false" /></backend>
  <outbound />
  <on-error />
</policies>
'''
  }
}