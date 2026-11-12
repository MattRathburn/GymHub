import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { PagedResult } from '../programs/program.model';
import { NotificationItem, NotificationPreference, PreferenceChange } from './notification.model';

@Injectable({ providedIn: 'root' })
export class NotificationsService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiBaseUrl}/notifications`;

  list(options: { unreadOnly?: boolean; pageIndex?: number; pageSize?: number } = {}): Observable<PagedResult<NotificationItem>> {
    let params = new HttpParams();

    if (options.unreadOnly) {
      params = params.set('unreadOnly', 'true');
    }
    if (options.pageIndex) {
      params = params.set('pageIndex', options.pageIndex);
    }
    if (options.pageSize) {
      params = params.set('pageSize', options.pageSize);
    }

    return this.http.get<PagedResult<NotificationItem>>(`${this.baseUrl}/`, { params });
  }

  unreadCount(): Observable<{ count: number }> {
    return this.http.get<{ count: number }>(`${this.baseUrl}/unread-count`);
  }

  markRead(id: string): Observable<void> {
    return this.http.post<void>(`${this.baseUrl}/${id}/read`, null);
  }

  markAllRead(): Observable<void> {
    return this.http.post<void>(`${this.baseUrl}/read-all`, null);
  }

  preferences(): Observable<NotificationPreference[]> {
    return this.http.get<NotificationPreference[]>(`${this.baseUrl}/preferences`);
  }

  savePreferences(preferences: PreferenceChange[]): Observable<void> {
    return this.http.put<void>(`${this.baseUrl}/preferences`, { preferences });
  }
}
