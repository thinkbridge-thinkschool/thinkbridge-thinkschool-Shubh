import { Component, inject } from '@angular/core';
import { Router, RouterOutlet } from '@angular/router';
import { Auth } from '../../core/services/auth';

@Component({
  selector: 'app-shell',
  imports: [RouterOutlet],
  templateUrl: './shell.html',
  styleUrl: './shell.css',
})
export class Shell {
  protected readonly auth = inject(Auth);
  private readonly router = inject(Router);

  protected logout(): void {
    this.auth.logout();
    // Logging out flips auth.isAuthenticated() to false, but that alone does
    // not re-run the authGuard on the route the user is already sitting on —
    // guards only run on navigation. Without this, the shell (and whatever
    // quote route is active) would stay on screen after logout.
    this.router.navigate(['/login']);
  }
}
