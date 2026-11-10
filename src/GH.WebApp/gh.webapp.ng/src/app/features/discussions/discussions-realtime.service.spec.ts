import { HubConnection, HubConnectionState } from '@microsoft/signalr';
import { DiscussionsRealtimeService, ThreadChange } from './discussions-realtime.service';

/** Just enough of a HubConnection to drive the service. */
class FakeConnection {
  state = HubConnectionState.Disconnected;
  readonly invoked: [string, string][] = [];
  private handlers = new Map<string, (...args: string[]) => void>();
  reconnected?: () => void;

  start = async () => {
    this.state = HubConnectionState.Connected;
  };
  invoke = async (method: string, id: string) => {
    this.invoked.push([method, id]);
  };
  on = (name: string, handler: (...args: string[]) => void) => this.handlers.set(name, handler);
  onreconnecting = () => undefined;
  onreconnected = (handler: () => void) => (this.reconnected = handler);
  onclose = () => undefined;

  emit(name: string, ...args: string[]): void {
    this.handlers.get(name)?.(...args);
  }
}

class TestableRealtime extends DiscussionsRealtimeService {
  readonly fake = new FakeConnection();
  protected override createConnection(): HubConnection {
    return this.fake as unknown as HubConnection;
  }
}

const flush = () => new Promise((resolve) => setTimeout(resolve));

describe('DiscussionsRealtimeService', () => {
  it('joins a board once, delivers only its changes, and leaves after the last watcher', async () => {
    const realtime = new TestableRealtime();
    const received: ThreadChange[] = [];

    const first = realtime.watchBoard('p1').subscribe((c) => received.push(c));
    const second = realtime.watchBoard('p1').subscribe();
    await flush();

    expect(realtime.status()).toBe('live');
    expect(realtime.fake.invoked).toEqual([['JoinBoard', 'p1']]);

    realtime.fake.emit('threadChanged', 'p1', 't1');
    realtime.fake.emit('threadChanged', 'p2', 't9');
    expect(received).toEqual([{ programId: 'p1', threadId: 't1' }]);

    first.unsubscribe();
    expect(realtime.fake.invoked).toHaveLength(1);

    second.unsubscribe();
    await flush();
    expect(realtime.fake.invoked).toEqual([
      ['JoinBoard', 'p1'],
      ['LeaveBoard', 'p1'],
    ]);
  });

  it('re-joins watched groups after reconnecting', async () => {
    const realtime = new TestableRealtime();
    const watching = realtime.watchThread('t1').subscribe();
    await flush();

    realtime.fake.reconnected?.();
    await flush();

    expect(realtime.fake.invoked).toEqual([
      ['JoinThread', 't1'],
      ['JoinThread', 't1'],
    ]);
    watching.unsubscribe();
  });
});
