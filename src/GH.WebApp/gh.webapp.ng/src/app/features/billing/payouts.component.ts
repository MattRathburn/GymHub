import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { toObservable, toSignal } from '@angular/core/rxjs-interop';
import { Observable, firstValueFrom, switchMap } from 'rxjs';
import { LoadState, toLoadState } from '../../core/http/load-state';
import { problemMessage } from '../../core/http/problem';
import { PagedResult } from '../programs/program.model';
import { PayoutStatus, Purchase, Redirect } from './billing.model';
import { BillingService, goToStripe } from './billing.service';
import { PurchasesTableComponent } from './purchases-table.component';

/** Trainer studio: connect a Stripe Express account, open its dashboard, and see and refund sales. */
@Component({
  selector: 'app-payouts',
  imports: [PurchasesTableComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="mx-auto max-w-5xl px-4 py-8">
      <h1 class="text-2xl font-semibold">Payouts</h1>

      @switch (status().status) {
        @case ('loading') {
          <p class="mt-6 text-slate-500">Loading…</p>
        }
        @case ('error') {
          <p class="mt-6 text-rose-600">Couldn't load your payout setup.</p>
        }
      }

      @if (payouts(); as p) {
        <div class="card mt-6 space-y-3">
          @switch (p.setup) {
            @case ('NotStarted') {
              <p>Connect a Stripe account to sell paid programs. Stripe handles payments and pays you out.</p>
              <button class="btn-primary" [disabled]="busy()" (click)="onboard()">Set up payouts</button>
            }
            @case ('InProgress') {
              <p>Your Stripe setup isn't finished, so you can't sell paid programs yet.</p>
              <button class="btn-primary" [disabled]="busy()" (click)="onboard()">Continue setup</button>
            }
            @case ('Ready') {
              <p class="font-medium text-emerald-700">You can sell paid programs.</p>
              @if (!p.payoutsEnabled) {
                <p class="text-sm text-amber-700">Stripe still needs details before it can pay out to your bank account.</p>
              }
            }
          }
          <p class="text-sm text-slate-500">GymHub keeps {{ p.platformFeePercent }}% of each payment. Stripe's own fees also apply.</p>
          @if (p.detailsSubmitted) {
            <button class="btn-secondary" [disabled]="busy()" (click)="dashboard()">Open Stripe dashboard</button>
          }
        </div>
      }

      @if (error(); as message) {
        <p class="mt-3 text-sm text-rose-600">{{ message }}</p>
      }
      @if (notice(); as message) {
        <p class="mt-3 text-sm text-emerald-700">{{ message }}</p>
      }

      <h2 class="mt-10 text-xl font-semibold">Sales</h2>
      <p class="mt-1 text-sm text-slate-500">You can refund a sale for 30 days after its latest payment. Refunds end the buyer's access.</p>
      @if (sales(); as state) {
        @if (state.status === 'ok') {
          <app-purchases-table class="mt-4 block" [purchases]="state.value.data" [busy]="busy()" (refund)="refund($event)" />
        }
      }
    </section>
  `,
})
export class PayoutsComponent {
  private readonly billing = inject(BillingService);

  private readonly refresh = signal(0);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly notice = signal<string | null>(null);

  protected readonly status = toSignal(
    toObservable(this.refresh).pipe(switchMap(() => this.billing.payoutStatus().pipe(toLoadState()))),
    { initialValue: { status: 'loading' } as LoadState<PayoutStatus> },
  );

  protected readonly sales = toSignal(
    toObservable(this.refresh).pipe(switchMap(() => this.billing.sales({ pageSize: 50 }).pipe(toLoadState()))),
    { initialValue: { status: 'loading' } as LoadState<PagedResult<Purchase>> },
  );

  protected readonly payouts = computed(() => {
    const state = this.status();
    return state.status === 'ok' ? state.value : null;
  });

  protected onboard(): Promise<void> {
    return this.redirect(this.billing.startOnboarding());
  }

  protected dashboard(): Promise<void> {
    return this.redirect(this.billing.payoutDashboard());
  }

  protected async refund(purchase: Purchase): Promise<void> {
    if (!confirm(`Refund this sale of ${purchase.programTitle}? The buyer loses access.`)) {
      return;
    }

    this.busy.set(true);
    this.error.set(null);
    try {
      await firstValueFrom(this.billing.refund(purchase.id));
      this.notice.set('Refunded.');
      this.refresh.update((n) => n + 1);
    } catch (error) {
      this.error.set(problemMessage(error));
    } finally {
      this.busy.set(false);
    }
  }

  private async redirect(request: Observable<Redirect>): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    try {
      goToStripe((await firstValueFrom(request)).url);
    } catch (error) {
      this.error.set(problemMessage(error));
      this.busy.set(false);
    }
  }
}
