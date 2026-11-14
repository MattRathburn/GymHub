import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { LoadState, toLoadState } from '../../core/http/load-state';
import { TrainingNavComponent } from './training-nav.component';
import { ExerciseSummary } from './training.model';
import { TrainingService } from './training.service';

/** Every exercise the member has logged, with their records. */
@Component({
  selector: 'app-training-exercises',
  imports: [RouterLink, TrainingNavComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="mx-auto max-w-3xl px-4 py-8">
      <app-training-nav />

      @switch (state().status) {
        @case ('loading') {
          <p class="mt-6 text-slate-500">Loading…</p>
        }
        @case ('error') {
          <p class="mt-6 text-rose-600">Couldn't load your exercises.</p>
        }
        @case ('ok') {
          <div class="mt-6 overflow-x-auto rounded-lg border border-slate-200 bg-white">
            <table class="w-full text-sm">
              <thead class="text-left text-xs text-slate-500">
                <tr>
                  <th class="px-4 py-2 font-medium">Exercise</th>
                  <th class="px-4 py-2 font-medium">Last done</th>
                  <th class="px-4 py-2 text-right font-medium">Workouts</th>
                  <th class="px-4 py-2 text-right font-medium">Heaviest</th>
                  <th class="px-4 py-2 text-right font-medium">Best e1RM</th>
                </tr>
              </thead>
              <tbody class="divide-y divide-slate-100">
                @for (e of exercises(); track e.key) {
                  <tr>
                    <td class="px-4 py-2">
                      <a [routerLink]="['/me/training/exercises', e.key]" class="font-medium text-indigo-700 hover:underline">{{ e.name }}</a>
                    </td>
                    <td class="px-4 py-2 text-slate-600">{{ e.lastPerformedOn }}</td>
                    <td class="px-4 py-2 text-right">{{ e.sessionCount }}</td>
                    <td class="px-4 py-2 text-right">{{ e.bestWeightKg !== null ? e.bestWeightKg + ' kg' : '–' }}</td>
                    <td class="px-4 py-2 text-right">{{ e.bestE1RmKg !== null ? e.bestE1RmKg + ' kg' : '–' }}</td>
                  </tr>
                } @empty {
                  <tr>
                    <td colspan="5" class="px-4 py-8 text-center text-slate-500">Exercises you log show up here.</td>
                  </tr>
                }
              </tbody>
            </table>
          </div>
        }
      }
    </section>
  `,
})
export class ExercisesComponent {
  private readonly training = inject(TrainingService);

  protected readonly state = toSignal(this.training.exercises().pipe(toLoadState()), {
    initialValue: { status: 'loading' } as LoadState<ExerciseSummary[]>,
  });

  protected exercises(): ExerciseSummary[] {
    const state = this.state();
    return state.status === 'ok' ? state.value : [];
  }
}
