import { Routes } from '@angular/router';
import { authGuard } from './core/guards/auth-guard';
import { Login } from './features/login/login';
import { Shell } from './layout/shell/shell';
import { QuotesPage } from './features/quotes/quotes-page/quotes-page';

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
      { path: 'quotes', component: QuotesPage },
      // Lazy-loaded: the quote detail feature must not be part of the
      // initial bundle, only fetched when the user actually navigates to
      // /quotes/:id.
      {
        path: 'quotes/:id',
        loadComponent: () =>
          import('./features/quotes/quote-detail/quote-detail').then((m) => m.QuoteDetail),
      },
    ],
  },
  { path: '**', redirectTo: 'login' },
];
