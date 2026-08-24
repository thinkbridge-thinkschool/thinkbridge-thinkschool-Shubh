import { Component, inject, signal } from '@angular/core';
import { Auth } from '../../core/services/auth';

@Component({
  selector: 'app-login',
  imports: [],
  templateUrl: './login.html',
  styleUrl: './login.css',
})
export class Login {
  protected readonly auth = inject(Auth);

  protected readonly email = signal('');
  protected readonly password = signal('');

  protected submit(): void {
    this.auth.login({ email: this.email(), password: this.password() });
  }
}
