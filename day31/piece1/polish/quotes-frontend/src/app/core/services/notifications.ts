import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { NotificationItem } from '../models/notification.models';
import { API_BASE_URL } from '../api-base-url';

@Injectable({
  providedIn: 'root',
})
export class Notifications {
  private readonly http = inject(HttpClient);

  // GET /api/v1/notifications?page=&size=&unreadOnly= (Day 31 NotificationEndpoints)
  // — requires authentication; scoped server-side to the caller's own
  // NameIdentifier claim, so there is no user id to pass. Newest first. Like
  // GET /api/v1/quotes there is no total-count field, only the page itself.
  getNotifications(page: number, size: number, unreadOnly?: boolean): Observable<NotificationItem[]> {
    const params: Record<string, string | number | boolean> = { page, size };
    if (unreadOnly !== undefined) {
      params['unreadOnly'] = unreadOnly;
    }
    return this.http.get<NotificationItem[]>(`${API_BASE_URL}/api/v1/notifications`, { params });
  }

  // POST /api/v1/notifications/{id}/read (Day 31 NotificationEndpoints) — 204 on
  // success, 404 for an id that isn't the caller's (the backend deliberately
  // does not distinguish "not yours" from "doesn't exist"). Idempotent: marking
  // an already-read notification read again keeps the original ReadAtUtc.
  markRead(id: string): Observable<void> {
    return this.http.post<void>(`${API_BASE_URL}/api/v1/notifications/${id}/read`, {});
  }
}
