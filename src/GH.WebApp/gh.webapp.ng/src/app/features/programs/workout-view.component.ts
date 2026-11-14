import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ExerciseSet, Week } from './program.model';

/**
 * Read-only rendering of weeks → days → workouts → exercises, shared by the subscriber view and the editor. With
 * logProgramId set, each workout gets a link to log it, and workouts in doneWorkoutIds are marked done.
 */
@Component({
  selector: 'app-workout-view',
  imports: [RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="space-y-4">
      @for (day of week().days; track day.id) {
        <div class="rounded-md border border-slate-200 p-3">
          <h4 class="text-sm font-semibold">
            Day {{ day.number }}@if (day.title) {: {{ day.title }}}
            @if (day.isRestDay) {
              <span class="ml-2 rounded bg-emerald-50 px-1.5 py-0.5 text-xs text-emerald-700">Rest</span>
            }
          </h4>
          @if (day.notes) {
            <p class="mt-1 text-sm text-slate-600">{{ day.notes }}</p>
          }
          @for (workout of day.workouts; track workout.id) {
            <div class="mt-3">
              <div class="flex flex-wrap items-center justify-between gap-2">
                <p class="font-medium">
                  {{ workout.title }}
                  @if (doneWorkoutIds().has(workout.id)) {
                    <span class="ml-2 rounded bg-emerald-50 px-1.5 py-0.5 text-xs text-emerald-700">✓ Done</span>
                  }
                </p>
                @if (logProgramId(); as programId) {
                  <a
                    class="text-sm text-indigo-600 hover:underline"
                    routerLink="/me/training/log"
                    [queryParams]="{ programId, workoutId: workout.id }"
                  >
                    Log workout
                  </a>
                }
              </div>
              @if (workout.notes) {
                <p class="text-sm text-slate-600">{{ workout.notes }}</p>
              }
              <ul class="mt-1 space-y-1 text-sm">
                @for (exercise of workout.exercises; track exercise.id) {
                  <li>
                    <span class="font-medium">{{ exercise.name }}</span>
                    <span class="text-slate-600"> · {{ describeSets(exercise.sets) }}</span>
                    @if (exercise.notes) {
                      <span class="block text-xs text-slate-500">{{ exercise.notes }}</span>
                    }
                  </li>
                }
              </ul>
              @for (media of workout.media; track media.id) {
                @if (media.kind === 'Video') {
                  <video [src]="media.url" controls class="mt-2 max-h-64 rounded"></video>
                } @else {
                  <img [src]="media.url" [alt]="media.caption ?? ''" class="mt-2 max-h-64 rounded" />
                }
              }
            </div>
          }
        </div>
      } @empty {
        <p class="text-sm text-slate-500">No days added yet.</p>
      }
    </div>
  `,
})
export class WorkoutViewComponent {
  readonly week = input.required<Week>();
  readonly logProgramId = input<string | null>(null);
  readonly doneWorkoutIds = input<ReadonlySet<string>>(new Set<string>());

  protected describeSets(sets: ExerciseSet[]): string {
    if (sets.length === 0) {
      return 'no sets';
    }

    const describe = (s: ExerciseSet) =>
      [
        s.reps !== null ? `${s.reps} reps` : null,
        s.weightKg !== null ? `${s.weightKg} kg` : null,
        s.durationSec !== null ? `${s.durationSec}s` : null,
        s.distanceM !== null ? `${s.distanceM} m` : null,
      ]
        .filter(Boolean)
        .join(' @ ');

    const first = describe(sets[0]);
    return sets.every((s) => describe(s) === first) ? `${sets.length} × ${first}` : sets.map(describe).join(', ');
  }
}
