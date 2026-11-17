import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { formatRating } from '../reviews/reviews.model';
import { StarRatingComponent } from '../reviews/star-rating.component';
import { TrainerCard } from './trainer.model';

/** A trainer in a grid: avatar, name, specialties, and how their programs are doing. */
@Component({
  selector: 'app-trainer-card',
  imports: [RouterLink, StarRatingComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @let t = trainer();
    <a [routerLink]="['/trainers', t.userId]" class="card flex h-full gap-4 transition hover:shadow-md">
      @if (t.avatarUrl) {
        <img [src]="t.avatarUrl" alt="" class="h-14 w-14 shrink-0 rounded-full object-cover" />
      } @else {
        <span class="flex h-14 w-14 shrink-0 items-center justify-center rounded-full bg-indigo-100 text-lg font-semibold text-indigo-700" aria-hidden="true">
          {{ t.displayName.charAt(0).toUpperCase() }}
        </span>
      }
      <span class="min-w-0">
        <span class="block font-semibold">{{ t.displayName }}</span>
        @if (t.specialties.length) {
          <span class="mt-0.5 block truncate text-sm text-slate-600">{{ t.specialties.join(' · ') }}</span>
        }
        <span class="mt-2 flex flex-wrap items-center gap-x-2 text-xs text-slate-500">
          @if (t.rating.count) {
            <app-star-rating [value]="t.rating.average" />
            <span>{{ formatRating(t.rating) }} ·</span>
          }
          {{ t.programCount }} {{ t.programCount === 1 ? 'program' : 'programs' }} ·
          {{ t.followerCount }} {{ t.followerCount === 1 ? 'follower' : 'followers' }}
        </span>
      </span>
    </a>
  `,
})
export class TrainerCardComponent {
  readonly trainer = input.required<TrainerCard>();

  protected readonly formatRating = formatRating;
}
