import { ChangeDetectionStrategy, Component, DestroyRef, ElementRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Router, RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { NotificationItem, timeAgo } from './notification.model';
import { NotificationsRealtimeService } from './notifications-realtime.service';
import { NotificationsService } from './notifications.service';

/** The nav bell: unread badge, and a dropdown with the latest notifications. */
@Component({
  selector: 'app-notification-bell',
  imports: [RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { '(document:click)': 'closeOnOutsideClick($event)', '(document:keydown.escape)': 'open.set(false)' },
  template: `
    <div class="relative">
      <button
        type="button"
        class="relative rounded-full p-2 text-slate-600 hover:bg-slate-100 hover:text-slate-900"
        [attr.aria-label]="realtime.unread() ? realtime.unread() + ' unread notifications' : 'Notifications'"
        [attr.aria-expanded]="open()"
        (click)="toggle()"
      >
        <svg viewBox="0 0 24 24" class="h-5 w-5" fill="none" stroke="currentColor" stroke-width="2" aria-hidden="true">
          <path stroke-linecap="round" stroke-linejoin="round" d="M15 17h5l-1.4-1.4A2 2 0 0 1 18 14.2V11a6 6 0 1 0-12 0v3.2a2 2 0 0 1-.6 1.4L4 17h5m6 0a3 3 0 1 1-6 0" />
        </svg>
        @if (realtime.unread() > 0) {
          <span class="absolute -right-0.5 -top-0.5 min-w-5 rounded-full bg-rose-600 px-1 text-center text-xs font-semibold text-white">
            {{ realtime.unread() > 99 ? '99+' : realtime.unread() }}
          </span>
        }
      </button>

      @if (open()) {
        <div class="absolute right-0 z-20 mt-2 w-80 max-w-[90vw] rounded-lg border border-slate-200 bg-white shadow-lg">
          <div class="flex items-center justify-between border-b border-slate-100 px-3 py-2">
            <span class="text-sm font-semibold">Notifications</span>
            @if (realtime.unread() > 0) {
              <button class="text-xs text-indigo-600 hover:underline" (click)="markAllRead()">Mark all read</button>
            }
          </div>

          <ul class="max-h-96 divide-y divide-slate-100 overflow-y-auto">
            @for (item of items(); track item.id) {
              <li>
                <button class="flex w-full gap-2 px-3 py-2 text-left hover:bg-slate-50" (click)="openItem(item)">
                  <span class="mt-1.5 h-2 w-2 shrink-0 rounded-full" [class.bg-indigo-600]="!item.isRead"></span>
                  <span class="min-w-0 flex-1">
                    <span class="block text-sm" [class.font-semibold]="!item.isRead">{{ item.title }}</span>
                    <span class="block truncate text-xs text-slate-500">{{ item.body }}</span>
                  </span>
                  <span class="shrink-0 text-xs text-slate-400">{{ ago(item.createdAt) }}</span>
                </button>
              </li>
            } @empty {
              <li class="px-3 py-6 text-center text-sm text-slate-500">{{ loading() ? 'Loading…' : "You're all caught up." }}</li>
            }
          </ul>

          <div class="flex justify-between border-t border-slate-100 px-3 py-2 text-xs">
            <a routerLink="/me/notifications" class="text-indigo-600 hover:underline" (click)="open.set(false)">See all</a>
            <a routerLink="/me/notifications/settings" class="text-slate-500 hover:underline" (click)="open.set(false)">Settings</a>
          </div>
        </div>
      }
    </div>
  `,
})
export class NotificationBellComponent {
  protected readonly realtime = inject(NotificationsRealtimeService);
  private readonly notifications = inject(NotificationsService);
  private readonly router = inject(Router);
  private readonly host = inject(ElementRef<HTMLElement>);

  protected readonly open = signal(false);
  protected readonly loading = signal(false);
  protected readonly items = signal<NotificationItem[]>([]);
  protected readonly ago = (iso: string) => timeAgo(iso);

  constructor() {
    void this.realtime.connect();
    inject(DestroyRef).onDestroy(() => void this.realtime.disconnect());

    // Keep an open dropdown current.
    this.realtime.changes.pipe(takeUntilDestroyed()).subscribe(() => {
      if (this.open()) {
        void this.load();
      }
    });
  }

  protected toggle(): void {
    this.open.update((open) => !open);

    if (this.open()) {
      void this.load();
    }
  }

  protected async openItem(item: NotificationItem): Promise<void> {
    this.open.set(false);

    if (!item.isRead) {
      await firstValueFrom(this.notifications.markRead(item.id)).catch(() => undefined);
      void this.realtime.refresh();
    }

    await this.router.navigateByUrl(item.link);
  }

  protected async markAllRead(): Promise<void> {
    await firstValueFrom(this.notifications.markAllRead()).catch(() => undefined);
    this.items.update((items) => items.map((i) => ({ ...i, isRead: true })));
    this.realtime.unread.set(0);
  }

  protected closeOnOutsideClick(event: MouseEvent): void {
    if (this.open() && !this.host.nativeElement.contains(event.target as Node)) {
      this.open.set(false);
    }
  }

  private async load(): Promise<void> {
    this.loading.set(true);
    try {
      this.items.set((await firstValueFrom(this.notifications.list({ pageSize: 10 }))).data);
    } catch {
      this.items.set([]);
    } finally {
      this.loading.set(false);
    }
  }
}
