import { DecimalPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject, input, signal } from '@angular/core';
import { toObservable, toSignal } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { switchMap } from 'rxjs';
import { LoadState, toLoadState } from '../../core/http/load-state';
import { ExerciseProgress, ProgressMetric, chartPoints, metricLabels, recordLabel } from './training.model';
import { TrainingService } from './training.service';

const width = 600;
const height = 200;

/** One exercise over time: a line chart of the chosen metric, and the personal records it took to get there. */
@Component({
  selector: 'app-exercise-progress',
  imports: [DecimalPipe, RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="mx-auto max-w-3xl px-4 py-8">
      <a routerLink="/me/training/exercises" class="text-sm text-indigo-600 hover:underline">← Exercises</a>

      @switch (state().status) {
        @case ('loading') {
          <p class="mt-6 text-slate-500">Loading…</p>
        }
        @case ('error') {
          <p class="mt-6 text-rose-600">You haven't logged this exercise yet.</p>
        }
      }

      @if (progress(); as p) {
        <h1 class="mt-2 text-2xl font-semibold">{{ p.name }}</h1>
        <div class="mt-4 grid grid-cols-3 gap-3 text-center">
          <div class="card">
            <p class="text-xs text-slate-500">Heaviest</p>
            <p class="text-lg font-semibold">{{ p.bestWeightKg !== null ? p.bestWeightKg + ' kg' : '–' }}</p>
          </div>
          <div class="card">
            <p class="text-xs text-slate-500">Best e1RM</p>
            <p class="text-lg font-semibold">{{ p.bestE1RmKg !== null ? p.bestE1RmKg + ' kg' : '–' }}</p>
          </div>
          <div class="card">
            <p class="text-xs text-slate-500">Workouts</p>
            <p class="text-lg font-semibold">{{ p.points.length }}</p>
          </div>
        </div>

        <div class="card mt-6">
          <div class="flex flex-wrap gap-2 text-sm" role="group" aria-label="Metric">
            @for (m of metrics; track m) {
              <button
                type="button"
                class="rounded-full border px-3 py-1"
                [class]="metric() === m ? 'border-indigo-600 bg-indigo-50 text-indigo-700' : 'border-slate-200 text-slate-600'"
                [attr.aria-pressed]="metric() === m"
                (click)="metric.set(m)"
              >
                {{ metricLabels[m] }}
              </button>
            }
          </div>

          @if (chart().length === 0) {
            <p class="mt-6 text-sm text-slate-500">No {{ metricLabels[metric()].toLowerCase() }} logged for this exercise.</p>
          } @else {
            <svg [attr.viewBox]="'-12 -12 ' + (width + 24) + ' ' + (height + 24)" class="mt-4 h-56 w-full" role="img" [attr.aria-label]="metricLabels[metric()] + ' over time'">
              <line x1="0" [attr.y1]="height" [attr.x2]="width" [attr.y2]="height" class="stroke-slate-200" />
              <polyline [attr.points]="polyline()" fill="none" class="stroke-indigo-600" stroke-width="2" stroke-linejoin="round" />
              @for (pt of chart(); track $index) {
                <circle [attr.cx]="pt.x" [attr.cy]="pt.y" r="4" class="fill-indigo-600">
                  <title>{{ pt.date }}: {{ pt.value | number: '1.0-1' }} kg</title>
                </circle>
              }
            </svg>
            <div class="flex justify-between text-xs text-slate-500">
              <span>{{ chart()[0].date }}</span>
              <span>{{ chart()[chart().length - 1].date }}</span>
            </div>
          }
        </div>

        <h2 class="mt-8 text-lg font-semibold">Personal records</h2>
        <ul class="mt-2 divide-y divide-slate-100 rounded-lg border border-slate-200 bg-white text-sm">
          @for (r of recordsNewestFirst(); track r.sessionId + r.kind) {
            <li class="flex flex-wrap items-center justify-between gap-2 px-4 py-2">
              <span>
                <span class="mr-2 rounded bg-amber-100 px-1.5 py-0.5 text-xs font-semibold text-amber-800">PR</span>
                {{ recordLabel(r.kind) }} {{ r.value }} kg <span class="text-slate-500">(was {{ r.previous }} kg)</span>
              </span>
              <a [routerLink]="['/me/training/sessions', r.sessionId]" class="text-slate-500 hover:underline">{{ r.performedOn }}</a>
            </li>
          } @empty {
            <li class="px-4 py-3 text-slate-500">Beat your best and your records show up here.</li>
          }
        </ul>
      }
    </section>
  `,
})
export class ExerciseProgressComponent {
  private readonly training = inject(TrainingService);

  /** Route parameter, bound by withComponentInputBinding. */
  readonly key = input.required<string>();

  protected readonly width = width;
  protected readonly height = height;
  protected readonly metrics: ProgressMetric[] = ['bestE1RmKg', 'topWeightKg', 'volumeKg'];
  protected readonly metricLabels = metricLabels;
  protected readonly recordLabel = recordLabel;
  protected readonly metric = signal<ProgressMetric>('bestE1RmKg');

  protected readonly state = toSignal(
    toObservable(this.key).pipe(switchMap((key) => this.training.progress(key).pipe(toLoadState()))),
    { initialValue: { status: 'loading' } as LoadState<ExerciseProgress> },
  );

  protected readonly progress = computed(() => {
    const state = this.state();
    return state.status === 'ok' ? state.value : null;
  });

  protected readonly chart = computed(() => chartPoints(this.progress()?.points ?? [], this.metric(), width, height));
  protected readonly polyline = computed(() => this.chart().map((p) => `${p.x},${p.y}`).join(' '));
  protected readonly recordsNewestFirst = computed(() => [...(this.progress()?.records ?? [])].reverse());
}
