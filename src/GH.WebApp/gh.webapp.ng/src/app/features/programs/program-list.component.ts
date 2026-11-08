import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { toObservable, toSignal } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { debounceTime, switchMap } from 'rxjs';
import { LoadState, toLoadState } from '../../core/http/load-state';
import { PagedResult, ProgramCard, formatPrice } from './program.model';
import { ProgramsService } from './programs.service';

type PriceFilter = 'all' | 'free' | 'paid';

@Component({
  selector: 'app-program-list',
  imports: [RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="mx-auto max-w-6xl px-4 py-8">
      <h1 class="text-2xl font-semibold">Find a program</h1>

      <div class="mt-4 flex flex-wrap gap-3">
        <input
          class="input max-w-sm"
          type="search"
          placeholder="Search by title"
          [value]="search()"
          (input)="search.set($any($event.target).value)"
        />
        <select class="input w-40" [value]="price()" (change)="price.set($any($event.target).value)">
          <option value="all">Any price</option>
          <option value="free">Free</option>
          <option value="paid">Paid</option>
        </select>
      </div>

      @switch (programs().status) {
        @case ('loading') {
          <p class="mt-8 text-slate-500">Loading programs…</p>
        }
        @case ('error') {
          <p class="mt-8 text-rose-600">{{ errorMessage() }}</p>
        }
        @case ('ok') {
          @if (cards().length === 0) {
            <p class="mt-8 text-slate-500">No programs match yet.</p>
          }
          <ul class="mt-6 grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
            @for (card of cards(); track card.id) {
              <li>
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
                  <p class="mt-3 text-xs text-slate-500">
                    {{ card.kind === 'Finite' ? 'Fixed dates' : 'Ongoing' }} · {{ card.weekCount }} weeks
                  </p>
                </a>
              </li>
            }
          </ul>
        }
      }
    </section>
  `,
})
export class ProgramListComponent {
  private readonly programsService = inject(ProgramsService);

  protected readonly search = signal('');
  protected readonly price = signal<PriceFilter>('all');
  protected readonly formatPrice = formatPrice;

  private readonly query = computed(() => ({
    search: this.search().trim(),
    free: this.price() === 'all' ? null : this.price() === 'free',
  }));

  protected readonly programs = toSignal(
    toObservable(this.query).pipe(
      debounceTime(250),
      switchMap((query) => this.programsService.browse(query).pipe(toLoadState())),
    ),
    { initialValue: { status: 'loading' } as LoadState<PagedResult<ProgramCard>> },
  );

  protected readonly cards = computed(() => {
    const state = this.programs();
    return state.status === 'ok' ? state.value.data : [];
  });

  protected readonly errorMessage = computed(() => {
    const state = this.programs();
    return state.status === 'error' ? state.message : '';
  });
}
