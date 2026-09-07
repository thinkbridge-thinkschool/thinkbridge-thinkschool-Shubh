import { Component, ElementRef, ViewChild, effect, inject, untracked } from '@angular/core';
import {
  AbstractControl,
  FormBuilder,
  ReactiveFormsModule,
  ValidationErrors,
  Validators,
} from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { Auth } from '../../core/services/auth';

type RegisterFieldName = 'email' | 'password' | 'confirmPassword';

// Mirrors QuotesApi's own registration rule (Program.cs POST /api/auth/register):
// email must contain '@', password must be at least 8 characters.
function passwordsMatchValidator(control: AbstractControl): ValidationErrors | null {
  const password = control.get('password')?.value;
  const confirmPassword = control.get('confirmPassword')?.value;
  return password === confirmPassword ? null : { passwordMismatch: true };
}

@Component({
  selector: 'app-register',
  imports: [ReactiveFormsModule, RouterLink],
  templateUrl: './register.html',
  styleUrl: './register.css',
})
export class Register {
  protected readonly auth = inject(Auth);
  private readonly fb = inject(FormBuilder);
  private readonly router = inject(Router);

  @ViewChild('emailInput') private readonly emailInput?: ElementRef<HTMLInputElement>;
  @ViewChild('passwordInput') private readonly passwordInput?: ElementRef<HTMLInputElement>;
  @ViewChild('confirmPasswordInput') private readonly confirmPasswordInput?: ElementRef<HTMLInputElement>;

  constructor() {
    // POST /api/auth/register (Program.cs) creates the account but returns no token —
    // unlike login, a successful registration does not authenticate the user. Once
    // Auth.registerSuccess flips true, send them to /login to sign in with the new
    // account. The flag is reset immediately so navigating back here doesn't
    // immediately bounce away again.
    effect(() => {
      if (this.auth.registerSuccess()) {
        untracked(() => this.auth.registerSuccess.set(false));
        this.router.navigateByUrl('/login');
      }
    });
  }

  protected readonly form = this.fb.nonNullable.group(
    {
      email: ['', [Validators.required, Validators.email]],
      password: ['', [Validators.required, Validators.minLength(8)]],
      confirmPassword: ['', Validators.required],
    },
    { validators: passwordsMatchValidator },
  );

  protected fieldError(name: RegisterFieldName): string | null {
    const control = this.form.controls[name];
    const touched = control.touched || control.dirty;

    if (name === 'confirmPassword' && touched && control.value && this.form.hasError('passwordMismatch')) {
      return 'Passwords do not match.';
    }

    if (!control.invalid || !touched) {
      return null;
    }

    switch (name) {
      case 'email':
        return control.hasError('required') ? 'Email is required.' : 'Enter a valid email address.';
      case 'password':
        return control.hasError('required')
          ? 'Password is required.'
          : 'Password must be at least 8 characters.';
      case 'confirmPassword':
        return 'Please confirm your password.';
    }
  }

  protected describedBy(name: RegisterFieldName): string | null {
    return this.fieldError(name) ? `${name}-error` : null;
  }

  protected submit(): void {
    if (this.auth.registerPending()) {
      return;
    }

    if (this.form.invalid) {
      this.form.markAllAsTouched();
      this.focusFirstInvalidField();
      return;
    }

    const { email, password } = this.form.getRawValue();
    this.auth.register({ email, password });
  }

  private focusFirstInvalidField(): void {
    const fields: Array<[RegisterFieldName, ElementRef<HTMLElement> | undefined]> = [
      ['email', this.emailInput],
      ['password', this.passwordInput],
      ['confirmPassword', this.confirmPasswordInput],
    ];
    for (const [name, ref] of fields) {
      if (this.form.get(name)?.invalid || (name === 'confirmPassword' && this.form.hasError('passwordMismatch'))) {
        ref?.nativeElement.focus();
        return;
      }
    }
  }
}
