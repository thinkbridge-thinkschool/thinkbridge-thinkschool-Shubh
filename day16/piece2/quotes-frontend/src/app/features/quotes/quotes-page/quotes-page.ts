import { Component } from '@angular/core';
import { QuotesList } from '../quotes-list/quotes-list';

@Component({
  selector: 'app-quotes-page',
  imports: [QuotesList],
  templateUrl: './quotes-page.html',
  styleUrl: './quotes-page.css',
})
export class QuotesPage {}
