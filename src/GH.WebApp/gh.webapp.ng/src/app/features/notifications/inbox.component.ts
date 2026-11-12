import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed, toObservable, toSignal } from '@angular/core/rxjs-interop';
import { Router, RouterLink } from '@angular/router';
import { firstValueFrom, switchMap } from 'rxjs';
import { LoadState, toLoadState } from '../../core/http/load-state';
import { PagedResult } from '../programs/program.model';
import { NotificationItem } from './notification.model';
import { NotificationsRealtimeService } from './notifications-realtime.service';
import { NotificationsService } from './notifications.service';

const pageSize = 20;

/** Every notification, newest first, with an unread filter. */
@Component({
  selector: 'app-inbox',
  imports: [DatePipe, RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="mx-auto max-w-3xl px-4 py-8">
      <div class="flex flex-wrap items-center justify-between gap-3">
        <h1 class="text-2xl font-semibold">Notifications</h1>
        <div class="flex items-center gap-3 text-sm">
          <label class="flex items-center gap-2">
            <input type="checkbox" [checked]="unreadOnly()" (change)="setUnreadOnly($any($event.target).checked)" />
            Unread only
          </label>
          <button class="btn-secondary" [disabled]="realtime.unread() === 0" (click)="markAllRead()">Mark all read</button>
          <a routerLink="/me/notifications/settings" class="text-indigo-600 hover:underline">Settings</a>
        </div>
      </div>

      @switch (page().status) {
        @case ('loading') {
          <p class="mt-6 text-slate-500">Loading…</p>
        }
        @case ('error') {
          <p class="mt-6 text-rose-600">Couldn't load your notifications.</p>
        }
      }

      @if (result(); as r) {
        <ul class="mt-6 divide-y divide-slate-200 rounded-lg border border-slate-200 bg-white">
          @for (item of r.data; track item.id) {
            <li>
              <button class="flex w-full gap-3 px-4 py-3 text-left hover:bg-slate-50" (click)="open(item)">
                <span class="mt-2 h-2 w-2 shrink-0 rounded-full" [class.bg-indigo-600]="!item.isRead"></span>
                <span class="min-w-0 flex-1">
                  <span class="block" [class.font-semibold]="!item.isRead">{{ item.title }}</span>
                  <span class="block text-sm text-slate-600">{{ item.body }}</span>
                </span>
                <span class="shrink-0 text-xs text-slate-400">{{ item.createdAt | date: 'short' }}</span>
              </button>
            </li>
          } @empty {
            <li class="px-4 py-8 text-center text-slate-500">{{ unreadOnly() ? 'No unread notifications.' : 'No notifications yet.' }}</li>
          }
        </ul>

        @if (pageCount() > 1) {
          <div class="mt-4 flex items-center gap-3 text-sm">
            <button class="btn-secondary" [disabled]="pageIndex() === 0" (click)="pageIndex.set(pageIndex() - 1)">Newer</button>
            <span class="text-slate-500">Page {{ pageIndex() + 1 }} of {{ pageCount() }}</span>
            <button class="btn-secondary" [disabled]="pageIndex() + 1 >= pageCount()" (click)="pageIndex.set(pageIndex() + 1)">Older</button>
          </div>
        }
      }
    </section>
  `,
})
export class InboxComponent {
  private readonly notifications = inject(NotificationsService);
  private readonly router = inject(Router);
  protected readonly realtime = inject(NotificationsRealtimeService);

  protected readonly unreadOnly = signal(false);
  protected readonly pageIndex = signal(0);
  private readonly refresh = signal(0);

  protected readonly page = toSignal(
    toObservable(computed(() => ({ unreadOnly: this.unreadOnly(), pageIndex: this.pageIndex(), refresh: this.refresh() }))).pipe(
      switchMap(({ unreadOnly, pageIndex }) => this.notifications.list({ unreadOnly, pageIndex, pageSize }).pipe(toLoadState())),
    ),
    { initialValue: { status: 'loading' } as LoadState<PagedResult<NotificationItem>> },
  );

  protected readonly result = computed(() => {
    const state = this.page();
    return state.status === 'ok' ? state.value : null;
  });

  protected readonly pageCount = computed(() => Math.max(1, Math.ceil((this.result()?.count ?? 0) / pageSize)));

  constructor() {
    this.realtime.changes.pipe(takeUntilDestroyed()).subscribe(() => this.refresh.update((n) => n + 1));
  }

  protected setUnreadOnly(value: boolean): void {
    this.unreadOnly.set(value);
    this.pageIndex.set(0);
  }

  protected async open(item: NotificationItem): Promise<void> {
    if (!item.isRead) {
      await firstValueFrom(this.notifications.markRead(item.id)).catch(() => undefined);
      void this.realtime.refresh();
    }

    await this.router.navigateByUrl(item.link);
  }

  protected async markAllRead(): Promise<void> {
    await firstValueFrom(this.notifications.markAllRead()).catch(() => undefined);
    this.realtime.unread.set(0);
    this.refresh.update((n) => n + 1);
  }
}
