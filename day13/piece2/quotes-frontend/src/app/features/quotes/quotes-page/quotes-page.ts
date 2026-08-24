import { Component, signal } from '@angular/core';
import { QuotesList } from '../quotes-list/quotes-list';
import { QuoteDetail } from '../quote-detail/quote-detail';

@Component({
  selector: 'app-quotes-page',
  imports: [QuotesList, QuoteDetail],
  templateUrl: './quotes-page.html',
  styleUrl: './quotes-page.css',
})
export class QuotesPage {
  // Owned here (not in the list or the detail component) because it is
  // shared state both siblings need: the list highlights it, the detail
  // panel fetches it.
  protected readonly selectedQuoteId = signal<number | null>(null);

  protected onSelect(id: number): void {
    this.selectedQuoteId.set(id);
  }
}
