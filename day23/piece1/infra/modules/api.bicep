// API infrastructure module — Azure Container Apps hosting for the Quotes API.
//
// The real Quotes API (day22/piece1) already runs as a Container App inside a shared
// Container Apps Environment (Azure for Students allows exactly one managed environment per
// subscription, and it already exists: cae-yayuogblvizdw in rg-quotes-api). This module can
// either attach to an existing environment/registry (the safe default, matching that
// constraint) or provision its own for a subscription that isn't capped that way — controlled
// entirely by parameters so the same module works in both cases.
@minLength(1)
@maxLength(32)
param containerAppName string

@description('Azure region for all resources this module owns.')
param location string

param tags object = {}

// --- Container Apps Environment -------------------------------------------------------
@description('When true, this module provisions its own Log Analytics workspace + Container Apps managed environment. When false, it attaches the Container App to an existing managed environment via existingManagedEnvironmentId.')
param createManagedEnvironment bool = false

@description('Full resource ID of an existing Microsoft.App/managedEnvironments to attach to. Required when createManagedEnvironment is false.')
param existingManagedEnvironmentId string = ''

@description('Name for the new managed environment. Required when createManagedEnvironment is true.')
param managedEnvironmentName string = ''

@description('Name for the new Log Analytics workspace backing a newly created environment.')
param logAnalyticsWorkspaceName string = ''

// --- Container registry -----------------------------------------------------------------
@description('Whether the container image is pulled through a registry that requires an identity-based pull (false = public/anonymous image, e.g. mcr.microsoft.com).')
param useContainerRegistry bool = false

@description('When useContainerRegistry is true: create a new registry (true) or attach to an existing one (false).')
param createContainerRegistry bool = false

@description('Name for a newly created registry. Required when createContainerRegistry is true.')
param containerRegistryName string = ''

@description('Name of an existing registry to pull from. Required when useContainerRegistry is true and createContainerRegistry is false.')
param existingContainerRegistryName string = ''

// --- Container app workload --------------------------------------------------------------
@description('Fully-qualified container image reference, e.g. myregistry.azurecr.io/quotes-api:latest.')
param containerImage string = 'mcr.microsoft.com/azuredocs/containerapps-helloworld:latest'

@description('CPU cores per replica, as a decimal string (Container Apps requires specific cpu/memory combinations).')
param containerCpu string = '0.5'

@description('Memory per replica, e.g. 1.0Gi.')
param containerMemory string = '1.0Gi'

param minReplicas int = 1
param maxReplicas int = 10
param targetPort int = 8080
param externalIngress bool = true

@description('Non-secret environment variables for the container, as an array of {name, value}.')
param containerEnvVars array = []

@description('Secret values exposed to the container (e.g. jwt-key). Keys become secret names; never populate this from a literal in a checked-in parameter file.')
@secure()
param containerSecrets object = {}

@description('Environment variables that reference one of containerSecrets by name, as an array of {name, secretRef}.')
param containerSecretEnvVars array = []

var secretList = [for key in items(containerSecrets): {
  name: key.key
  value: key.value
}]

var resolvedEnvVars = concat(containerEnvVars, containerSecretEnvVars)

// --- Managed identity used both for registry pulls and for downstream RBAC (e.g. Service Bus) ---
resource identity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: 'id-${containerAppName}'
  location: location
  tags: tags
}

resource logAnalytics 'Microsoft.OperationalInsights/workspaces@2022-10-01' = if (createManagedEnvironment) {
  name: logAnalyticsWorkspaceName
  location: location
  tags: tags
  properties: {
    sku: {
      name: 'PerGB2018'
    }
    retentionInDays: 30
  }
}

// Guaranteed non-empty names for anything fed into resourceId()/reference(), even on the
// branch that isn't taken: a ternary that mixes a conditional resource's .id/.properties
// with another value still forces ARM to resolve BOTH branches' resource IDs, and an empty
// name segment there produces a "malformed" resource identifier even though that branch is
// never actually used.
var effectiveManagedEnvironmentName = empty(managedEnvironmentName) ? 'unused-managed-environment' : managedEnvironmentName

