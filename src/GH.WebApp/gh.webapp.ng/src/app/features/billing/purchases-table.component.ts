import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Purchase, formatMoney } from './billing.model';

/** Sales for the trainer's payouts page and the admin purchases page, with a refund button where allowed. */
@Component({
  selector: 'app-purchases-table',
  imports: [DatePipe, RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="overflow-x-auto rounded-lg border border-slate-200 bg-white">
      <table class="w-full text-left text-sm">
        <thead class="bg-slate-50 text-xs uppercase text-slate-500">
          <tr>
            <th class="px-3 py-2">Date</th>
            <th class="px-3 py-2">Program</th>
            <th class="px-3 py-2">Buyer</th>
            <th class="px-3 py-2">Price</th>
            <th class="px-3 py-2">Fee</th>
            <th class="px-3 py-2">Status</th>
            <th class="px-3 py-2"></th>
          </tr>
        </thead>
        <tbody class="divide-y divide-slate-100">
          @for (purchase of purchases(); track purchase.id) {
            <tr>
              <td class="px-3 py-2 whitespace-nowrap">{{ purchase.createdAt | date: 'mediumDate' }}</td>
              <td class="px-3 py-2">
                <a [routerLink]="['/programs', purchase.programId]" class="text-indigo-600 hover:underline">{{ purchase.programTitle }}</a>
              </td>
              <td class="px-3 py-2">{{ purchase.buyerName ?? 'Member' }}</td>
              <td class="px-3 py-2 whitespace-nowrap">{{ money(purchase) }}</td>
              <td class="px-3 py-2 whitespace-nowrap text-slate-500">{{ fee(purchase) }}</td>
              <td class="px-3 py-2">{{ purchase.status }}</td>
              <td class="px-3 py-2 text-right">
                @if (purchase.canRefund) {
                  <button class="btn-secondary" [disabled]="busy()" (click)="refund.emit(purchase)">Refund</button>
                }
              </td>
            </tr>
          } @empty {
            <tr>
              <td colspan="7" class="px-3 py-6 text-center text-slate-500">No sales yet.</td>
            </tr>
          }
        </tbody>
      </table>
    </div>
  `,
})
export class PurchasesTableComponent {
  readonly purchases = input.required<Purchase[]>();
  readonly busy = input(false);
  readonly refund = output<Purchase>();

  protected money(purchase: Purchase): string {
    return formatMoney(purchase.amountMinor, purchase.currency, purchase.interval);
  }

  protected fee(purchase: Purchase): string {
    return formatMoney(purchase.feeMinor, purchase.currency);
  }
}
