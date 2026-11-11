import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { toObservable, toSignal } from '@angular/core/rxjs-interop';
import { firstValueFrom, switchMap } from 'rxjs';
import { LoadState, toLoadState } from '../../../core/http/load-state';
import { problemMessage } from '../../../core/http/problem';
import { PagedResult } from '../../programs/program.model';
import { Purchase, PurchaseStatus } from '../billing.model';
import { BillingService } from '../billing.service';
import { PurchasesTableComponent } from '../purchases-table.component';

const pageSize = 25;

/** Admin: every purchase, filtered by status, with refunds at any time. */
@Component({
  selector: 'app-admin-purchases',
  imports: [PurchasesTableComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="mx-auto max-w-6xl px-4 py-8">
      <div class="flex flex-wrap items-end justify-between gap-4">
        <h1 class="text-2xl font-semibold">Purchases</h1>
        <select class="input w-48" aria-label="Status" (change)="setStatus($any($event.target).value)">
          <option value="">All statuses</option>
          @for (option of statuses; track option) {
            <option [value]="option">{{ option }}</option>
          }
        </select>
      </div>
      <p class="mt-1 text-sm text-slate-500">Disputes are answered in the Stripe dashboard. A disputed purchase has already lost access.</p>

      @if (error(); as message) {
        <p class="mt-3 text-sm text-rose-600">{{ message }}</p>
      }

      @switch (page().status) {
        @case ('loading') {
          <p class="mt-6 text-slate-500">Loading…</p>
        }
        @case ('error') {
          <p class="mt-6 text-rose-600">Couldn't load purchases.</p>
        }
      }

      @if (result(); as r) {
        <app-purchases-table class="mt-4 block" [purchases]="r.data" [busy]="busy()" (refund)="refund($event)" />
        <div class="mt-4 flex items-center gap-3 text-sm">
          <button class="btn-secondary" [disabled]="pageIndex() === 0" (click)="pageIndex.set(pageIndex() - 1)">Previous</button>
          <span class="text-slate-500">Page {{ pageIndex() + 1 }} of {{ pageCount() }}</span>
          <button class="btn-secondary" [disabled]="pageIndex() + 1 >= pageCount()" (click)="pageIndex.set(pageIndex() + 1)">Next</button>
        </div>
      }
    </section>
  `,
})
export class PurchasesComponent {
  private readonly billing = inject(BillingService);

  protected readonly statuses: PurchaseStatus[] = ['Trialing', 'Active', 'PastDue', 'Ended', 'Refunded', 'Disputed'];
  protected readonly status = signal<PurchaseStatus | null>(null);
  protected readonly pageIndex = signal(0);
  private readonly refresh = signal(0);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);

  protected readonly page = toSignal(
    toObservable(computed(() => ({ status: this.status(), pageIndex: this.pageIndex(), refresh: this.refresh() }))).pipe(
      switchMap(({ status, pageIndex }) => this.billing.allPurchases({ status, pageIndex, pageSize }).pipe(toLoadState())),
    ),
    { initialValue: { status: 'loading' } as LoadState<PagedResult<Purchase>> },
  );

  protected readonly result = computed(() => {
    const state = this.page();
    return state.status === 'ok' ? state.value : null;
  });

  protected readonly pageCount = computed(() => Math.max(1, Math.ceil((this.result()?.count ?? 0) / pageSize)));

  protected setStatus(value: string): void {
    this.status.set((value || null) as PurchaseStatus | null);
    this.pageIndex.set(0);
  }

  protected async refund(purchase: Purchase): Promise<void> {
    if (!confirm(`Refund ${purchase.buyerName ?? 'this member'}'s purchase of ${purchase.programTitle}? They lose access.`)) {
      return;
    }

    this.busy.set(true);
    this.error.set(null);
    try {
      await firstValueFrom(this.billing.refund(purchase.id));
      this.refresh.update((n) => n + 1);
    } catch (error) {
      this.error.set(problemMessage(error));
    } finally {
      this.busy.set(false);
    }
  }
}
