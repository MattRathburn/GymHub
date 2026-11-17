import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { formatRating } from '../reviews/reviews.model';
import { StarRatingComponent } from '../reviews/star-rating.component';
import { ProgramCard, formatPrice } from './program.model';

/** A program in a grid: cover, title, price, summary and rating. Used by the catalog and the landing page. */
@Component({
  selector: 'app-program-card',
  imports: [RouterLink, StarRatingComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @let card = program();
    <a [routerLink]="['/programs', card.id]" class="card block h-full transition hover:shadow-md">
      @if (card.coverUrl) {
        <img [src]="card.coverUrl" alt="" class="mb-3 h-40 w-full rounded-md object-cover" />
      }
      <div class="flex items-start justify-between gap-2">
        <h2 class="font-semibold">{{ card.title }}</h2>
        <span class="shrink-0 rounded bg-indigo-50 px-2 py-0.5 text-xs font-medium text-indigo-700">
          {{ formatPrice(card.price) }}
        </span>
      </div>
      @if (card.summary) {
        <p class="mt-1 text-sm text-slate-600">{{ card.summary }}</p>
      }
      <p class="mt-3 flex flex-wrap items-center gap-x-2 text-xs text-slate-500">
        @if (card.rating.count) {
          <app-star-rating [value]="card.rating.average" />
          <span>{{ formatRating(card.rating) }} ·</span>
        }
        {{ card.kind === 'Finite' ? 'Fixed dates' : 'Ongoing' }} · {{ card.weekCount }} {{ card.weekCount === 1 ? 'week' : 'weeks' }}
      </p>
    </a>
  `,
})
export class ProgramCardComponent {
  readonly program = input.required<ProgramCard>();

  protected readonly formatPrice = formatPrice;
  protected readonly formatRating = formatRating;
}
