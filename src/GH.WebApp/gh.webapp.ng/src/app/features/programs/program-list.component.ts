import { ChangeDetectionStrategy, Component, DestroyRef, computed, effect, inject, input, signal, untracked } from '@angular/core';
import { toObservable, toSignal } from '@angular/core/rxjs-interop';
import { Router } from '@angular/router';
import { switchMap } from 'rxjs';
import { LoadState, toLoadState } from '../../core/http/load-state';
import { ProgramCardComponent } from './program-card.component';
import {
  CatalogState,
  PagedResult,
  PricingModel,
  ProgramCard,
  ProgramKind,
  ProgramSort,
  browseQueryFor,
  catalogQueryParams,
  readCatalogState,
} from './program.model';
import { ProgramsService } from './programs.service';

/** The catalog. Every filter lives in the query string, so a search or a sorted view can be linked to. */
@Component({
  selector: 'app-program-list',
  imports: [ProgramCardComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="mx-auto max-w-6xl px-4 py-8">
      <h1 class="text-2xl font-semibold">Find a program</h1>

      <div class="mt-4 flex flex-wrap gap-3">
        <input
          class="input max-w-sm"
          type="search"
          placeholder="Search programs or trainers"
          aria-label="Search programs or trainers"
          [value]="searchText()"
          (input)="typed($any($event.target).value)"
        />
        <select class="input w-44" aria-label="Pricing" [value]="state().pricing ?? ''" (change)="setPricing($any($event.target).value)">
          <option value="">Any price</option>
          <option value="Free">Free</option>
          <option value="OneTime">One-time purchase</option>
          <option value="Recurring">Subscription</option>
        </select>
        <select class="input w-40" aria-label="Schedule" [value]="state().kind ?? ''" (change)="setKind($any($event.target).value)">
          <option value="">Any schedule</option>
          <option value="Ongoing">Ongoing</option>
          <option value="Finite">Fixed dates</option>
        </select>
        <select class="input w-48" aria-label="Sort" [value]="state().sort" (change)="setSort($any($event.target).value)">
          <option value="Newest">Newest</option>
          <option value="TopRated">Top rated</option>
          <option value="PriceLow">Price: low to high</option>
          <option value="PriceHigh">Price: high to low</option>
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
            <p class="mt-8 text-slate-500">
              No programs match.
              @if (filtered()) {
                <button class="text-indigo-600 underline" (click)="clear()">Clear the filters</button>
              }
            </p>
          } @else {
            <p class="mt-6 text-sm text-slate-500">{{ total() }} {{ total() === 1 ? 'program' : 'programs' }}</p>
            <ul class="mt-3 grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
              @for (card of cards(); track card.id) {
                <li><app-program-card [program]="card" /></li>
              }
            </ul>

            @if (pageCount() > 1) {
              <nav class="mt-8 flex items-center justify-center gap-4" aria-label="Pages">
                <button class="btn-secondary" [disabled]="state().page === 0" (click)="goToPage(state().page - 1)">Previous</button>
                <span class="text-sm text-slate-600">Page {{ state().page + 1 }} of {{ pageCount() }}</span>
                <button class="btn-secondary" [disabled]="state().page + 1 >= pageCount()" (click)="goToPage(state().page + 1)">Next</button>
              </nav>
            }
          }
        }
      }
    </section>
  `,
})
export class ProgramListComponent {
  private readonly programsService = inject(ProgramsService);
  private readonly router = inject(Router);

  // Query parameters, bound by withComponentInputBinding.
  readonly q = input<string>();
  readonly pricing = input<string>();
  readonly kind = input<string>();
  readonly sort = input<string>();
  readonly page = input<string>();

  protected readonly state = computed(() =>
    readCatalogState({ q: this.q(), pricing: this.pricing(), kind: this.kind(), sort: this.sort(), page: this.page() }),
  );

  /** What's in the search box; it reaches the URL a moment after typing stops. */
  protected readonly searchText = signal('');
  private searchTimer: ReturnType<typeof setTimeout> | undefined;

  protected readonly programs = toSignal(
    toObservable(this.state).pipe(switchMap((state) => this.programsService.browse(browseQueryFor(state)).pipe(toLoadState()))),
    { initialValue: { status: 'loading' } as LoadState<PagedResult<ProgramCard>> },
  );

  protected readonly cards = computed(() => {
    const state = this.programs();
    return state.status === 'ok' ? state.value.data : [];
  });

  protected readonly total = computed(() => {
    const state = this.programs();
    return state.status === 'ok' ? state.value.count : 0;
  });

  protected readonly pageCount = computed(() => {
    const state = this.programs();
    return state.status === 'ok' ? Math.ceil(state.value.count / state.value.pageSize) : 0;
  });

  protected readonly filtered = computed(() => {
    const s = this.state();
    return !!s.q || !!s.pricing || !!s.kind;
  });

  protected readonly errorMessage = computed(() => {
    const state = this.programs();
    return state.status === 'error' ? state.message : '';
  });

  constructor() {
    // Keep the box in step with the URL (back/forward, links), unless the user is mid-typing. Text that only differs
    // by surrounding spaces is left alone, so a space typed just before the search goes out isn't removed.
    effect(() => {
      const q = this.state().q;
      untracked(() => {
        if (this.searchTimer === undefined && this.searchText().trim() !== q) {
          this.searchText.set(q);
        }
      });
    });

    inject(DestroyRef).onDestroy(() => clearTimeout(this.searchTimer));
  }

  protected typed(text: string): void {
    this.searchText.set(text);
    clearTimeout(this.searchTimer);
    this.searchTimer = setTimeout(() => {
      this.searchTimer = undefined;
      this.update({ q: text.trim(), page: 0 }, true);
    }, 300);
  }

  protected setPricing(value: string): void {
    this.update({ pricing: (value || null) as PricingModel | null, page: 0 });
  }

  protected setKind(value: string): void {
    this.update({ kind: (value || null) as ProgramKind | null, page: 0 });
  }

  protected setSort(value: string): void {
    this.update({ sort: value as ProgramSort, page: 0 });
  }

  protected goToPage(page: number): void {
    this.update({ page });
    globalThis.scrollTo({ top: 0 });
  }

  protected clear(): void {
    clearTimeout(this.searchTimer);
    this.searchTimer = undefined;
    this.searchText.set('');
    this.update({ q: '', pricing: null, kind: null, page: 0 });
  }

  /** Typing replaces the history entry instead of adding one per pause. */
  private update(change: Partial<CatalogState>, replaceUrl = false): void {
    void this.router.navigate([], { queryParams: catalogQueryParams({ ...this.state(), ...change }), replaceUrl });
  }
}
