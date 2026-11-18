import { Injectable, inject, signal } from '@angular/core';
import { HubConnection } from '@microsoft/signalr';
import { Subject, firstValueFrom } from 'rxjs';
import { environment } from '../../../environments/environment';
import { HubTransport, buildHubConnection, startHub } from '../../core/realtime/hub-connection';
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
  private connecting?: Promise<void>;

  /** Loads the count and starts listening. Safe to call more than once. */
  async connect(): Promise<void> {
    await this.refresh();
    this.connecting ??= this.start();
    await this.connecting;
  }

  async disconnect(): Promise<void> {
    const connection = this.connection;
    this.connection = undefined;
    this.connecting = undefined;
    this.unread.set(0);
    await connection?.stop();
  }

  private async start(): Promise<void> {
    try {
      this.connection = await startHub(
        (transport) => this.createConnection(transport),
        (connection) => {
          connection.on('notificationsChanged', (count: number) => {
            this.unread.set(count);
            this.changes.next();
          });
          // Counts may have moved while disconnected.
          connection.onreconnected(() => void this.refresh());
        },
      );
    } catch (error) {
      // The bell still works; it just won't update until the next page load.
      console.warn('Live notifications unavailable.', error);
    }
  }

  async refresh(): Promise<void> {
    try {
      this.unread.set((await firstValueFrom(this.notifications.unreadCount())).count);
    } catch {
      // Keep the last known count.
    }
  }

  protected createConnection(transport: HubTransport = 'websocket'): HubConnection {
    return buildHubConnection(`${environment.apiBaseUrl}/notifications/hub`, transport);
  }
}
