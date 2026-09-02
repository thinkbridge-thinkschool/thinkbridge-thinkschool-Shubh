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

var abbrs = loadJsonContent('./abbreviations.json')
var resourceToken = uniqueString(subscription().id, resourceGroup().id, location)

// Monitor application with Azure Monitor
module monitoring 'br/public:avm/ptn/azd/monitoring:0.1.0' = {
  name: 'monitoring'
  params: {
    logAnalyticsName: '${abbrs.operationalInsightsWorkspaces}${resourceToken}'
    applicationInsightsName: '${abbrs.insightsComponents}${resourceToken}'
    applicationInsightsDashboardName: '${abbrs.portalDashboards}${resourceToken}'
    location: location
    tags: tags
  }
}
// Container registry
module containerRegistry 'br/public:avm/res/container-registry/registry:0.1.1' = {
  name: 'registry'
  params: {
    name: '${abbrs.containerRegistryRegistries}${resourceToken}'
    location: location
    tags: tags
    publicNetworkAccess: 'Enabled'
    roleAssignments:[
      {
        principalId: quotesApiIdentity.outputs.principalId
        principalType: 'ServicePrincipal'
        roleDefinitionIdOrName: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '7f951dda-4ed3-4680-a7ca-43fe172d538d')
      }
    ]
  }
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

module quotesApiIdentity 'br/public:avm/res/managed-identity/user-assigned-identity:0.2.1' = {
  name: 'quotesApiidentity'
  params: {
    name: '${abbrs.managedIdentityUserAssignedIdentities}quotesApi-${resourceToken}'
    location: location
  }
}
module quotesApiFetchLatestImage './modules/fetch-container-image.bicep' = {
  name: 'quotesApi-fetch-image'
  params: {
    exists: quotesApiExists
    name: 'quotes-api-day13-piece1'
  }
}

module quotesApi 'br/public:avm/res/app/container-app:0.8.0' = {
  name: 'quotesApi'
  params: {
    // Container App names must be unique within a Container Apps
    // Environment (not just within a resource group). This environment
    // is shared with an earlier day's deployment, which already owns the
    // plain 'quotes-api' name, so this app is disambiguated.
    name: 'quotes-api-day13-piece1'
    ingressTargetPort: 8080
    scaleMinReplicas: 1
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
            value: monitoring.outputs.applicationInsightsConnectionString
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
        ]
      }
    ]
    managedIdentities:{
      systemAssigned: false
      userAssignedResourceIds: [quotesApiIdentity.outputs.resourceId]
    }
    registries:[
      {
        server: containerRegistry.outputs.loginServer
        identity: quotesApiIdentity.outputs.resourceId
      }
    ]
    environmentResourceId: containerAppsEnvironment.id
    location: location
    tags: union(tags, { 'azd-service-name': 'quotes-api' })
  }
}
output AZURE_CONTAINER_REGISTRY_ENDPOINT string = containerRegistry.outputs.loginServer
output AZURE_RESOURCE_QUOTES_API_ID string = quotesApi.outputs.resourceId
