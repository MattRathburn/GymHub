import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { toObservable, toSignal } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { debounceTime, distinctUntilChanged, firstValueFrom, map, merge, switchMap } from 'rxjs';
import { AuthService } from '../../core/auth/auth.service';
import { LoadState, toLoadState } from '../../core/http/load-state';
import { problemMessage } from '../../core/http/problem';
import { PagedResult } from '../programs/program.model';
import { AdminNavComponent } from './admin-nav.component';
import { AdminUser, adminPageSize } from './admin.model';
import { AdminService } from './admin.service';

/**
 * Admin: find users (from Keycloak), suspend or reactivate them, and revoke the trainer role. Suspending disables
 * sign-in and ends their sessions; revoking archives the trainer's published programs.
 */
@Component({
  selector: 'app-admin-users',
  imports: [DatePipe, RouterLink, AdminNavComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="mx-auto max-w-6xl px-4 py-8">
      <h1 class="text-2xl font-semibold">Users</h1>
      <app-admin-nav class="mt-4 block" />

      <input
        class="input mt-6 max-w-md"
        type="search"
        placeholder="Search by username, email or name"
        aria-label="Search users"
        [value]="search()"
        (input)="setSearch($any($event.target).value)"
      />

      @if (error(); as message) {
        <p class="mt-3 text-sm text-rose-600">{{ message }}</p>
      }

      @switch (page().status) {
        @case ('loading') {
          <p class="mt-6 text-slate-500">Loading…</p>
        }
        @case ('error') {
          <p class="mt-6 text-rose-600">Couldn't load users.</p>
        }
      }

      @if (result(); as r) {
        @if (r.data.length === 0) {
          <p class="mt-6 text-slate-500">No users match.</p>
        } @else {
          <div class="mt-4 overflow-x-auto">
            <table class="w-full text-left text-sm">
              <thead class="border-b border-slate-200 text-slate-500">
                <tr>
                  <th class="py-2 pr-4 font-medium">User</th>
                  <th class="py-2 pr-4 font-medium">Roles</th>
                  <th class="py-2 pr-4 font-medium">Joined</th>
                  <th class="py-2 pr-4 font-medium">Status</th>
                  <th class="py-2 font-medium"><span class="sr-only">Actions</span></th>
                </tr>
              </thead>
              <tbody>
                @for (user of r.data; track user.id) {
                  <tr class="border-b border-slate-100 align-top">
                    <td class="py-3 pr-4">
                      @if (user.isTrainer) {
                        <a [routerLink]="['/trainers', user.id]" class="font-medium hover:text-indigo-700">{{ user.displayName ?? user.username }}</a>
                      } @else {
                        <span class="font-medium">{{ user.displayName ?? user.username }}</span>
                      }
                      <span class="block text-slate-500">{{ user.username }}@if (user.email) { · {{ user.email }} }</span>
                      @if (user.email && !user.emailVerified) {
                        <span class="block text-xs text-amber-700">Email not verified</span>
                      }
                    </td>
                    <td class="py-3 pr-4">
                      @if (user.isAdmin) {
                        <span class="mr-1 rounded bg-indigo-50 px-2 py-0.5 text-xs text-indigo-700">Admin</span>
                      }
                      @if (user.isTrainer) {
                        <span class="rounded bg-emerald-50 px-2 py-0.5 text-xs text-emerald-700">Trainer</span>
                      }
                      @if (!user.isAdmin && !user.isTrainer) {
                        <span class="text-slate-500">Member</span>
                      }
                    </td>
                    <td class="py-3 pr-4 text-slate-600">{{ user.createdAt | date: 'mediumDate' }}</td>
                    <td class="py-3 pr-4">
                      @if (user.suspension; as s) {
                        <span class="font-medium text-rose-700">Suspended</span>
                        <span class="block text-xs text-slate-500">{{ s.suspendedAt | date: 'mediumDate' }}@if (s.reason) { · {{ s.reason }} }</span>
                      } @else if (!user.enabled) {
                        <span class="text-amber-700">Disabled in Keycloak</span>
                      } @else {
                        <span class="text-slate-600">Active</span>
                      }
                      @if (user.deletionScheduledFor; as at) {
                        <span class="block text-xs text-amber-700">Deletion on {{ at | date: 'mediumDate' }}</span>
                      }
                    </td>
                    <td class="py-3 text-right whitespace-nowrap">
                      @if (user.suspension) {
                        <button class="btn-secondary" [disabled]="busy()" (click)="reactivate(user)">Reactivate</button>
                      } @else if (!user.isAdmin && user.id !== auth.user()?.subject) {
                        <button class="btn-secondary" [disabled]="busy()" (click)="suspend(user)">Suspend</button>
                      }
                      @if (user.isTrainer) {
                        <button class="btn-secondary ml-2" [disabled]="busy()" (click)="revoke(user)">Revoke trainer</button>
                      }
                    </td>
                  </tr>
                }
              </tbody>
            </table>
          </div>

          <div class="mt-4 flex items-center gap-3 text-sm">
            <button class="btn-secondary" [disabled]="pageIndex() === 0" (click)="pageIndex.set(pageIndex() - 1)">Previous</button>
            <span class="text-slate-500">Page {{ pageIndex() + 1 }} of {{ pageCount() }}</span>
            <button class="btn-secondary" [disabled]="pageIndex() + 1 >= pageCount()" (click)="pageIndex.set(pageIndex() + 1)">Next</button>
          </div>
        }
      }
    </section>
  `,
})
export class UsersComponent {
  private readonly admin = inject(AdminService);
  protected readonly auth = inject(AuthService);

  protected readonly search = signal('');
  protected readonly pageIndex = signal(0);
  private readonly refresh = signal(0);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);

  private readonly debouncedSearch = toObservable(this.search).pipe(debounceTime(300), distinctUntilChanged());

  protected readonly page = toSignal(
    merge(
      this.debouncedSearch.pipe(map(() => 0)),
      toObservable(computed(() => ({ pageIndex: this.pageIndex(), refresh: this.refresh() }))).pipe(map(() => 1)),
    ).pipe(switchMap(() => this.admin.users(this.search(), this.pageIndex()).pipe(toLoadState()))),
    { initialValue: { status: 'loading' } as LoadState<PagedResult<AdminUser>> },
  );

  protected readonly result = computed(() => {
    const state = this.page();
    return state.status === 'ok' ? state.value : null;
  });

  protected readonly pageCount = computed(() => Math.max(1, Math.ceil((this.result()?.count ?? 0) / adminPageSize)));

  protected setSearch(value: string): void {
    this.search.set(value);
    this.pageIndex.set(0);
  }

  protected suspend(user: AdminUser): Promise<void> {
    const reason = prompt(`Suspend ${user.displayName ?? user.username}? They're signed out and can't sign in until reactivated.\n\nReason (optional, only admins see it):`);
    return reason === null ? Promise.resolve() : this.run(this.admin.suspend(user.id, reason.trim() || null));
  }

  protected reactivate(user: AdminUser): Promise<void> {
    return this.run(this.admin.reactivate(user.id));
  }

  protected revoke(user: AdminUser): Promise<void> {
    const ok = confirm(
      `Revoke ${user.displayName ?? user.username}'s trainer role? Their published programs are archived (buyers keep access). This can't be undone; they can apply again.`,
    );
    return ok ? this.run(this.admin.revokeTrainer(user.id)) : Promise.resolve();
  }

  private async run(action: ReturnType<AdminService['reactivate']>): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    try {
      await firstValueFrom(action);
      this.refresh.update((n) => n + 1);
    } catch (error) {
      this.error.set(problemMessage(error));
    } finally {
      this.busy.set(false);
    }
  }
}
