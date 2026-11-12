import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { problemMessage } from '../../core/http/problem';
import { NotificationPreference, NotificationType, groupPreferences, preferenceChanges } from './notification.model';
import { NotificationsService } from './notifications.service';

/** In-app and email switches per kind of notification. Billing and access emails are locked on. */
@Component({
  selector: 'app-notification-settings',
  imports: [RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="mx-auto max-w-3xl px-4 py-8">
      <a routerLink="/me/notifications" class="text-sm text-indigo-600 hover:underline">← Notifications</a>
      <h1 class="mt-2 text-2xl font-semibold">Notification settings</h1>
      <p class="mt-1 text-sm text-slate-500">Emails go to the address on your GymHub account.</p>

      @if (loadError(); as message) {
        <p class="mt-6 text-rose-600">{{ message }}</p>
      } @else if (edited().length === 0) {
        <p class="mt-6 text-slate-500">Loading…</p>
      } @else {
        @for (group of groups(); track group.group) {
          <h2 class="mt-8 text-sm font-semibold uppercase tracking-wide text-slate-500">{{ group.group }}</h2>
          <div class="mt-2 divide-y divide-slate-100 rounded-lg border border-slate-200 bg-white">
            <div class="flex items-center gap-4 px-4 py-2 text-xs text-slate-500">
              <span class="flex-1"></span>
              <span class="w-14 text-center">In app</span>
              <span class="w-14 text-center">Email</span>
            </div>
            @for (p of group.items; track p.type) {
              <div class="flex items-center gap-4 px-4 py-3 text-sm">
                <span class="flex-1">
                  {{ p.label }}
                  @if (p.emailLocked) {
                    <span class="block text-xs text-slate-500">Email required for billing and access</span>
                  }
                </span>
                <span class="w-14 text-center">
                  <input type="checkbox" [checked]="p.inApp" [attr.aria-label]="p.label + ' in app'" (change)="set(p.type, 'inApp', $any($event.target).checked)" />
                </span>
                <span class="w-14 text-center">
                  <input
                    type="checkbox"
                    [checked]="p.email"
                    [disabled]="p.emailLocked"
                    [attr.aria-label]="p.label + ' by email'"
                    (change)="set(p.type, 'email', $any($event.target).checked)"
                  />
                </span>
              </div>
            }
          </div>
        }

        <div class="mt-6 flex items-center gap-3">
          <button class="btn-primary" [disabled]="busy() || changes().length === 0" (click)="save()">Save</button>
          @if (notice(); as message) {
            <span class="text-sm text-emerald-700">{{ message }}</span>
          }
          @if (error(); as message) {
            <span class="text-sm text-rose-600">{{ message }}</span>
          }
        </div>
      }
    </section>
  `,
})
export class NotificationSettingsComponent {
  private readonly notifications = inject(NotificationsService);

  private readonly original = signal<NotificationPreference[]>([]);
  protected readonly edited = signal<NotificationPreference[]>([]);
  protected readonly busy = signal(false);
  protected readonly loadError = signal<string | null>(null);
  protected readonly error = signal<string | null>(null);
  protected readonly notice = signal<string | null>(null);

  protected readonly groups = computed(() => groupPreferences(this.edited()));
  protected readonly changes = computed(() => preferenceChanges(this.original(), this.edited()));

  constructor() {
    void this.load();
  }

  protected set(type: NotificationType, channel: 'inApp' | 'email', value: boolean): void {
    this.notice.set(null);
    this.edited.update((all) => all.map((p) => (p.type === type ? { ...p, [channel]: value } : p)));
  }

  protected async save(): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    try {
      await firstValueFrom(this.notifications.savePreferences(this.changes()));
      this.original.set(this.edited());
      this.notice.set('Saved.');
    } catch (error) {
      this.error.set(problemMessage(error));
    } finally {
      this.busy.set(false);
    }
  }

  private async load(): Promise<void> {
    try {
      const preferences = await firstValueFrom(this.notifications.preferences());
      this.original.set(preferences);
      this.edited.set(preferences);
    } catch (error) {
      this.loadError.set(problemMessage(error));
    }
  }
}