resource managedEnvironment 'Microsoft.App/managedEnvironments@2023-05-01' = if (createManagedEnvironment) {
  name: effectiveManagedEnvironmentName
  location: location
  tags: tags
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: logAnalytics!.properties.customerId
        sharedKey: logAnalytics!.listKeys().primarySharedKey
      }
    }
  }
}

var resolvedEnvironmentId = createManagedEnvironment
  ? resourceId('Microsoft.App/managedEnvironments', effectiveManagedEnvironmentName)
  : existingManagedEnvironmentId

var effectiveNewRegistryName = empty(containerRegistryName) ? 'unusedregistry' : containerRegistryName
var effectiveExistingRegistryName = empty(existingContainerRegistryName) ? 'unusedregistry' : existingContainerRegistryName

resource newRegistry 'Microsoft.ContainerRegistry/registries@2023-01-01-preview' = if (useContainerRegistry && createContainerRegistry) {
  name: effectiveNewRegistryName
  location: location
  tags: tags
  sku: {
    name: 'Basic'
  }
  properties: {
    adminUserEnabled: false
  }
}

// An existing registry to attach to must live in this same resource group — attaching
// across resource groups would need its own nested module deployment, which is more
// complexity than this training exercise's registry paths call for.
resource existingRegistry 'Microsoft.ContainerRegistry/registries@2023-01-01-preview' existing = if (useContainerRegistry && !createContainerRegistry) {
  name: effectiveExistingRegistryName
}

// ACR login server hostnames are deterministic from the registry name in public cloud, so
// this is built directly rather than via .properties.loginServer — avoiding a reference()
// call against whichever registry branch isn't actually in play.
var resolvedRegistryLoginServer = useContainerRegistry
  ? (createContainerRegistry ? '${effectiveNewRegistryName}.azurecr.io' : '${effectiveExistingRegistryName}.azurecr.io')
  : ''

// AcrPull is granted only when a registry is actually in play — never touches an existing
// registry's role assignments unless useContainerRegistry is explicitly turned on.
resource acrPullOnNewRegistry 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (useContainerRegistry && createContainerRegistry) {
  name: guid(newRegistry.id, identity.id, 'AcrPull')
  scope: newRegistry
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '7f951dda-4ed3-4680-a7ca-43fe172d538d')
    principalId: identity.properties.principalId
    principalType: 'ServicePrincipal'
  }
}

resource acrPullOnExistingRegistry 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (useContainerRegistry && !createContainerRegistry) {
  name: guid(existingRegistry.id, identity.id, 'AcrPull')
  scope: existingRegistry
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '7f951dda-4ed3-4680-a7ca-43fe172d538d')
    principalId: identity.properties.principalId
    principalType: 'ServicePrincipal'
  }
}

resource containerApp 'Microsoft.App/containerApps@2023-05-01' = {
  name: containerAppName
  location: location
  tags: tags
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${identity.id}': {}
    }
  }
  properties: {
    environmentId: resolvedEnvironmentId
    configuration: {
      activeRevisionsMode: 'Single'
      ingress: {
        external: externalIngress
        targetPort: targetPort
        transport: 'Auto'
        allowInsecure: false
      }
      registries: useContainerRegistry ? [
        {
          server: resolvedRegistryLoginServer
          identity: identity.id
        }
      ] : []
      secrets: secretList
    }
    template: {
      containers: [
        {
          name: 'main'
          image: containerImage
          resources: {
            cpu: json(containerCpu)
            memory: containerMemory
          }
          env: resolvedEnvVars
        }
      ]
      scale: {
        minReplicas: minReplicas
        maxReplicas: maxReplicas
      }
    }
  }
}

output containerAppName string = containerApp.name
output containerAppResourceId string = containerApp.id
output containerAppFqdn string = externalIngress ? containerApp.properties.configuration.ingress.fqdn : ''
output managedIdentityPrincipalId string = identity.properties.principalId
output managedIdentityClientId string = identity.properties.clientId
output containerRegistryLoginServer string = resolvedRegistryLoginServer
