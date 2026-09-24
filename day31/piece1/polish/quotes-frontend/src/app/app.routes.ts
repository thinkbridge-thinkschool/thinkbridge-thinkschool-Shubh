import { Routes } from '@angular/router';
import { authGuard } from './core/guards/auth-guard';
import { Login } from './features/login/login';
import { Shell } from './layout/shell/shell';
import { QuotesPage } from './features/quotes/quotes-page/quotes-page';
import { QuotesEmpty } from './features/quotes/quotes-empty/quotes-empty';

export const routes: Routes = [
  { path: 'login', component: Login },
  {
    path: 'register',
    // Lazy-loaded: registration is a one-time detour off the login path, not
    // something every visitor needs in the initial bundle.
    loadComponent: () => import('./features/register/register').then((m) => m.Register),
  },
  {
    path: '',
    component: Shell,
    canActivate: [authGuard],
    children: [
      { path: '', pathMatch: 'full', redirectTo: 'quotes' },
      // QuotesPage owns columns 2 and 3 of the three-column workspace: it
      // renders the list itself and hosts a child outlet for the detail panel.
      // quotes/:id is a child route rather than a sibling purely so the list
      // stays mounted while the detail changes — the public URLs /quotes and
      // /quotes/:id are exactly what they were before.
      {
        path: 'quotes',
        component: QuotesPage,
        children: [
          // The "no quote selected" third column. Eager because it is the
          // default view of /quotes, so lazy-loading it would only add a
          // round trip before the workspace looks complete.
          { path: '', pathMatch: 'full', component: QuotesEmpty },
          // Lazy-loaded: the quote detail feature must not be part of the
          // initial bundle, only fetched when the user actually navigates to
          // /quotes/:id.
          {
            path: ':id',
            loadComponent: () =>
              import('./features/quotes/quote-detail/quote-detail').then((m) => m.QuoteDetail),
          },
        ],
      },
      // Lazy-loaded, same reasoning as quotes/:id above: My Collections is a
      // secondary view, not part of the initial bundle.
      {
        path: 'collections',
        loadComponent: () =>
          import('./features/collections/collections-page/collections-page').then(
            (m) => m.CollectionsPage,
          ),
      },
    ],
  },
  { path: '**', redirectTo: 'login' },
];
