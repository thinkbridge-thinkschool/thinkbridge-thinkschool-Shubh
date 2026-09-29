// The QuotesApi Container App for one environment, in the shared Container Apps Environment:
// main API container + Redis sidecar, HTTPS-only ingress, scale limits, environment
// variables, the JWT signing key as a Key Vault reference, and managed-identity ACR pull.
//
// Zero-secret configuration: no passwords, connection-string secrets, SAS keys or registry
// credentials. Azure SQL, Service Bus, ACR and Key Vault are all reached as the user-assigned
// managed identity; the only secret entry is a Key Vault *reference* (no value).

@description('Container App name — unique within the shared Container Apps Environment')
param name string

param location string
param tags object = {}

@description('Whether the Container App already exists (azd sets this after the first deploy), so provisioning keeps the deployed image')
param exists bool

@description('Resource id of the shared Container Apps Environment')
param containerAppsEnvironmentId string

@description('Resource group of the shared registry (for the AcrPull role assignment)')
param sharedResourceGroupName string

param containerRegistryName string
param containerRegistryLoginServer string

@description('The API user-assigned managed identity')
param identityResourceId string
param identityPrincipalId string
param identityClientId string

@description('Name of this environment\'s Application Insights component (connection string is read via an existing reference)')
param appInsightsName string

param sqlServerFqdn string
param sqlDatabaseName string

param serviceBusNamespaceName string
param serviceBusTopicName string
param serviceBusSubscriptionName string

@description('Browser origin allowed by CORS (this environment\'s Static Web App)')
param corsAllowedOrigin string

@description('Versionless Key Vault URI of the JWT signing key secret. Passed from the key-vault module\'s output, so this module deploys only after that module — including its secret-scoped role assignment — has completed.')
param jwtSecretUri string

@description('Redis sidecar image in the shared registry; empty omits the sidecar (first provision only)')
param redisSidecarImage string = ''

param scaleMinReplicas int
param scaleMaxReplicas int

resource appInsights 'Microsoft.Insights/components@2020-02-02' existing = {
  name: appInsightsName
}

// AcrPull on the shared registry — Managed Identity pull, no admin credentials. The registry
// lives in the shared resource group, so the role assignment is a module scoped there.
module acrPullRoleAssignment './acr-pull-role-assignment.bicep' = {
  name: 'acrPullRoleAssignment-${name}'
  scope: resourceGroup(sharedResourceGroupName)
  params: {
    acrName: containerRegistryName
    principalId: identityPrincipalId
  }
}

module fetchLatestImage './fetch-container-image.bicep' = {
  name: 'quotesApi-fetch-image'
  params: {
    exists: exists
    name: name
  }
}

var mainContainer = {
  image: fetchLatestImage.outputs.?containers[?0].?image ?? 'mcr.microsoft.com/azuredocs/containerapps-helloworld:latest'
  name: 'main'
  resources: {
    cpu: json('0.5')
    memory: '1.0Gi'
  }
  env: [
    {
      name: 'APPLICATIONINSIGHTS_CONNECTION_STRING'
      value: appInsights.properties.ConnectionString
    }
    {
      name: 'AZURE_CLIENT_ID'
      value: identityClientId
    }
    {
      name: 'PORT'
      value: '8080'
    }
    {
      // Resolved from Key Vault by the platform (secret "jwt-key" below is a reference).
      name: 'Jwt__Key'
      secretRef: 'jwt-key'
    }
    {
      // Authentication is Managed Identity via SqlManagedIdentityConnectionInterceptor; no
      // password anywhere.
      name: 'Sql__Server'
      value: sqlServerFqdn
    }
    {
      name: 'Sql__Database'
      value: sqlDatabaseName
    }
    {
      // Redis runs as a sidecar in this same Container App, reached over localhost — no
      // ingress and no network path between apps (the Day 29 shared Redis container app's
      // internal TCP ingress never accepted connections). Until the sidecar exists (first
      // provision only), the app's RedisException handling keeps every endpoint working.
      name: 'Redis__ConnectionString'
      value: 'localhost:6379'
    }
    {
      name: 'ServiceBus__Namespace'
      value: '${serviceBusNamespaceName}.servicebus.windows.net'
    }
    {
      name: 'ServiceBus__Topic'
      value: serviceBusTopicName
    }
    {
      name: 'ServiceBus__Notifications__SubscriptionName'
      value: serviceBusSubscriptionName
    }
    {
      // Read by Program.cs in addition to its built-in local-dev origins.
      name: 'Cors__AllowedOrigins__0'
      value: corsAllowedOrigin
    }
    {
      // appsettings.json logs EF Core at Debug; in Azure that volume goes straight into
      // Log Analytics ingestion, so it is lowered for both logging pipelines here.
      name: 'Logging__LogLevel__Microsoft.EntityFrameworkCore'
      value: 'Warning'
    }
    {
      name: 'Serilog__MinimumLevel__Override__Microsoft.EntityFrameworkCore'
      value: 'Warning'
    }
    {
      // Day 32 DEV finding: ~91% of console lines were Azure SDK / MSAL Information logs
      // (managed-identity token-cache lookups on every 5s outbox poll). Raising the "Azure"
      // category to Warning keeps real identity failures visible while cutting that volume.
      name: 'Serilog__MinimumLevel__Override__Azure'
      value: 'Warning'
    }
    {
      name: 'Logging__LogLevel__Azure'
      value: 'Warning'
    }
  ]
}

var redisSidecarContainer = {
  image: redisSidecarImage
  name: 'redis'
  // Cache only: no append-only log, bounded memory, evict least-recently-used keys when full.
  args: [
    '--appendonly'
    'no'
    '--maxmemory'
    '256mb'
    '--maxmemory-policy'
    'allkeys-lru'
  ]
  resources: {
    cpu: json('0.25')
    memory: '0.5Gi'
  }
}

module containerApp 'br/public:avm/res/app/container-app:0.8.0' = {
  name: 'quotesApi'
  params: {
    name: name
    ingressTargetPort: 8080
    // Day 32 security fix: HTTPS only. The module defaults this to true, which served the
    // API over plain HTTP; with false, HTTP requests are redirected to HTTPS instead of
    // reaching the app.
    ingressAllowInsecure: false
    scaleMinReplicas: scaleMinReplicas
    scaleMaxReplicas: scaleMaxReplicas
    secrets: {
      secureList: [
        {
          // Key Vault reference only — no secret value in Container App configuration.
          name: 'jwt-key'
          keyVaultUrl: jwtSecretUri
          identity: identityResourceId
        }
      ]
    }
    // 'main' must stay first: azd deploy replaces containers[0]'s image.
    containers: empty(redisSidecarImage) ? [mainContainer] : [mainContainer, redisSidecarContainer]
    managedIdentities: {
      systemAssigned: false
      userAssignedResourceIds: [identityResourceId]
    }
    registries: [
      {
        server: containerRegistryLoginServer
        identity: identityResourceId
      }
    ]
    environmentResourceId: containerAppsEnvironmentId
    location: location
    tags: union(tags, { 'azd-service-name': 'quotes-api' })
  }
  dependsOn: [
    acrPullRoleAssignment
  ]
}

output resourceId string = containerApp.outputs.resourceId
output name string = containerApp.outputs.name
output fqdn string = containerApp.outputs.fqdn
