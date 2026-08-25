import { AbstractControl, ValidationErrors, ValidatorFn } from '@angular/forms';

// Mirrors the backend's actual rule: Quote.Create (QuotesApi/Models/Quote.cs) trims the
// value and rejects it with string.IsNullOrWhiteSpace. Validators.required alone lets a
// whitespace-only string through (its length is > 0), so this closes that gap.
export const notBlankValidator: ValidatorFn = (control: AbstractControl): ValidationErrors | null => {
  const value = control.value;
  if (typeof value !== 'string' || value.length === 0) {
    return null;
  }
  return value.trim().length === 0 ? { blank: true } : null;
};
