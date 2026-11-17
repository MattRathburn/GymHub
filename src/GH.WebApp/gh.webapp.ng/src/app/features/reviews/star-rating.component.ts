import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { starPercent } from './reviews.model';

/** Five stars filled to a rating (partial stars included). Purely visual; the label says the number. */
@Component({
  selector: 'app-star-rating',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <span class="relative inline-block leading-none" [class]="sizeClass()" role="img" [attr.aria-label]="label()">
      <span class="text-slate-300" aria-hidden="true">★★★★★</span>
      <span class="absolute inset-y-0 left-0 overflow-hidden whitespace-nowrap text-amber-400" [style.width.%]="percent()" aria-hidden="true">★★★★★</span>
    </span>
  `,
})
export class StarRatingComponent {
  readonly value = input<number | null>(null);
  readonly size = input<'sm' | 'lg'>('sm');

  protected readonly percent = computed(() => starPercent(this.value()));
  protected readonly label = computed(() => (this.value() === null ? 'Not rated yet' : `Rated ${this.value()} out of 5`));
  protected readonly sizeClass = computed(() => (this.size() === 'lg' ? 'text-2xl' : 'text-sm'));
}
