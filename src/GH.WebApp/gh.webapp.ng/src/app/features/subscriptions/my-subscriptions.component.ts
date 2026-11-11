import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { catchError, firstValueFrom, of } from 'rxjs';
import { LoadState, toLoadState } from '../../core/http/load-state';
import { problemMessage } from '../../core/http/problem';
import { Purchase, nextPriceLine, purchaseStatusLine } from '../billing/billing.model';
import { BillingService, goToStripe } from '../billing/billing.service';
import { MyEnrollment } from './subscription.model';
import { SubscriptionsService } from './subscriptions.service';

@Component({
  selector: 'app-my-subscriptions',
  imports: [RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="mx-auto max-w-3xl px-4 py-8">
      <div class="flex items-center justify-between gap-4">
        <h1 class="text-2xl font-semibold">My programs</h1>
        @if (purchases().length > 0) {
          <button class="btn-secondary" [disabled]="busy()" (click)="manageBilling()">Manage billing</button>
        }
      </div>
      @if (error(); as message) {
        <p class="mt-2 text-sm text-rose-600">{{ message }}</p>
      }

      @switch (enrollments().status) {
        @case ('loading') {
          <p class="mt-6 text-slate-500">Loading…</p>
        }
        @case ('error') {
          <p class="mt-6 text-rose-600">Couldn't load your programs.</p>
        }
      }

      @if (enrollments(); as state) {
        @if (state.status === 'ok') {
          <ul class="mt-6 space-y-3">
            @for (enrollment of state.value; track enrollment.programId) {
              <li>
                <a [routerLink]="['/programs', enrollment.programId]" class="card flex justify-between hover:shadow-md">
                  <span class="font-medium">{{ enrollment.programTitle ?? 'Program' }}</span>
                  <span class="text-right text-xs text-slate-500">
                    @if (purchaseFor(enrollment.programId); as purchase) {
                      <span class="block">{{ statusLine(purchase) }}</span>
                      @if (nextPrice(purchase); as next) {
                        <span class="block text-amber-700">{{ next }}</span>
                      }
                    }
                    @if (enrollment.programStatus === 'Archived') {
                      <span class="block">Archived by trainer</span>
                    }
                  </span>
                </a>
              </li>
            } @empty {
              <li class="text-slate-500">
                You haven't joined a program yet. <a routerLink="/" class="text-indigo-600 underline">Browse programs</a>
              </li>
            }
          </ul>
        }
      }
    </section>
  `,
})
export class MySubscriptionsComponent {
  private readonly subscriptions = inject(SubscriptionsService);
  private readonly billing = inject(BillingService);

  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly statusLine = purchaseStatusLine;
  protected readonly nextPrice = nextPriceLine;

  protected readonly purchases = toSignal(this.billing.mine().pipe(catchError(() => of([] as Purchase[]))), {
    initialValue: [] as Purchase[],
  });

  private readonly purchasesByProgram = computed(() => new Map(this.purchases().map((p) => [p.programId, p])));

  protected purchaseFor(programId: string): Purchase | undefined {
    return this.purchasesByProgram().get(programId);
  }

  protected async manageBilling(): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    try {
      goToStripe((await firstValueFrom(this.billing.portal())).url);
    } catch (error) {
      this.error.set(problemMessage(error));
      this.busy.set(false);
    }
  }

  protected readonly enrollments = toSignal(this.subscriptions.mine().pipe(toLoadState()), {
    initialValue: { status: 'loading' } as LoadState<MyEnrollment[]>,
  });
}
