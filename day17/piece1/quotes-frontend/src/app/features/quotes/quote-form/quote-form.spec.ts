import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { API_BASE_URL } from '../../../core/api-base-url';
import { httpErrorMappingInterceptor } from '../../../core/interceptors/http-error-mapping-interceptor';
import { QuoteForm } from './quote-form';

describe('QuoteForm (Signal Forms)', () => {
  let httpMock: HttpTestingController;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [QuoteForm],
      providers: [
        provideZonelessChangeDetection(),
        // Same interceptor pipeline as app.config.ts, so a flushed HttpErrorResponse
        // reaches the component as the same typed AppError production code sees.
        provideHttpClient(withInterceptors([httpErrorMappingInterceptor])),
        provideHttpClientTesting(),
      ],
    }).compileComponents();
    httpMock = TestBed.inject(HttpTestingController);
  });

  function create() {
    const fixture = TestBed.createComponent(QuoteForm);
    fixture.detectChanges();
    const authorInput = () => fixture.nativeElement.querySelector('#author') as HTMLInputElement;
    const textInput = () => fixture.nativeElement.querySelector('#text') as HTMLTextAreaElement;
    const form = () => fixture.nativeElement.querySelector('form') as HTMLFormElement;
    return { fixture, authorInput, textInput, form };
  }

  function typeInto(el: HTMLInputElement | HTMLTextAreaElement, value: string) {
    el.value = value;
    el.dispatchEvent(new Event('input'));
  }

  function blur(el: HTMLElement) {
    el.dispatchEvent(new Event('blur'));
  }

  it('native form submit invokes handleSubmit and prevents the default (page-navigating) submission', async () => {
    const { fixture, form } = create();
    const comp = fixture.componentInstance as any;
    const spy = vi.spyOn(comp, 'handleSubmit');

    const event = new Event('submit', { cancelable: true });
    form().dispatchEvent(event);
    await fixture.whenStable();

    expect(spy).toHaveBeenCalledTimes(1);
    expect(event.defaultPrevented).toBe(true);
  });

  it('starts pristine (not dirty) and untouched', () => {
    const { fixture } = create();
    const comp = fixture.componentInstance as any;
    expect(comp.quoteForm.author().dirty()).toBe(false);
    expect(comp.quoteForm.author().touched()).toBe(false);
    expect(comp.quoteForm().dirty()).toBe(false);
  });

  it('becomes dirty after the value changes via the bound control', async () => {
    const { fixture, authorInput } = create();
    const comp = fixture.componentInstance as any;
    typeInto(authorInput(), 'Ada Lovelace');
    await fixture.whenStable();
    expect(comp.quoteForm.author().dirty()).toBe(true);
  });

  it('becomes touched on blur', async () => {
    const { fixture, authorInput } = create();
    const comp = fixture.componentInstance as any;
    expect(comp.quoteForm.author().touched()).toBe(false);
    blur(authorInput());
    await fixture.whenStable();
    expect(comp.quoteForm.author().touched()).toBe(true);
  });

  it('required validator fires for an empty author after touch, and clears once filled', async () => {
    const { fixture, authorInput } = create();
    const comp = fixture.componentInstance as any;
    blur(authorInput());
    await fixture.whenStable();
    expect(comp.quoteForm.author().invalid()).toBe(true);
    expect(comp.fieldError(comp.quoteForm.author)).toBe('Author must be between 1 and 200 characters.');

    typeInto(authorInput(), 'Ada');
    await fixture.whenStable();
    expect(comp.quoteForm.author().invalid()).toBe(false);
  });

  it('blank-after-trim validator fires for whitespace-only text (required alone would miss this)', async () => {
    const { fixture, textInput } = create();
    const comp = fixture.componentInstance as any;
    typeInto(textInput(), '     ');
    blur(textInput());
    await fixture.whenStable();
    expect(comp.quoteForm.text().invalid()).toBe(true);
    expect(comp.fieldError(comp.quoteForm.text)).toBe('Text must be between 1 and 1000 characters.');
  });

  it('maxLength validator fires past 200 chars for author and past 1000 for text', async () => {
    const { fixture, authorInput, textInput } = create();
    const comp = fixture.componentInstance as any;

    typeInto(authorInput(), 'a'.repeat(201));
    blur(authorInput());
    typeInto(textInput(), 'b'.repeat(1001));
    blur(textInput());
    await fixture.whenStable();

    expect(comp.quoteForm.author().invalid()).toBe(true);
    expect(comp.quoteForm.text().invalid()).toBe(true);

    typeInto(authorInput(), 'a'.repeat(200));
    typeInto(textInput(), 'b'.repeat(1000));
    await fixture.whenStable();
    expect(comp.quoteForm.author().invalid()).toBe(false);
    expect(comp.quoteForm.text().invalid()).toBe(false);
  });

  it('renders the validation error message in the DOM once touched and invalid', async () => {
    const { fixture, authorInput } = create();
    blur(authorInput());
    await fixture.whenStable();
    const errorEl = fixture.nativeElement.querySelector('#author-error');
    expect(errorEl?.textContent).toContain('Author must be between 1 and 200 characters.');
    expect(authorInput().getAttribute('aria-invalid')).toBe('true');
  });

  it('submits POST /api/quotes with exactly {author, text} on a clean, valid form', async () => {
    const { fixture, authorInput, textInput } = create();
    const comp = fixture.componentInstance as any;

    typeInto(authorInput(), 'Ada Lovelace');
    typeInto(textInput(), 'The Analytical Engine weaves algebraic patterns.');
    await fixture.whenStable();

    const submitPromise = comp.handleSubmit(new Event('submit', { cancelable: true }));
    await Promise.resolve();

    const req = httpMock.expectOne(`${API_BASE_URL}/api/quotes`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({
      author: 'Ada Lovelace',
      text: 'The Analytical Engine weaves algebraic patterns.',
    });

    req.flush({ id: 1, author: 'Ada Lovelace', text: 'The Analytical Engine weaves algebraic patterns.', isDeleted: false, userId: 1 });
    await submitPromise;
    await fixture.whenStable();

    expect(comp.successMessage()).toContain('Ada Lovelace');
    expect(comp.quoteForm.author().value()).toBe('');
    expect(comp.quoteForm.author().dirty()).toBe(false);
    expect(comp.quoteForm.author().touched()).toBe(false);
  });

  it('shows the submitting state while the request is in flight, then clears it', async () => {
    const { fixture, authorInput, textInput } = create();
    const comp = fixture.componentInstance as any;

    typeInto(authorInput(), 'Ada Lovelace');
    typeInto(textInput(), 'The Analytical Engine weaves algebraic patterns.');
    await fixture.whenStable();

    expect(comp.quoteForm().submitting()).toBe(false);
    const submitPromise = comp.handleSubmit(new Event('submit', { cancelable: true }));
    await Promise.resolve();

    expect(comp.quoteForm().submitting()).toBe(true);
    fixture.detectChanges();
    const button = fixture.nativeElement.querySelector('button[type="submit"]') as HTMLButtonElement;
    expect(button.disabled).toBe(true);
    expect(button.textContent).toContain('Creating…');

    const req = httpMock.expectOne(`${API_BASE_URL}/api/quotes`);
    req.flush({ id: 1, author: 'Ada Lovelace', text: 'x', isDeleted: false, userId: 1 });
    await submitPromise;
    await fixture.whenStable();

    expect(comp.quoteForm().submitting()).toBe(false);
  });

  it('on a failed submission (400 with field errors), shows the error, keeps entered values, and does not reset', async () => {
    const { fixture, authorInput, textInput } = create();
    const comp = fixture.componentInstance as any;

    typeInto(authorInput(), 'Ada Lovelace');
    typeInto(textInput(), 'The Analytical Engine weaves algebraic patterns.');
    await fixture.whenStable();

    const submitPromise = comp.handleSubmit(new Event('submit', { cancelable: true }));
    await Promise.resolve();

    const req = httpMock.expectOne(`${API_BASE_URL}/api/quotes`);
    req.flush(
      { errors: { author: ['Author must be between 1 and 200 characters.'] } },
      { status: 400, statusText: 'Bad Request' },
    );
    await submitPromise;
    await fixture.whenStable();

    expect(comp.serverError()).toContain('could not be saved');
    expect(comp.quoteForm.author().invalid()).toBe(true);
    expect(comp.fieldError(comp.quoteForm.author)).toBe('Author must be between 1 and 200 characters.');
    // Values must survive the failed submission.
    expect(comp.quoteForm.author().value()).toBe('Ada Lovelace');
    expect(comp.quoteForm.text().value()).toBe('The Analytical Engine weaves algebraic patterns.');
    expect(authorInput().value).toBe('Ada Lovelace');
  });

  it('a generic (non-field) server failure is reported AND leaves the form/submit() state consistently invalid', async () => {
    const { fixture, authorInput, textInput } = create();
    const comp = fixture.componentInstance as any;
    const focusSpy = vi.spyOn(comp, 'focusFirstInvalidField');

    typeInto(authorInput(), 'Ada Lovelace');
    typeInto(textInput(), 'The Analytical Engine weaves algebraic patterns.');
    await fixture.whenStable();

    const submitPromise = comp.handleSubmit(new Event('submit', { cancelable: true }));
    await Promise.resolve();
    const req = httpMock.expectOne(`${API_BASE_URL}/api/quotes`);
    req.flush({ title: 'Server error' }, { status: 500, statusText: 'Internal Server Error' });

    await submitPromise;
    await fixture.whenStable();

    // The banner is shown (using the server's title, per mapServerError's fallback)...
    expect(comp.serverError()).toBe('Server error');
    // ...and now agrees with Signal Forms' own state: the root field carries the
    // error too, so submit() correctly resolved `false` and triggered the same
    // invalid-field focus handling a client-side validation failure would.
    expect(comp.quoteForm().invalid()).toBe(true);
    expect(focusSpy).toHaveBeenCalled();
    // Values must still survive a fully generic failure, same as a field-specific one.
    expect(comp.quoteForm.author().value()).toBe('Ada Lovelace');
  });
});
