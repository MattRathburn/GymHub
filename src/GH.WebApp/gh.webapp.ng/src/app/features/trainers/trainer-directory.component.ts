import { ChangeDetectionStrategy, Component, DestroyRef, computed, effect, inject, input, signal, untracked } from '@angular/core';
import { toObservable, toSignal } from '@angular/core/rxjs-interop';
import { Router } from '@angular/router';
import { switchMap } from 'rxjs';
import { LoadState, toLoadState } from '../../core/http/load-state';
import { PagedResult } from '../programs/program.model';
import { TrainerCardComponent } from './trainer-card.component';
import { TrainerCard } from './trainer.model';
import { TrainersService } from './trainers.service';

/** The trainer directory: trainers with a published program, most followed first. */
@Component({
  selector: 'app-trainer-directory',
  imports: [TrainerCardComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="mx-auto max-w-6xl px-4 py-8">
      <h1 class="text-2xl font-semibold">Trainers</h1>
      <p class="mt-1 text-slate-600">Coaches publishing programs on GymHub.</p>

      <input
        class="input mt-4 max-w-sm"
        type="search"
        placeholder="Search by name, bio or specialty"
        aria-label="Search trainers"
        [value]="searchText()"
        (input)="typed($any($event.target).value)"
      />

      @switch (trainers().status) {
        @case ('loading') {
          <p class="mt-8 text-slate-500">Loading trainers…</p>
        }
        @case ('error') {
          <p class="mt-8 text-rose-600">Couldn't load trainers.</p>
        }
        @case ('ok') {
          <ul class="mt-6 grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
            @for (trainer of cards(); track trainer.userId) {
              <li><app-trainer-card [trainer]="trainer" /></li>
            } @empty {
              <li class="text-slate-500">No trainers match.</li>
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
    </section>
  `,
})
export class TrainerDirectoryComponent {
  private readonly trainersService = inject(TrainersService);
  private readonly router = inject(Router);

  // Query parameters (?q=&page=, page 1-based), bound by withComponentInputBinding, so back/forward and links keep them.
  readonly q = input<string>();
  readonly page = input<string>();

  protected readonly state = computed(() => {
    const page = Number.parseInt(this.page() ?? '', 10);
    return { q: this.q()?.trim() ?? '', page: Number.isFinite(page) && page > 1 ? page - 1 : 0 };
  });

  /** What's in the search box; it reaches the URL a moment after typing stops. */
  protected readonly searchText = signal('');
  private searchTimer: ReturnType<typeof setTimeout> | undefined;

  protected readonly trainers = toSignal(
    toObservable(this.state).pipe(switchMap(({ q, page }) => this.trainersService.directory(q, page).pipe(toLoadState()))),
    { initialValue: { status: 'loading' } as LoadState<PagedResult<TrainerCard>> },
  );

  constructor() {
    // Keep the box in step with the URL unless the user is mid-typing (same rule as the program catalog).
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
      this.update(text.trim(), 0, true);
    }, 250);
  }

  protected goToPage(page: number): void {
    this.update(this.state().q, page);
    globalThis.scrollTo({ top: 0 });
  }

  /** Typing replaces the history entry instead of adding one per pause. */
  private update(q: string, page: number, replaceUrl = false): void {
    void this.router.navigate([], { queryParams: { q: q || null, page: page > 0 ? String(page + 1) : null }, replaceUrl });
  }

  protected readonly cards = computed(() => {
    const state = this.trainers();
    return state.status === 'ok' ? state.value.data : [];
  });

  protected readonly pageCount = computed(() => {
    const state = this.trainers();
    return state.status === 'ok' ? Math.ceil(state.value.count / state.value.pageSize) : 0;
  });
}
