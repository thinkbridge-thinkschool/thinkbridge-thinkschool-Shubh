import { Component, computed, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { NavigationEnd, Router, RouterOutlet } from '@angular/router';
import { filter, map, startWith } from 'rxjs';
import { QuotesList } from '../quotes-list/quotes-list';

@Component({
  selector: 'app-quotes-page',
  imports: [QuotesList, RouterOutlet],
  templateUrl: './quotes-page.html',
  styleUrl: './quotes-page.css',
})
export class QuotesPage {
  private readonly router = inject(Router);

  // On desktop both columns are always on screen, so nothing here matters. On
  // mobile there is only room for one, and this is what decides which: the
  // list until a quote is opened, then the detail. Derived from the URL rather
  // than from a click handler so a deep link to /quotes/12 opens straight into
  // the detail view, and the browser back button returns to the list.
  private readonly url = toSignal(
    this.router.events.pipe(
      filter((event): event is NavigationEnd => event instanceof NavigationEnd),
      map((event) => event.urlAfterRedirects),
      startWith(this.router.url),
    ),
    { requireSync: true },
  );

  protected readonly hasSelection = computed(() => /\/quotes\/[^/?#]+/.test(this.url()));
}
