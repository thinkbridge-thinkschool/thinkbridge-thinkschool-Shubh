import { DatePipe } from '@angular/common';
import { Component, computed, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { Notifications } from '../../../core/services/notifications';
import { NotificationItem } from '../../../core/models/notification.models';
import { AppError } from '../../../core/models/app-error.models';
import { Icon } from '../../../shared/ui/icon/icon';

type PanelStatus = 'idle' | 'loading' | 'loaded' | 'error';

// One page is fetched — read and unread together — and the unread badge is
// counted from it. A separate unreadOnly=true request would buy a more exact
// count at the cost of doubling the requests on every open, which isn't worth
// it for a badge; the count is honestly capped instead (see unreadLabel).
const PAGE_SIZE = 20;

@Component({
  selector: 'app-notifications-panel',
  imports: [Icon, DatePipe],
  templateUrl: './notifications-panel.html',
  styleUrl: './notifications-panel.css',
})
export class NotificationsPanel {
  private readonly notificationsService = inject(Notifications);
  private readonly router = inject(Router);

  protected readonly open = signal(false);
  protected readonly status = signal<PanelStatus>('idle');
  protected readonly notifications = signal<NotificationItem[]>([]);
  protected readonly errorMessage = signal<string | null>(null);

  // Derived from the fetched page, never stored separately, so the badge can
  // never disagree with the list the user is looking at.
  protected readonly unreadCount = computed(
    () => this.notifications().filter((n) => !n.isRead).length,
  );

  // "20+" rather than "20": the count is only ever of the newest PAGE_SIZE
  // notifications, so a full page of unread ones means "at least this many".
  protected readonly unreadLabel = computed(() => {
    const count = this.unreadCount();
    return count >= PAGE_SIZE ? `${PAGE_SIZE}+` : String(count);
  });

  protected readonly bellLabel = computed(() => {
    const count = this.unreadCount();
    return count === 0 ? 'Notifications' : `Notifications, ${this.unreadLabel()} unread`;
  });

  // Same stale-response guard the quote list and detail views use: a slow
  // response for a panel the user already closed and reopened must not
  // overwrite the newer one.
  private latestRequestId = 0;

  constructor() {
    // One fetch on mount populates the unread badge without waiting for the
    // user to open the panel. No polling and no SignalR — the badge refreshes
    // whenever the panel is opened.
    this.load();
  }

  protected toggle(): void {
    const willOpen = !this.open();
    this.open.set(willOpen);
    if (willOpen) {
      this.load();
    }
  }

  protected close(): void {
    this.open.set(false);
  }

  protected load(): void {
    const requestId = ++this.latestRequestId;
    this.status.set('loading');
    this.errorMessage.set(null);

    // GET /api/v1/notifications?page=1&size=20 — the caller's own notifications,
    // newest first.
    this.notificationsService.getNotifications(1, PAGE_SIZE).subscribe({
      next: (notifications) => {
        if (requestId !== this.latestRequestId) {
          return; // stale response
        }
        this.notifications.set(notifications);
        this.status.set('loaded');
      },
      error: (err: AppError) => {
        if (requestId !== this.latestRequestId) {
          return;
        }
        this.errorMessage.set(err.message);
        this.status.set('error');
      },
    });
  }

  // Marking read is optimistic in the UI but still authoritative on the server:
  // the local flag flips immediately so the list doesn't lurch, and a failed
  // POST rolls it back rather than leaving the UI claiming something the
  // backend never recorded.
  protected activate(notification: NotificationItem): void {
    const quoteId = notification.quoteId;

    if (!notification.isRead) {
      this.setRead(notification.id, true);
      this.notificationsService.markRead(notification.id).subscribe({
        error: () => this.setRead(notification.id, false),
      });
    }

    if (quoteId !== null) {
      this.close();
      this.router.navigate(['/quotes', quoteId]);
    }
  }

  private setRead(id: string, isRead: boolean): void {
    this.notifications.update((items) =>
      items.map((item) => (item.id === id ? { ...item, isRead } : item)),
    );
  }
}
