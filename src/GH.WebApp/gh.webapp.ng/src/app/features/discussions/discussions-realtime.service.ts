import { Injectable, signal } from '@angular/core';
import { HubConnection, HubConnectionBuilder, HubConnectionState, LogLevel } from '@microsoft/signalr';
import { Observable, Subject, filter } from 'rxjs';
import { environment } from '../../../environments/environment';
import { getCookie } from '../../core/auth/get-cookie';
import { xsrfCookieName, xsrfHeaderName } from '../../core/auth/secure-api.interceptor';

/** Something changed in a thread (new post, edit, moderation…). Refetch it over REST to see what. */
export interface ThreadChange {
  programId: string;
  threadId: string;
}

export type RealtimeStatus = 'offline' | 'connecting' | 'live';

type GroupKind = 'Board' | 'Thread';

/**
 * Live updates from the Discussions hub, through the Gateway like every other API call (the Gateway adds the access
 * token; the browser only sends its session cookie and the XSRF header). One shared connection; groups are joined
 * while at least one subscriber watches them and re-joined after a reconnect.
 */
@Injectable({ providedIn: 'root' })
export class DiscussionsRealtimeService {
  readonly status = signal<RealtimeStatus>('offline');

  private connection?: HubConnection;
  private started?: Promise<void>;
  private readonly watchers = new Map<string, number>();
  private readonly changes = new Subject<ThreadChange>();

  watchBoard(programId: string): Observable<ThreadChange> {
    return this.watch('Board', programId).pipe(filter((change) => change.programId === programId));
  }

  watchThread(threadId: string): Observable<ThreadChange> {
    return this.watch('Thread', threadId).pipe(filter((change) => change.threadId === threadId));
  }

  protected createConnection(): HubConnection {
    return new HubConnectionBuilder()
      .withUrl(`${environment.apiBaseUrl}/discussions/hub`, {
        withCredentials: true,
        headers: { [xsrfHeaderName]: getCookie(xsrfCookieName) },
      })
      .withAutomaticReconnect()
      .configureLogging(LogLevel.Warning)
      .build();
  }

  private watch(kind: GroupKind, id: string): Observable<ThreadChange> {
    return new Observable<ThreadChange>((subscriber) => {
      const subscription = this.changes.subscribe(subscriber);
      const key = `${kind}:${id}`;
      const count = (this.watchers.get(key) ?? 0) + 1;
      this.watchers.set(key, count);

      if (count === 1) {
        void this.invoke(`Join${kind}`, id);
      }

      return () => {
        subscription.unsubscribe();
        const remaining = (this.watchers.get(key) ?? 1) - 1;

        if (remaining > 0) {
          this.watchers.set(key, remaining);
          return;
        }

        this.watchers.delete(key);
        if (this.connection?.state === HubConnectionState.Connected) {
          void this.connection.invoke(`Leave${kind}`, id).catch(() => undefined);
        }
      };
    });
  }

  private async invoke(method: string, id: string): Promise<void> {
    try {
      await this.ensureStarted();
      await this.connection!.invoke(method, id);
    } catch (error) {
      // Live updates are a convenience; pages still load and refresh over REST.
      console.warn(`Live updates unavailable (${method}).`, error);
    }
  }

  private ensureStarted(): Promise<void> {
    if (this.started) {
      return this.started;
    }

    const connection = this.createConnection();
    this.connection = connection;

    connection.on('threadChanged', (programId: string, threadId: string) => this.changes.next({ programId, threadId }));
    connection.onreconnecting(() => this.status.set('connecting'));
    connection.onreconnected(() => {
      this.status.set('live');
      for (const key of this.watchers.keys()) {
        const [kind, id] = key.split(':');
        void connection.invoke(`Join${kind}`, id).catch(() => undefined);
      }
    });
    connection.onclose(() => {
      this.status.set('offline');
      this.started = undefined;
    });

    this.status.set('connecting');
    this.started = connection.start().then(
      () => this.status.set('live'),
      (error: unknown) => {
        this.status.set('offline');
        this.started = undefined;
        throw error;
      },
    );

    return this.started;
  }
}
