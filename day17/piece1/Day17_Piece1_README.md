# Day 17 --- Deploy to Azure Static Web Apps

## Overview

Day 17 deploys the Angular 21 Quotes frontend to Azure Static Web Apps
and connects it to the real Day 13 Quotes API. Deployment is automated
through GitHub Actions. The project uses JWT/Bearer authentication for
browser-to-API calls and Azure Managed Identity for Azure
resource-to-resource authentication.

> **Custom domain:** No domain was available for this exercise, so the
> Azure-provided Static Web Apps hostname is used.

## Live Application

**Frontend:**\
https://white-mushroom-0f3920100.7.azurestaticapps.net

**Production API:**\
https://quotes-api-day13-piece1.bluemoss-72267de6.eastasia.azurecontainerapps.io

### Routes

  Route            Purpose
  ---------------- ---------------------------
  `/`              Angular application entry
  `/login`         Authentication
  `/quotes`        Quotes list
  `/quotes/{id}`   Quote detail

SPA fallback is configured through `staticwebapp.config.json`.

## Real Day 13 API Contract

The frontend calls the deployed Day 13 API.

### List

``` http
GET /api/quotes?page=1&size=10
```

### Detail

``` http
GET /api/quotes/{id}
```

### Login

``` http
POST /api/auth/login
```

### Quote shape

``` json
{
  "id": 1,
  "author": "Marie Curie",
  "text": "Nothing in life is to be feared, it is only to be understood.",
  "isDeleted": false,
  "userId": 1
}
```

No mock API or invented fields were used.

## Azure Architecture

``` text
GitHub
   |
   | push: day17-piece1
   v
GitHub Actions
   |
   | Angular production build
   v
Azure Static Web Apps
   |
   | HTTPS + JWT/Bearer
   v
Azure Container Apps
   |
   v
Day 13 Quotes API
```

### Managed Identity

A User-Assigned Managed Identity is used for Azure resource-to-resource
authentication, specifically for Container App → Azure Container
Registry image pulling.

The Angular browser does **not** use Managed Identity for user
authentication. Browser-to-API authentication remains JWT/Bearer based.

## CI/CD

Workflow:

``` text
.github/workflows/day17-piece1-swa-deploy.yml
```

Branch:

``` text
day17-piece1
```

The workflow:

1.  Checks out the repository.
2.  Sets up Node.js.
3.  Installs dependencies.
4.  Builds Angular in production mode.
5.  Copies `staticwebapp.config.json` into the compiled output.
6.  Deploys the compiled artifacts to Azure Static Web Apps.

### CI/CD bug found and fixed

The first deployment failed because the SWA action was given the Angular
source directory while `skip_app_build: true` was enabled.

It searched:

``` text
day17/piece1/quotes-frontend
```

for `index.html` and failed.

The real Angular production output was:

``` text
day17/piece1/quotes-frontend/dist/quotes-frontend/browser
```

The workflow was corrected to deploy directly from that compiled
directory.

### Successful GitHub Actions run

https://github.com/thinkbridge-thinkschool/thinkbridge-thinkschool-Shubh/actions/runs/33240557045

All deployment steps succeeded.

## Verification

### API

Verified the real deployed API:

``` text
GET /api/quotes?page=1&size=10 → 200
```

### Authentication

Verified:

``` text
POST /api/auth/login → 200
POST /api/quotes without Authorization → 401
```

The protected API therefore enforces Bearer authentication.

### Populated state

During controlled Development-mode verification, a real quote was
created:

``` text
id: 1
author: Marie Curie
text: Nothing in life is to be feared, it is only to be understood.
isDeleted: false
userId: 1
```

Verified:

``` text
POST /api/quotes → 201
GET /api/quotes → 200
GET /api/quotes/1 → 200
```

The live Angular application was checked against the populated API
during the verification window.

The backend was subsequently returned to Production.

### Empty state

After the Production revert:

``` text
GET /api/quotes?page=1&size=10 → 200 []
```

This is expected because the deployed API uses intentionally ephemeral
SQLite storage.

### SPA routing

Verified live routes:

``` text
/
/login
/quotes
/quotes/1
```

## Lighthouse

Final Lighthouse results on the live SWA URL:

  Category             Score
  ---------------- ---------
  Performance        **100**
  Accessibility      **100**
  Best Practices     **100**
  SEO                **100**

Initial results were Performance 100, Accessibility 89, Best Practices
100, and SEO 82. After addressing the Lighthouse findings, the final
live run reached 100 in all four categories.

## Security

No deployment token, client secret, API key, password, or connection
string is committed to the repository.

The SWA deployment token is stored only as the GitHub Actions repository
secret:

``` text
AZURE_STATIC_WEB_APPS_API_TOKEN_QUOTES_FRONTEND
```

Production Angular configuration contains the real deployed API URL and
no localhost API URL.

## What Would Break?

### API URL

If the production API hostname changes, the Angular production
environment configuration must be updated and redeployed.

### Endpoint changes

Changing:

``` text
GET /api/quotes?page=N&size=N
GET /api/quotes/{id}
```

would require updates to the Angular HTTP service and affected
components.

### Field changes

The frontend relies on:

``` text
id
author
text
isDeleted
userId
```

Renaming `id`, changing its type, or changing the response structure
would affect typed models, routing, links, and detail loading.

### Authentication changes

Changing the API's JWT/Bearer authentication model would require
corresponding frontend authentication/interceptor changes.

### Build output changes

If the Angular production output directory changes, the GitHub Actions
SWA deployment path must also change.

### Managed Identity

Changes to the Container App identity or ACR role assignment could
prevent the container from pulling its image.

### Database persistence

The deployed API uses ephemeral SQLite storage. Data created during
verification does not survive a restart/redeployment. Persistent
production data would require persistent storage/database
infrastructure.

## GitHub

**Repository:**\
https://github.com/thinkbridge-thinkschool/thinkbridge-thinkschool-Shubh

**Branch:** `day17-piece1`

**Commits:**

``` text
9bc3491
3caf1d3
```

**Successful CI/CD run:**\
https://github.com/thinkbridge-thinkschool/thinkbridge-thinkschool-Shubh/actions/runs/33240557045

## Custom Domain

A custom domain was not configured because no domain was available.

The verified Azure-provided hostname is:

``` text
https://white-mushroom-0f3920100.7.azurestaticapps.net
```

## Final Status

  Requirement                      Status
  -------------------------------- ------------------
  Day 13 API deployed              ✅
  Day 17 Angular deployed          ✅
  Real production API              ✅
  Production configuration         ✅
  SPA routing                      ✅
  Authentication / 401             ✅
  Loading state                    ✅
  Empty state                      ✅
  Populated success verification   ✅
  Managed Identity for ACR         ✅
  No secrets committed             ✅
  Lighthouse Performance           ✅ 100
  Lighthouse Accessibility         ✅ 100
  Lighthouse Best Practices        ✅ 100
  Lighthouse SEO                   ✅ 100
  GitHub CI/CD                     ✅
  Custom domain                    ⚠️ Not available

## Key Takeaway

The main lesson from Day 17 was that deployment is not complete just
because an Azure resource or workflow exists. The live application, real
API, authentication, Lighthouse scores, and CI/CD pipeline all need to
be verified. The first CI/CD failure exposed the incorrect artifact
path, which was fixed and then verified through a successful GitHub
Actions deployment.
