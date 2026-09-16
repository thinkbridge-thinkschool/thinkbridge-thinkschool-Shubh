@description('The location used for all deployed resources')
param location string = resourceGroup().location

@description('Tags that will be applied to all resources')
param tags object = {}


param quotesApiExists bool

@description('Id of the user or app to assign application roles')
param principalId string

@description('Principal type of user or app')
param principalType string

@description('The signing key for the SelfJwt authentication scheme (Program.cs Jwt:Key). Supplied via azd env, never committed to source.')
@secure()
param jwtKey string

@description('Name of the azd environment (e.g. day27-dev, day27-prod). Used to build a unique, environment-specific Container App name so Dev and Prod never collide with each other or with earlier days\' deployments that share the same Container Apps Environment.')
param environmentName string

var abbrs = loadJsonContent('./abbreviations.json')
var resourceToken = uniqueString(subscription().id, resourceGroup().id, location)

// Day 27 Container App name is derived from the azd environment name (day27-dev /
// day27-prod) instead of a fixed literal, so Dev and Prod each get their own name
// and neither collides with 'quotes-api' or 'quotes-api-day13-piece1', which already
// exist in the shared Container Apps Environment referenced below.
var containerAppName = toLower('quotes-api-${environmentName}')

// Reuse the existing shared Application Insights instance instead of provisioning a
// new Application Insights + Log Analytics workspace per azd environment. This is a
// student subscription with limited credits, and there is no data-isolation
// requirement between Day 27 Dev and Prod for this assignment, so sharing one App
// Insights resource for both is the safe, minimal-cost choice. The existing
// Container Apps Environment already has its own Log Analytics workspace wired up
// from when it was first created, so no new workspace is needed for platform logs.
resource appInsights 'Microsoft.Insights/components@2020-02-02' existing = {
  name: 'appi-yayuogblvizdw'
  scope: resourceGroup('rg-quotes-api')
}

// Reuse the existing shared Container Registry instead of creating a new Basic ACR
// for every azd environment.
resource containerRegistry 'Microsoft.ContainerRegistry/registries@2023-07-01' existing = {
  name: 'cryayuogblvizdw'
  scope: resourceGroup('rg-quotes-api')
}

// Container apps environment
// This subscription (Azure for Students) allows exactly one Container Apps
// Environment total, and it's already in use (rg-quotes-api / cae-yayuogblvizdw,
// from an earlier day's deployment). Rather than fail provisioning or delete
// that environment, this app is deployed into the existing shared environment
// as its own separate Container App (unique within this resource group).
resource containerAppsEnvironment 'Microsoft.App/managedEnvironments@2023-05-01' existing = {
  name: 'cae-yayuogblvizdw'
  scope: resourceGroup('rg-quotes-api')
}

// Day 29: no Azure Cache for Redis exists in this subscription, and creating a dedicated
// managed instance would be a real recurring cost for a Dev exercise. Reuses a small,
// internal-only Redis container app already created in the shared environment above
// (redis:7-alpine, TCP ingress, no public exposure) instead of provisioning new Redis
// infrastructure — read here only to resolve its internal FQDN for Redis__ConnectionString.
resource redisCacheShared 'Microsoft.App/containerApps@2023-05-01' existing = {
  name: 'redis-cache-shared'
  scope: resourceGroup('rg-quotes-api')
}

module quotesApiIdentity 'br/public:avm/res/managed-identity/user-assigned-identity:0.2.1' = {
  name: 'quotesApiidentity'
  params: {
    name: '${abbrs.managedIdentityUserAssignedIdentities}quotesApi-${resourceToken}'
    location: location
  }
}

// Grant the new identity AcrPull on the existing shared ACR — Managed Identity pull,
// no admin credentials, no new registry. The ACR lives in a different resource group
// (rg-quotes-api) than this deployment, so the role assignment must be deployed via
// a module scoped to that resource group rather than as a plain resource here.
module acrPullRoleAssignment './modules/acr-pull-role-assignment.bicep' = {
  name: 'acrPullRoleAssignment'
  scope: resourceGroup('rg-quotes-api')
  params: {
    acrName: containerRegistry.name
    principalId: quotesApiIdentity.outputs.principalId
  }
}

module quotesApiFetchLatestImage './modules/fetch-container-image.bicep' = {
  name: 'quotesApi-fetch-image'
  params: {
    exists: quotesApiExists
    name: containerAppName
  }
}

module quotesApi 'br/public:avm/res/app/container-app:0.8.0' = {
  name: 'quotesApi'
  params: {
    // Container App names must be unique within a Container Apps Environment (not
    // just within a resource group). This environment is shared with earlier days'
    // deployments, which already own 'quotes-api' and 'quotes-api-day13-piece1', so
    // this app uses its own environment-specific name (see containerAppName above).
    name: containerAppName
    ingressTargetPort: 8080
    // Day 29 Dev: scale to zero when idle — this is a Dev/verification environment, not a
    // production deployment, and Azure for Students credits are limited.
    scaleMinReplicas: 0
    scaleMaxReplicas: 10
    secrets: {
      secureList:  [
        {
          name: 'jwt-key'
          value: jwtKey
        }
      ]
    }
    containers: [
      {
        image: quotesApiFetchLatestImage.outputs.?containers[?0].?image ?? 'mcr.microsoft.com/azuredocs/containerapps-helloworld:latest'
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
            value: quotesApiIdentity.outputs.clientId
          }
          {
            name: 'PORT'
            value: '8080'
          }
          {
            name: 'Jwt__Key'
            secretRef: 'jwt-key'
          }
          {
            // Dedicated Day 29 database on the existing Day 25 Entra-ID-only SQL server —
            // never Day 25's own identitydb. Authentication is Managed Identity via
            // SqlManagedIdentityConnectionInterceptor; no password anywhere.
            name: 'Sql__Server'
            value: 'sql-day25-shubh2026.database.windows.net'
          }
          {
            name: 'Sql__Database'
            value: 'quotesapi-day29'
          }
          {
            // The shared internal Redis container app (see redisCacheShared above) — not a
            // new dedicated Redis instance. Stage 3B investigated a Container Apps platform
            // issue where this internal TCP-transport ingress's documented exposedPort
            // (6379) accepts no connections (silent timeout) while the standard port 443
            // completes a TCP handshake but then resets as soon as StackExchange.Redis sends
            // its first command — neither is currently usable for real Redis traffic. 6379
            // matches Azure's documented exposedPort/targetPort contract, so it stays here
            // as the semantically correct value pending further investigation, rather than
            // the empirically-also-broken 443. See Stage 3B's report for full diagnosis.
            name: 'Redis__ConnectionString'
            value: '${redisCacheShared.properties.configuration.ingress.fqdn}:6379'
          }
        ]
      }
    ]
    managedIdentities:{
      systemAssigned: false
      userAssignedResourceIds: [quotesApiIdentity.outputs.resourceId]
    }
    registries:[
      {
        server: containerRegistry.properties.loginServer
        identity: quotesApiIdentity.outputs.resourceId
      }
    ]
    environmentResourceId: containerAppsEnvironment.id
    location: location
    tags: union(tags, { 'azd-service-name': 'quotes-api' })
  }
}
output AZURE_CONTAINER_REGISTRY_ENDPOINT string = containerRegistry.properties.loginServer
output AZURE_RESOURCE_QUOTES_API_ID string = quotesApi.outputs.resourceId
