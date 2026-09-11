targetScope = 'resourceGroup'

/*
  Azure Database for PostgreSQL Flexible Server.
  - Entra ID authentication only (password auth disabled).
  - pgvector extension allow-listed at the server level (still requires
    `CREATE EXTENSION vector;` in each database — done by the .NET migrator).
  - Firewall closed by default. Allow Azure services on for MVP so App Service
    outbound IPs can reach the server without VNet integration.
*/

@description('The region in which this architecture is deployed. Should match the region of the resource group.')
@minLength(1)
param location string = resourceGroup().location

@description('This is the base name for each Azure resource name (6-8 chars)')
@minLength(6)
@maxLength(20)
param baseName string

@description('The name of the workload\'s existing Log Analytics workspace.')
@minLength(4)
param logAnalyticsWorkspaceName string

@description('Object ID of the human deployer principal — granted Entra admin on the server so migrations can be run from a workstation.')
@maxLength(36)
@minLength(36)
param deployerPrincipalId string

@description('Object ID of the App Service user-assigned managed identity — granted Entra admin on the server for MVP. Production should demote this to a database-level role.')
@maxLength(36)
@minLength(36)
param appServicePrincipalId string

@description('Client ID of the App Service user-assigned managed identity — persisted as the login name for the Entra admin entry.')
@maxLength(36)
@minLength(36)
param appServicePrincipalClientId string

@description('Display name to persist as the login name for the deployer Entra admin entry.')
@minLength(1)
param deployerPrincipalDisplayName string

@description('Legacy deployments only. New deployments grant the application a database-scoped role after migrations.')
param enableAppServerAdmin bool = false

var pgServerName = 'pg-${baseName}'
var databaseName = 'translator'

// ---- Existing resources ----

resource logAnalyticsWorkspace 'Microsoft.OperationalInsights/workspaces@2025-02-01' existing = {
  name: logAnalyticsWorkspaceName
}

// ---- New resources ----

@description('PostgreSQL Flexible Server with Entra-only authentication and pgvector.')
resource pgServer 'Microsoft.DBforPostgreSQL/flexibleServers@2024-08-01' = {
  name: pgServerName
  location: location
  sku: {
    name: 'Standard_B1ms' // Production readiness change: pick General Purpose (D-series) for real workloads.
    tier: 'Burstable'
  }
  properties: {
    version: '16'
    createMode: 'Default'
    storage: {
      storageSizeGB: 32
      autoGrow: 'Enabled'
      tier: 'P4'
    }
    backup: {
      backupRetentionDays: 7
      geoRedundantBackup: 'Disabled'
    }
    highAvailability: {
      mode: 'Disabled'
    }
    authConfig: {
      activeDirectoryAuth: 'Enabled'
      passwordAuth: 'Disabled'
      tenantId: subscription().tenantId
    }
    network: {
      publicNetworkAccess: 'Disabled'
    }
  }
}

@description('Application database used by the translator workload.')
resource translatorDatabase 'Microsoft.DBforPostgreSQL/flexibleServers/databases@2024-08-01' = {
  parent: pgServer
  name: databaseName
  properties: {
    charset: 'UTF8'
    collation: 'en_US.utf8'
  }
}

@description('Allow-list the pgvector extension. `CREATE EXTENSION vector;` still has to be run inside the database.')
resource azureExtensionsConfig 'Microsoft.DBforPostgreSQL/flexibleServers/configurations@2024-08-01' = {
  parent: pgServer
  name: 'azure.extensions'
  properties: {
    value: 'vector'
    source: 'user-override'
  }
}

@description('Entra admin for the deploying human — needed to run migrations from a workstation.')
resource deployerAdmin 'Microsoft.DBforPostgreSQL/flexibleServers/administrators@2024-08-01' = {
  parent: pgServer
  name: deployerPrincipalId
  properties: {
    principalType: 'User'
    principalName: deployerPrincipalDisplayName
    tenantId: subscription().tenantId
  }
}

@description('Entra admin for the App Service managed identity. Production hardening: replace this admin grant with a database-level role granted via `SELECT pgaadauth_create_principal(...)` after migrations.')
resource appServiceAdmin 'Microsoft.DBforPostgreSQL/flexibleServers/administrators@2024-08-01' = if (enableAppServerAdmin) {
  parent: pgServer
  name: appServicePrincipalId
  properties: {
    principalType: 'ServicePrincipal'
    principalName: appServicePrincipalClientId
    tenantId: subscription().tenantId
  }
  dependsOn: [
    deployerAdmin
  ]
}

@description('Diagnostic settings for the Postgres server.')
resource azureDiagnostics 'Microsoft.Insights/diagnosticSettings@2021-05-01-preview' = {
  name: 'default'
  scope: pgServer
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

@description('The Postgres server FQDN. The .NET app connects with Npgsql using Password=<AAD-token> and Include Error Detail.')
output pgServerFqdn string = pgServer.properties.fullyQualifiedDomainName

@description('The Postgres server resource name.')
output pgServerName string = pgServer.name

@description('The application database name.')
output pgDatabaseName string = translatorDatabase.name
