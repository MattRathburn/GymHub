import { DecimalPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject, input, signal } from '@angular/core';
import { toObservable, toSignal } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { switchMap } from 'rxjs';
import { LoadState, toLoadState } from '../../core/http/load-state';
import { PagedResult } from '../programs/program.model';
import { TrainingNavComponent } from './training-nav.component';
import { SessionSummary } from './training.model';
import { TrainingService } from './training.service';

const pageSize = 20;

/** The member's logged workouts, newest first. ?programId= narrows it to one program. */
@Component({
  selector: 'app-training-history',
  imports: [DecimalPipe, RouterLink, TrainingNavComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="mx-auto max-w-3xl px-4 py-8">
      <app-training-nav />

      @if (programId()) {
        <p class="mt-4 text-sm text-slate-600">
          Showing workouts from {{ programTitle() ?? 'one program' }} ·
          <a routerLink="/me/training" class="text-indigo-600 hover:underline">Show all</a>
        </p>
      }

      @switch (page().status) {
        @case ('loading') {
          <p class="mt-6 text-slate-500">Loading…</p>
        }
        @case ('error') {
          <p class="mt-6 text-rose-600">Couldn't load your training log.</p>
        }
      }

      @if (result(); as r) {
        <ul class="mt-6 space-y-3">
          @for (s of r.data; track s.id) {
            <li>
              <a [routerLink]="['/me/training/sessions', s.id]" class="card block hover:border-indigo-300">
                <div class="flex flex-wrap items-baseline justify-between gap-2">
                  <span class="font-semibold">{{ s.title }}</span>
                  <span class="text-sm text-slate-500">{{ s.performedOn }}</span>
                </div>
                @if (s.programTitle) {
                  <p class="text-xs text-indigo-700">{{ s.programTitle }} · Week {{ s.weekNumber }}</p>
                }
                <p class="mt-1 text-sm text-slate-600">
                  {{ s.exerciseNames.join(', ') }}@if (s.exerciseCount > s.exerciseNames.length) {, +{{ s.exerciseCount - s.exerciseNames.length }} more}
                </p>
                <p class="mt-1 text-xs text-slate-500">
                  {{ s.setCount }} sets
                  @if (s.volumeKg > 0) {
                    · {{ s.volumeKg | number: '1.0-0' }} kg volume
                  }
                  @if (s.durationMin) {
                    · {{ s.durationMin }} min
                  }
                </p>
              </a>
            </li>
          } @empty {
            <li class="card text-center text-slate-500">
              No workouts logged yet. <a routerLink="/me/training/log" class="text-indigo-600 hover:underline">Log your first one.</a>
            </li>
          }
        </ul>

        @if (pageCount() > 1) {
          <div class="mt-4 flex items-center gap-3 text-sm">
            <button class="btn-secondary" [disabled]="pageIndex() === 0" (click)="pageIndex.set(pageIndex() - 1)">Newer</button>
            <span class="text-slate-500">Page {{ pageIndex() + 1 }} of {{ pageCount() }}</span>
            <button class="btn-secondary" [disabled]="pageIndex() + 1 >= pageCount()" (click)="pageIndex.set(pageIndex() + 1)">Older</button>
          </div>
        }
      }
    </section>
  `,
})
export class HistoryComponent {
  private readonly training = inject(TrainingService);

  /** Query parameter, bound by withComponentInputBinding. */
  readonly programId = input<string>();

  protected readonly pageIndex = signal(0);

  protected readonly page = toSignal(
    toObservable(computed(() => ({ programId: this.programId(), pageIndex: this.pageIndex() }))).pipe(
      switchMap(({ programId, pageIndex }) => this.training.sessions({ programId, pageIndex, pageSize }).pipe(toLoadState())),
    ),
    { initialValue: { status: 'loading' } as LoadState<PagedResult<SessionSummary>> },
  );

  protected readonly result = computed(() => {
    const state = this.page();
    return state.status === 'ok' ? state.value : null;
  });

  protected readonly programTitle = computed(() => this.result()?.data.find((s) => s.programTitle)?.programTitle ?? null);
  protected readonly pageCount = computed(() => Math.max(1, Math.ceil((this.result()?.count ?? 0) / pageSize)));
}
