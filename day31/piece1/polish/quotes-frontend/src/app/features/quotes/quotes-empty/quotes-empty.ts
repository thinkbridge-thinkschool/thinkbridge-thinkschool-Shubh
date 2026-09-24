import { Component } from '@angular/core';
import { Icon } from '../../../shared/ui/icon/icon';

// The third column when no quote is selected. A real routed component rather
// than an @if inside QuotesPage, so /quotes and /quotes/:id are both "a route
// filling the detail outlet" and the column never renders empty during a
// navigation.
@Component({
  selector: 'app-quotes-empty',
  imports: [Icon],
  template: `
    <section class="detail-empty">
      <app-icon name="quote" [size]="34" class="state__icon" />
      <p class="state__title">Select a quote to view its details</p>
      <p class="t-meta">Pick one from the list, or create a new quote from the navigation rail.</p>
    </section>
  `,
  styles: `
    .detail-empty {
      height: 100%;
      display: flex;
      flex-direction: column;
      align-items: center;
      justify-content: center;
      gap: var(--space-2);
      padding: var(--space-6);
      text-align: center;
      color: var(--color-text-secondary);
    }
  `,
})
export class QuotesEmpty {}
