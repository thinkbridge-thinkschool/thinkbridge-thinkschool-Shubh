// Free-tier Static Web App hosting one environment's Angular build (deployed with the SWA CLI).
// Its hostname feeds the API's CORS allow-list.

@description('Name of the Static Web App')
param name string

@description('Location of the Static Web App (Free tier regions include eastasia)')
param location string

@description('Tags applied to the Static Web App')
param tags object = {}

resource staticWebApp 'Microsoft.Web/staticSites@2023-01-01' = {
  name: name
  location: location
  tags: tags
  sku: {
    name: 'Free'
    tier: 'Free'
  }
  properties: {}
}

output name string = staticWebApp.name
output defaultHostname string = staticWebApp.properties.defaultHostname
