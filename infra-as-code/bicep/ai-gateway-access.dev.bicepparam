using './ai-gateway-access.bicep'

param gatewayName = 'apim-aitr-dev-swe-001'
param productName = 'prd-aitr-dev-swe-001'
param environmentLabel = 'Dev'
param departments = [
  {
    subscriptionName: 'sub-aitr-finance-dev-swe-001'
    displayName: 'Finance'
  }
  {
    subscriptionName: 'sub-aitr-hr-dev-swe-001'
    displayName: 'HR'
  }
  {
    subscriptionName: 'sub-aitr-unallocated-dev-swe-001'
    displayName: 'Unallocated'
  }
]