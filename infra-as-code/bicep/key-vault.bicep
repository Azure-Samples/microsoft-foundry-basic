targetScope = 'resourceGroup'

/*
  Key Vault stores:
  - the HMAC signing key for meeting join tokens (short-lived JWTs bound to a meetingId)
  - future secrets that must not sit in appsettings.
  RBAC only (no access policies). App Service MI is granted "Key Vault Secrets User".
*/

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

@description('Object ID of the deployer human. Granted Key Vault Secrets Officer so migrations / rotations can be run interactively.')
@maxLength(36)
@minLength(36)
param deployerPrincipalId string

@description('Object ID of the App Service managed identity. Granted Key Vault Secrets User.')
@maxLength(36)
@minLength(36)
param appServicePrincipalId string

var kvName = 'kv-${baseName}'

// ---- Existing role definitions ----

@description('Built-in role: Key Vault Secrets User (read secret values only).')
resource keyVaultSecretsUserRole 'Microsoft.Authorization/roleDefinitions@2022-04-01' existing = {
  name: '4633458b-17de-4321-a41d-b30fca9145db'
  scope: subscription()
}

@description('Built-in role: Key Vault Secrets Officer (manage secrets).')
resource keyVaultSecretsOfficerRole 'Microsoft.Authorization/roleDefinitions@2022-04-01' existing = {
  name: 'b86a8fe4-44ce-4948-aee5-eccb2c155cd7'
  scope: subscription()
}

resource logAnalyticsWorkspace 'Microsoft.OperationalInsights/workspaces@2025-02-01' existing = {
  name: logAnalyticsWorkspaceName
}

// ---- New resources ----

@description('Key Vault, RBAC-only authorization, soft-delete + purge protection on.')
resource keyVault 'Microsoft.KeyVault/vaults@2024-11-01' = {
  name: kvName
  location: location
  properties: {
    tenantId: subscription().tenantId
    sku: {
      family: 'A'
      name: 'standard'
    }
    enableRbacAuthorization: true
    enableSoftDelete: true
    softDeleteRetentionInDays: 7
    enablePurgeProtection: true
    publicNetworkAccess: 'Enabled'
    networkAcls: {
      defaultAction: 'Allow'
      bypass: 'AzureServices'
      ipRules: []
      virtualNetworkRules: []
    }
  }
}

@description('Grant the deployer Secrets Officer so join-token signing keys can be seeded post-deploy.')
resource deployerSecretsOfficer 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: keyVault
  name: guid(keyVault.id, deployerPrincipalId, keyVaultSecretsOfficerRole.id)
  properties: {
    roleDefinitionId: keyVaultSecretsOfficerRole.id
    principalId: deployerPrincipalId
    principalType: 'User'
  }
}

@description('Grant the App Service MI read access to secrets (JWT signing key).')
resource appServiceSecretsUser 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: keyVault
  name: guid(keyVault.id, appServicePrincipalId, keyVaultSecretsUserRole.id)
  properties: {
    roleDefinitionId: keyVaultSecretsUserRole.id
    principalId: appServicePrincipalId
    principalType: 'ServicePrincipal'
  }
}

@description('Diagnostic settings for Key Vault.')
resource azureDiagnostics 'Microsoft.Insights/diagnosticSettings@2021-05-01-preview' = {
  name: 'default'
  scope: keyVault
  properties: {
    workspaceId: logAnalyticsWorkspace.id
    logs: [
      {
        categoryGroup: 'audit'
        enabled: true
      }
      {
        categoryGroup: 'allLogs'
        enabled: true
      }
    ]
    metrics: [
      {
        category: 'AllMetrics'
        enabled: true
      }
    ]
  }
}

// ---- Outputs ----

@description('The Key Vault resource name.')
output keyVaultName string = keyVault.name

@description('The Key Vault URI, e.g. https://kv-xxxx.vault.azure.net/. App uses this + DefaultAzureCredential to fetch secrets.')
output keyVaultUri string = keyVault.properties.vaultUri
