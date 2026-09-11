targetScope = 'resourceGroup'

param accountName string = 'bing-ai-agent'

#disable-next-line BCP081
resource bingAccount 'Microsoft.Bing/accounts@2025-05-01-preview' = {
  name: accountName
  location: 'global'
  kind: 'Bing.Grounding'
  sku: {
    name: 'G1'
  }
}

output bingAccountName string = bingAccount.name
