targetScope = 'resourceGroup'

@description('Location for all Day 26 observability resources')
param location string = resourceGroup().location

@description('Email address to receive the error-rate alert (optional). Leave empty to create the action group with no notification receivers.')
param alertEmail string = ''

var tags = {
  'day': '26'
  'purpose': 'observability-demo'
}

// Log Analytics workspace backing the workspace-based Application Insights resource.
// PerGB2018 is the standard pay-as-you-go SKU; 30-day retention is the minimum/default
// and keeps ingestion cost near-zero for a small student demo (well under the free daily
// ingestion allowance).
resource logAnalytics 'Microsoft.OperationalInsights/workspaces@2022-10-01' = {
  name: 'log-day26-piece1'
  location: location
  tags: tags
  properties: {
    sku: {
      name: 'PerGB2018'
    }
    retentionInDays: 30
  }
}

// Workspace-based Application Insights (the modern architecture) — data lands in the Log
// Analytics workspace above, and the Application Insights compatibility layer still exposes
// the classic requests/dependencies/exceptions table names used by the KQL in this folder's
// README.
resource appInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: 'appi-day26-piece1'
  location: location
  tags: tags
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: logAnalytics.id
    IngestionMode: 'LogAnalytics'
  }
}

var hasEmail = !empty(alertEmail)

// Action group for the error-rate alert. With no email supplied this has zero receivers —
// the alert rule still exists and evaluates, it just has nothing to notify, which avoids
// sending real external notifications for a demo threshold.
resource actionGroup 'Microsoft.Insights/actionGroups@2023-01-01' = {
  name: 'ag-day26-errorrate'
  location: 'global'
  tags: tags
  properties: {
    groupShortName: 'day26err'
    enabled: true
    emailReceivers: hasEmail ? [
      {
        name: 'demo-owner'
        emailAddress: alertEmail
        useCommonAlertSchema: true
      }
    ] : []
  }
}

// Error-rate alert: fires when the failed-request percentage over the last 15 minutes
// exceeds 5%, evaluated every 5 minutes. Scoped directly to the Application Insights
// resource, so the query below uses the classic `requests` table name.
resource errorRateAlert 'Microsoft.Insights/scheduledQueryRules@2023-03-15-preview' = {
  name: 'alert-day26-error-rate'
  location: location
  tags: tags
  properties: {
    displayName: 'Day 26 - QuotesApi error rate > 5%'
    description: 'Fires when the percentage of failed requests to QuotesApi exceeds 5% over a 15 minute window.'
    severity: 2
    enabled: true
    evaluationFrequency: 'PT5M'
    windowSize: 'PT15M'
    scopes: [
      appInsights.id
    ]
    criteria: {
      allOf: [
        {
          query: 'requests | summarize Total = count(), Failed = countif(success == false) | extend ErrorRatePercent = iff(Total == 0, 0.0, 100.0 * Failed / Total)'
          timeAggregation: 'Maximum'
          metricMeasureColumn: 'ErrorRatePercent'
          operator: 'GreaterThan'
          threshold: 5
          failingPeriods: {
            numberOfEvaluationPeriods: 1
            minFailingPeriodsToAlert: 1
          }
        }
      ]
    }
    actions: {
      actionGroups: [
        actionGroup.id
      ]
    }
  }
}

output logAnalyticsWorkspaceId string = logAnalytics.id
output appInsightsName string = appInsights.name
output appInsightsConnectionString string = appInsights.properties.ConnectionString
output actionGroupName string = actionGroup.name
output alertRuleName string = errorRateAlert.name
