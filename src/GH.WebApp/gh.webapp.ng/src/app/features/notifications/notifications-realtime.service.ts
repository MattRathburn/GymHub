import { Injectable, inject, signal } from '@angular/core';
import { HubConnection, HubConnectionBuilder, LogLevel } from '@microsoft/signalr';
import { Subject, firstValueFrom } from 'rxjs';
import { environment } from '../../../environments/environment';
import { getCookie } from '../../core/auth/get-cookie';
import { xsrfCookieName, xsrfHeaderName } from '../../core/auth/secure-api.interceptor';
import { NotificationsService } from './notifications.service';

/**
 * The signed-in user's unread count, kept live by the Notifications hub. The hub only says the count changed;
 * lists refetch over REST when `changes` fires.
 */
@Injectable({ providedIn: 'root' })
export class NotificationsRealtimeService {
  private readonly notifications = inject(NotificationsService);

  readonly unread = signal(0);

  /** Fires whenever the inbox changed (new notification, or read elsewhere). */
  readonly changes = new Subject<void>();

  private connection?: HubConnection;

  /** Loads the count and starts listening. Safe to call more than once. */
  async connect(): Promise<void> {
    await this.refresh();

    if (this.connection) {
      return;
    }

    const connection = this.createConnection();
    this.connection = connection;

    connection.on('notificationsChanged', (count: number) => {
      this.unread.set(count);
      this.changes.next();
    });
    // Counts may have moved while disconnected.
    connection.onreconnected(() => void this.refresh());

    try {
      await connection.start();
    } catch (error) {
      // The bell still works; it just won't update until the next page load.
      console.warn('Live notifications unavailable.', error);
    }
  }

  async disconnect(): Promise<void> {
    const connection = this.connection;
    this.connection = undefined;
    this.unread.set(0);
    await connection?.stop();
  }

  async refresh(): Promise<void> {
    try {
      this.unread.set((await firstValueFrom(this.notifications.unreadCount())).count);
    } catch {
      // Keep the last known count.
    }
  }

  protected createConnection(): HubConnection {
    return new HubConnectionBuilder()
      .withUrl(`${environment.apiBaseUrl}/notifications/hub`, {
        withCredentials: true,
        headers: { [xsrfHeaderName]: getCookie(xsrfCookieName) },
      })
      .withAutomaticReconnect()
      .configureLogging(LogLevel.Warning)
      .build();
  }
}
