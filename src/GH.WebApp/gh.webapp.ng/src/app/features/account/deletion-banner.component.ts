import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, effect, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { AuthService } from '../../core/auth/auth.service';
import { AccountService } from './account.service';

/** Shown on every page while the signed-in member's account is waiting to be deleted. */
@Component({
  selector: 'app-deletion-banner',
  imports: [DatePipe, RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (account.status()?.deletion; as pending) {
      <div class="border-b border-amber-200 bg-amber-50" role="status">
        <div class="mx-auto flex max-w-6xl flex-wrap items-center gap-3 px-4 py-2 text-sm text-amber-900">
          <span>
            Your account will be deleted on <strong>{{ pending.scheduledFor | date: 'mediumDate' }}</strong>.
            <a routerLink="/me/account" class="underline">Details</a>
          </span>
          <button class="btn-secondary ml-auto" [disabled]="busy()" (click)="keep()">Keep my account</button>
        </div>
      </div>
    }
  `,
})
export class DeletionBannerComponent {
  protected readonly account = inject(AccountService);
  private readonly auth = inject(AuthService);
  protected readonly busy = signal(false);

  constructor() {
    effect(() => {
      if (this.auth.isAuthenticated()) {
        this.account.refresh().subscribe();
      }
    });
  }

  protected async keep(): Promise<void> {
    this.busy.set(true);
    try {
      await firstValueFrom(this.account.cancelDeletion());
    } catch {
      // The refresh below shows whatever the state is now.
    } finally {
      await firstValueFrom(this.account.refresh());
      this.busy.set(false);
    }
  }
}
