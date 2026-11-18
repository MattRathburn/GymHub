import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { problemMessage } from '../../core/http/problem';
import { confirmsDeletion, deletionPhrase } from './account.model';
import { AccountService } from './account.service';

/** Download your data, and delete your account after a 7-day grace period (or cancel that). */
@Component({
  selector: 'app-account',
  imports: [DatePipe, RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="mx-auto max-w-xl space-y-6 px-4 py-8">
      <div>
        <h1 class="text-2xl font-semibold">Account</h1>
        <p class="mt-1 text-sm text-slate-600">
          Your name and avatar are on <a routerLink="/me/profile" class="text-indigo-600 underline">your profile</a>.
          How we use your data is in the <a routerLink="/privacy" class="text-indigo-600 underline">privacy policy</a>.
        </p>
      </div>

      <div class="card">
        <h2 class="font-semibold">Download your data</h2>
        <p class="mt-1 text-sm text-slate-600">
          A JSON file with your profile, programs, purchases, training log, posts, reviews and notification settings.
        </p>
        <a class="btn-secondary mt-4 inline-block" [href]="account.exportUrl" download>Download my data</a>
      </div>

      <div class="card border-rose-200">
        <h2 class="font-semibold text-rose-700">Delete your account</h2>

        @if (status(); as s) {
          @if (s.deletion; as pending) {
            <p class="mt-2 text-sm text-slate-700">
              Your account is scheduled for deletion on <strong>{{ pending.scheduledFor | date: 'mediumDate' }}</strong>.
              Until then everything works as usual.
            </p>
            @if (s.deletionBlockers.length) {
              <p class="mt-2 text-sm text-amber-700">It will wait until this is sorted out:</p>
              <ul class="mt-1 list-disc pl-5 text-sm text-amber-700">
                @for (blocker of s.deletionBlockers; track blocker) {
                  <li>{{ blocker }}</li>
                }
              </ul>
            }
            <button class="btn-primary mt-4" [disabled]="busy()" (click)="cancel()">Keep my account</button>
          } @else {
            <p class="mt-2 text-sm text-slate-600">
              After a 7-day grace period we delete your profile, training log, enrollments and notifications, and your
              sign-in. Your discussion posts and reviews stay, shown as by "Deleted user". Purchase records are kept for
              accounting. You can cancel any time in those 7 days by signing in.
            </p>

            @if (s.deletionBlockers.length) {
              <p class="mt-4 text-sm font-medium text-amber-700">Before you can delete your account:</p>
              <ul class="mt-1 list-disc pl-5 text-sm text-amber-700">
                @for (blocker of s.deletionBlockers; track blocker) {
                  <li>{{ blocker }}</li>
                }
              </ul>
            } @else {
              <label class="label mt-4 block" for="confirmDelete">Type "{{ phrase }}" to confirm</label>
              <input
                id="confirmDelete"
                class="input"
                autocomplete="off"
                [value]="confirmation()"
                (input)="confirmation.set($any($event.target).value)"
              />
              <button class="btn-danger mt-4" [disabled]="busy() || !confirmed()" (click)="schedule()">Delete my account</button>
            }
          }
        } @else {
          <p class="mt-2 text-sm text-slate-500">Loading…</p>
        }
      </div>

      @if (error(); as message) {
        <p class="text-sm text-rose-600">{{ message }}</p>
      }
    </section>
  `,
})
export class AccountComponent {
  protected readonly account = inject(AccountService);

  protected readonly phrase = deletionPhrase;
  protected readonly status = this.account.status;
  protected readonly confirmation = signal('');
  protected readonly confirmed = computed(() => confirmsDeletion(this.confirmation()));
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);

  constructor() {
    this.account.refresh().subscribe();
  }

  protected async schedule(): Promise<void> {
    await this.run(async () => {
      await firstValueFrom(this.account.scheduleDeletion(this.confirmation()));
      this.confirmation.set('');
    });
  }

  protected async cancel(): Promise<void> {
    await this.run(() => firstValueFrom(this.account.cancelDeletion()));
  }

  private async run(action: () => Promise<unknown>): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    try {
      await action();
    } catch (error) {
      this.error.set(problemMessage(error));
    } finally {
      await firstValueFrom(this.account.refresh());
      this.busy.set(false);
    }
  }
}
