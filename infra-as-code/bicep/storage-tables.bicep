targetScope = 'resourceGroup'

@description('Base workload name used to derive the globally unique storage account name.')
@minLength(6)
@maxLength(20)
param baseName string

@description('Azure region for the storage account.')
param location string = resourceGroup().location

@description('Existing Log Analytics workspace receiving Table Storage diagnostics.')
param logAnalyticsWorkspaceName string

@description('Principal ID of the App Service managed identity.')
param appServicePrincipalId string

@description('Use a private endpoint and disable public network access.')
param enablePrivateNetworking bool = true

@description('Name of the user profile table.')
@minLength(3)
param usersTableName string = 'users'

@description('Name of the user invitation table.')
@minLength(3)
param userInvitesTableName string = 'userinvites'

var storageAccountName = take(toLower(replace('st${baseName}', '-', '')), 24)

resource logWorkspace 'Microsoft.OperationalInsights/workspaces@2025-02-01' existing = {
  name: logAnalyticsWorkspaceName
}

resource storageAccount 'Microsoft.Storage/storageAccounts@2025-06-01' = {
  #disable-next-line BCP334 // The derived name starts with "st" and baseName requires at least six characters.
  name: storageAccountName
  location: location
  kind: 'StorageV2'
  sku: {
    name: 'Standard_LRS'
  }
  properties: {
    allowBlobPublicAccess: false
    allowSharedKeyAccess: false
    defaultToOAuthAuthentication: true
    minimumTlsVersion: 'TLS1_2'
    publicNetworkAccess: enablePrivateNetworking ? 'Disabled' : 'Enabled'
    supportsHttpsTrafficOnly: true
    networkAcls: {
      bypass: 'None'
      defaultAction: enablePrivateNetworking ? 'Deny' : 'Allow'
    }
    encryption: {
      keySource: 'Microsoft.Storage'
      requireInfrastructureEncryption: true
      services: {
        blob: {
          enabled: true
        }
        file: {
          enabled: true
        }
        queue: {
          enabled: true
        }
        table: {
          enabled: true
        }
      }
    }
  }
}

resource tableService 'Microsoft.Storage/storageAccounts/tableServices@2025-06-01' = {
  parent: storageAccount
  name: 'default'
}

resource usersTable 'Microsoft.Storage/storageAccounts/tableServices/tables@2025-06-01' = {
  parent: tableService
  name: usersTableName
}

resource userInvitesTable 'Microsoft.Storage/storageAccounts/tableServices/tables@2025-06-01' = {
  parent: tableService
  name: userInvitesTableName
}

@description('Built-in role: Storage Table Data Contributor.')
resource tableDataContributorRole 'Microsoft.Authorization/roleDefinitions@2022-04-01' existing = {
  scope: subscription()
  name: '0a9a7e1f-b9d0-4cc4-a60d-0319b160aaa3'
}

resource appTableDataContributor 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: storageAccount
  name: guid(storageAccount.id, appServicePrincipalId, tableDataContributorRole.id)
  properties: {
    principalId: appServicePrincipalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: tableDataContributorRole.id
  }
}

resource tableDiagnostics 'Microsoft.Insights/diagnosticSettings@2021-05-01-preview' = {
  name: 'default'
  scope: tableService
  properties: {
    workspaceId: logWorkspace.id
    logs: [
      {
        categoryGroup: 'allLogs'
        enabled: true
      }
    ]
    metrics: [
      {
        category: 'Transaction'
        enabled: true
      }
    ]
  }
}

output storageAccountName string = storageAccount.name
output tableEndpoint string = storageAccount.properties.primaryEndpoints.table
output usersTableName string = usersTable.name
output userInvitesTableName string = userInvitesTable.name
