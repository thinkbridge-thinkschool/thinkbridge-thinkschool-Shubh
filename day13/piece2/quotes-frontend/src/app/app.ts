import { Component, inject } from '@angular/core';
import { Auth } from './core/services/auth';
import { Login } from './features/login/login';
import { QuotesPage } from './features/quotes/quotes-page/quotes-page';

@Component({
  selector: 'app-root',
  imports: [Login, QuotesPage],
  templateUrl: './app.html',
  styleUrl: './app.css',
})
export class App {
  protected readonly auth = inject(Auth);

  protected logout(): void {
    this.auth.logout();
  }
}
