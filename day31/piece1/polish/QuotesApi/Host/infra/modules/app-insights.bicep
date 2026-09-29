// Workspace-based Application Insights for one environment, writing into the shared,
// daily-capped Log Analytics workspace (modules/shared.bicep). The "Smart Detection" action
// group and "Failure Anomalies" alert rule that Azure adds automatically are not declared here.

@description('Name of the Application Insights component')
param name string

@description('Location of the component')
param location string

@description('Tags applied to the component')
param tags object = {}

@description('Resource id of the shared Log Analytics workspace')
param logAnalyticsWorkspaceId string

resource appInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: name
  location: location
  tags: tags
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: logAnalyticsWorkspaceId
    IngestionMode: 'LogAnalytics'
  }
}

// Only the name is output; consumers read the connection string through an `existing`
// reference, so it never appears in deployment outputs.
output name string = appInsights.name
