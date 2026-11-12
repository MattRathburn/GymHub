import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { HubConnection } from '@microsoft/signalr';
import { NotificationPreference, groupPreferences, preferenceChanges, timeAgo } from './notification.model';
import { NotificationsRealtimeService } from './notifications-realtime.service';
import { NotificationsService } from './notifications.service';

describe('NotificationsService', () => {
  let service: NotificationsService;
  let backend: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    service = TestBed.inject(NotificationsService);
    backend = TestBed.inject(HttpTestingController);
  });

  afterEach(() => backend.verify());

  it('lists unread notifications only when asked', () => {
    service.list({ unreadOnly: true, pageSize: 10 }).subscribe();
    service.list().subscribe();

    const [unread, all] = backend.match((r) => r.url === '/gh-api/api/v1/notifications/');
    expect(unread.request.params.get('unreadOnly')).toBe('true');
    expect(unread.request.params.get('pageSize')).toBe('10');
    expect(all.request.params.has('unreadOnly')).toBe(false);
  });

  it('saves preference changes as a list', () => {
    service.savePreferences([{ type: 'PostReplied', inApp: false, email: true }]).subscribe();

    const request = backend.expectOne('/gh-api/api/v1/notifications/preferences').request;
    expect(request.method).toBe('PUT');
    expect(request.body).toEqual({ preferences: [{ type: 'PostReplied', inApp: false, email: true }] });
  });
});

describe('preferences', () => {
  const replies: NotificationPreference = {
    type: 'PostReplied',
    group: 'Discussions',
    label: 'Replies',
    inApp: true,
    email: false,
    emailLocked: false,
  };
  const refunds: NotificationPreference = {
    type: 'AccessEnded',
    group: 'Billing',
    label: 'Access ended',
    inApp: true,
    email: true,
    emailLocked: true,
  };

  it('saves only what changed and never turns a locked email off', () => {
    const edited = [{ ...replies, email: true }, { ...refunds, inApp: false, email: false }];

    expect(preferenceChanges([replies, refunds], edited)).toEqual([
      { type: 'PostReplied', inApp: true, email: true },
      { type: 'AccessEnded', inApp: false, email: true },
    ]);
    expect(preferenceChanges([replies], [replies])).toEqual([]);
  });

  it('groups in a fixed order and skips empty groups', () => {
    expect(groupPreferences([refunds, replies]).map((g) => g.group)).toEqual(['Discussions', 'Billing']);
  });

  it('shows compact ages', () => {
    const now = new Date('2026-11-11T12:00:00Z');
    expect(timeAgo('2026-11-11T11:55:00Z', now)).toBe('5m');
    expect(timeAgo('2026-11-11T09:00:00Z', now)).toBe('3h');
    expect(timeAgo('2026-11-09T12:00:00Z', now)).toBe('2d');
  });
});

describe('NotificationsRealtimeService', () => {
  class FakeConnection {
    handlers = new Map<string, (...args: unknown[]) => void>();
    start = async () => undefined;
    stop = async () => undefined;
    on = (name: string, handler: (...args: unknown[]) => void) => this.handlers.set(name, handler);
    onreconnected = () => undefined;
  }

  class TestableRealtime extends NotificationsRealtimeService {
    readonly fake = new FakeConnection();
    protected override createConnection(): HubConnection {
      return this.fake as unknown as HubConnection;
    }
  }

  it('loads the count, then follows pushes from the hub', async () => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), { provide: NotificationsRealtimeService, useClass: TestableRealtime }],
    });
    const realtime = TestBed.inject(NotificationsRealtimeService) as TestableRealtime;
    const backend = TestBed.inject(HttpTestingController);
    let changes = 0;
    realtime.changes.subscribe(() => changes++);

    const connecting = realtime.connect();
    backend.expectOne('/gh-api/api/v1/notifications/unread-count').flush({ count: 2 });
    await connecting;
    expect(realtime.unread()).toBe(2);

    realtime.fake.handlers.get('notificationsChanged')!(5);
    expect(realtime.unread()).toBe(5);
    expect(changes).toBe(1);

    await realtime.disconnect();
    expect(realtime.unread()).toBe(0);
  });
});
