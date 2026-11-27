targetScope = 'resourceGroup'

param gatewayName string
param productName string
param environmentLabel string
param apiName string = 'foundry-project'
param departments {
  subscriptionName: string
  displayName: string
}[]

resource gateway 'Microsoft.ApiManagement/service@2024-05-01' existing = {
  name: gatewayName
}

resource product 'Microsoft.ApiManagement/service/products@2024-05-01' = {
  parent: gateway
  name: productName
  properties: {
    displayName: 'AI Translator - Department Access - ${environmentLabel}'
    description: 'Application-owned access for Finance and HR, with separate department subscriptions. HTTP project API only; realtime audio, Speech and Translator are excluded. Departmental enforcement requires application integration.'
    subscriptionRequired: true
    approvalRequired: true
    state: 'published'
    terms: 'Internal application use only. Keep subscription keys server-side. Managed identity authentication is also required. Do not use the built-in all-access subscription for application traffic.'
  }
}

resource productApi 'Microsoft.ApiManagement/service/products/apis@2024-05-01' = {
  parent: product
  name: apiName
}

resource subscriptions 'Microsoft.ApiManagement/service/subscriptions@2024-05-01' = [for department in departments: {
  parent: gateway
  name: department.subscriptionName
  properties: {
    displayName: 'AI Translator - ${department.displayName} - ${environmentLabel}'
    scope: product.id
    state: 'active'
    allowTracing: false
  }
}]