targetScope = 'resourceGroup'

@description('Location for the Day 26 container app and its identity')
param location string = resourceGroup().location

@description('Existing Application Insights connection string (from appi-day26-piece1). Not a secret in the classic sense (an instrumentation key, not a credential with write access to anything but this one telemetry resource), but still passed as a secret app setting rather than a plain one.')
@secure()
param appInsightsConnectionString string

@description('Freshly generated JWT signing key for this deployment only, independent of any other days key.')
@secure()
param jwtKey string

@description('Resource ID of the existing, shared Container Apps Environment (cae-yayuogblvizdw in rg-quotes-api). Referenced only — never modified.')
param existingContainerAppsEnvironmentId string

@description('Name of the existing, shared Container Registry (cryayuogblvizdw in rg-quotes-api) that already holds the pushed quotes-api-day26-observability image.')
param existingRegistryName string

@description('Login server of that same registry, e.g. cryayuogblvizdw.azurecr.io')
param existingRegistryLoginServer string

@description('Resource group name that owns the existing Container Registry, for the cross-resource-group AcrPull role assignment.')
param existingRegistryResourceGroup string

@description('Image tag to deploy, e.g. quotes-api-day26-observability:v1')
param imageTag string

var tags = {
  day: '26'
  purpose: 'observability-demo'
}

resource identity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: 'id-quotesapi-day26'
  location: location
  tags: tags
}

// AcrPull on the existing, shared registry — additive only; does not touch any existing
// role assignment, image, or repository already there (e.g. Day 13's own images).
module acrPullRoleAssignment 'day26-acrpull.bicep' = {
  name: 'day26-acrpull-roleassignment'
  scope: resourceGroup(existingRegistryResourceGroup)
  params: {
    registryName: existingRegistryName
    principalId: identity.properties.principalId
  }
}

resource containerApp 'Microsoft.App/containerApps@2023-05-01' = {
  name: 'quotes-api-day26-observability'
  location: location
  tags: tags
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${identity.id}': {}
    }
  }
  properties: {
    environmentId: existingContainerAppsEnvironmentId
    configuration: {
      ingress: {
        external: true
        targetPort: 8080
      }
      registries: [
        {
          server: existingRegistryLoginServer
          identity: identity.id
        }
      ]
      secrets: [
        {
          name: 'jwt-key'
          value: jwtKey
        }
        {
          name: 'appinsights-connection-string'
          value: appInsightsConnectionString
        }
      ]
    }
    template: {
      scale: {
        minReplicas: 0
        maxReplicas: 2
      }
      containers: [
        {
          image: '${existingRegistryLoginServer}/${imageTag}'
          name: 'main'
          resources: {
            cpu: json('0.5')
            memory: '1.0Gi'
          }
          env: [
            {
              name: 'APPLICATIONINSIGHTS_CONNECTION_STRING'
              secretRef: 'appinsights-connection-string'
            }
            {
              name: 'AZURE_CLIENT_ID'
              value: identity.properties.clientId
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
              // Never actually dialed in this demo (the Redis-backed hot-read endpoint,
              // GET /api/quotes/{id}, is deliberately not exercised here — see README) —
              // this only exists to satisfy Program.cs's startup check that
              // Redis:ConnectionString is configured. Not a real/reachable Redis instance,
              // and not a secret.
              name: 'Redis__ConnectionString'
              value: 'unused-in-day26-demo:6379'
            }
            {
              // Slows the pre-existing outbox relay worker's polling way down for this
              // isolated deployment, since this new identity has no Service Bus role
              // assignment on the Day 19 namespace (deliberately not granted — out of
              // scope for Day 26) and quote-creation (the only thing that enqueues an
              // outbox row) is not part of this demo's traffic.
              name: 'Outbox__PollingIntervalSeconds'
              value: '3600'
            }
          ]
        }
      ]
    }
  }
}

output fqdn string = containerApp.properties.configuration.ingress.fqdn
output identityPrincipalId string = identity.properties.principalId
output identityClientId string = identity.properties.clientId
