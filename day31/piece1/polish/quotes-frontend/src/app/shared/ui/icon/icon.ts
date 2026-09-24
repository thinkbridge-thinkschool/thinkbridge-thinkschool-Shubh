import { Component, input } from '@angular/core';

// The names this component knows how to draw. Adding an icon means adding a
// case to the template below and a member here — the compiler then flags any
// <app-icon name="..."> that names an icon nobody drew.
export type IconName =
  | 'quote'
  | 'search'
  | 'plus'
  | 'clock'
  | 'folder'
  | 'bell'
  | 'logout'
  | 'menu'
  | 'close'
  | 'back'
  | 'trash'
  | 'layers';

// Inline-SVG icon set. Deliberately not an icon library: the app has no icon
// dependency and a dozen 24x24 outline glyphs don't justify adding one. Every
// path is drawn on the same 24x24 grid with the same 1.7 stroke width so the
// set stays optically consistent.
//
// The <svg> is aria-hidden by default because an icon almost always sits next
// to its own label; pass [label] only for an icon that is the sole content of
// a control, and it becomes an img-role element with that accessible name.
@Component({
  selector: 'app-icon',
  template: `
    <svg
      class="icon"
      [attr.width]="size()"
      [attr.height]="size()"
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      stroke-width="1.7"
      stroke-linecap="round"
      stroke-linejoin="round"
      [attr.aria-hidden]="label() ? null : 'true'"
      [attr.role]="label() ? 'img' : null"
      [attr.aria-label]="label()"
    >
      @switch (name()) {
        @case ('quote') {
          <path d="M9.5 6.5C6.9 7.9 5.5 10.1 5.5 13v4.5h5.2V12H8.1c0-1.6.6-2.8 1.9-3.6z" fill="currentColor" stroke="none" />
          <path d="M18.3 6.5c-2.6 1.4-4 3.6-4 6.5v4.5h5.2V12h-2.6c0-1.6.6-2.8 1.9-3.6z" fill="currentColor" stroke="none" />
        }
        @case ('search') {
          <circle cx="11" cy="11" r="6.5" />
          <path d="m20 20-3.6-3.6" />
        }
        @case ('plus') {
          <path d="M12 5v14M5 12h14" />
        }
        @case ('clock') {
          <circle cx="12" cy="12" r="8.5" />
          <path d="M12 7.5V12l3 1.8" />
        }
        @case ('folder') {
          <path d="M3.5 7.2c0-1 .8-1.7 1.7-1.7h3.4l2 2.3h8.2c.9 0 1.7.8 1.7 1.7v7.3c0 1-.8 1.7-1.7 1.7H5.2c-.9 0-1.7-.7-1.7-1.7z" />
        }
        @case ('bell') {
          <path d="M18 9a6 6 0 1 0-12 0c0 5-2 6.5-2 6.5h16S18 14 18 9" />
          <path d="M13.7 19a2 2 0 0 1-3.4 0" />
        }
        @case ('logout') {
          <path d="M9.5 20H5.8A1.8 1.8 0 0 1 4 18.2V5.8C4 4.8 4.8 4 5.8 4h3.7" />
          <path d="M15 16.5 19.5 12 15 7.5M19.5 12H9.5" />
        }
        @case ('menu') {
          <path d="M4 7h16M4 12h16M4 17h16" />
        }
        @case ('close') {
          <path d="M6.5 6.5l11 11M17.5 6.5l-11 11" />
        }
        @case ('back') {
          <path d="M19 12H5M11 6l-6 6 6 6" />
        }
        @case ('trash') {
          <path d="M4.5 7h15M9.5 7V5.5c0-.6.4-1 1-1h3c.6 0 1 .4 1 1V7" />
          <path d="M6.5 7l.8 11.2c0 .7.6 1.3 1.3 1.3h6.8c.7 0 1.3-.6 1.3-1.3L17.5 7" />
        }
        @case ('layers') {
          <path d="M12 3.5 21 8l-9 4.5L3 8z" />
          <path d="m3 13 9 4.5L21 13" />
        }
      }
    </svg>
  `,
  styles: `
    :host {
      display: inline-flex;
      flex: none;
      line-height: 0;
    }
  `,
})
export class Icon {
  readonly name = input.required<IconName>();
  readonly size = input(18);
  // Only set on an icon that carries meaning on its own (an icon-only button).
  readonly label = input<string | null>(null);
}
