import { Component, ElementRef, ViewChild, effect, inject } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import { Auth } from '../../core/services/auth';

type LoginFieldName = 'email' | 'password';

@Component({
  selector: 'app-login',
  imports: [ReactiveFormsModule],
  templateUrl: './login.html',
  styleUrl: './login.css',
})
export class Login {
  protected readonly auth = inject(Auth);
  private readonly fb = inject(FormBuilder);
  private readonly router = inject(Router);

  @ViewChild('emailInput') private readonly emailInput?: ElementRef<HTMLInputElement>;
  @ViewChild('passwordInput') private readonly passwordInput?: ElementRef<HTMLInputElement>;

  constructor() {
    // Before routing existed, App.html reactively swapped <app-login> for the
    // authenticated view the instant auth.isAuthenticated() flipped true —
    // no navigation was involved. Now /login is a real route reached only by
    // URL, and authGuard only runs when a navigation happens; a successful
    // login flips the signal but fires no navigation on its own, so without
    // this effect the user would sit on /login, authenticated, going nowhere.
    effect(() => {
      if (this.auth.isAuthenticated()) {
        this.router.navigateByUrl('/quotes');
      }
    });
  }

  // POST /api/auth/login (Program.cs) only requires non-empty Email/Password —
  // it does no format validation of its own, so no stricter validator is added
  // here beyond required.
  protected readonly form = this.fb.nonNullable.group({
    email: ['', Validators.required],
    password: ['', Validators.required],
  });

  protected fieldError(name: LoginFieldName): string | null {
    const control = this.form.controls[name];
    if (!control.invalid || !(control.touched || control.dirty)) {
      return null;
    }
    return name === 'email' ? 'Email is required.' : 'Password is required.';
  }

  protected describedBy(name: LoginFieldName): string | null {
    return this.fieldError(name) ? `${name}-error` : null;
  }

  protected submit(): void {
    if (this.auth.loginPending()) {
      return;
    }

    if (this.form.invalid) {
      this.form.markAllAsTouched();
      this.focusFirstInvalidField();
      return;
    }

    const { email, password } = this.form.getRawValue();
    this.auth.login({ email, password });
  }

  private focusFirstInvalidField(): void {
    const fields: Array<[LoginFieldName, ElementRef<HTMLElement> | undefined]> = [
      ['email', this.emailInput],
      ['password', this.passwordInput],
    ];
    for (const [name, ref] of fields) {
      if (this.form.get(name)?.invalid) {
        ref?.nativeElement.focus();
        return;
      }
    }
  }
}
