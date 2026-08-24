import { Component, inject } from '@angular/core';
import { Auth } from './core/services/auth';
import { Login } from './features/login/login';
import { QuotesList } from './features/quotes/quotes-list/quotes-list';

@Component({
  selector: 'app-root',
  imports: [Login, QuotesList],
  templateUrl: './app.html',
  styleUrl: './app.css',
})
export class App {
  protected readonly auth = inject(Auth);

  protected logout(): void {
    this.auth.logout();
  }
}
