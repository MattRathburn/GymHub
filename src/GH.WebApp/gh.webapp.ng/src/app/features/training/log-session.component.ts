import { ChangeDetectionStrategy, Component, OnInit, computed, inject, input, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { FormArray, NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { catchError, firstValueFrom, of } from 'rxjs';
import { problemMessage } from '../../core/http/problem';
import { ProgramsService } from '../programs/programs.service';
import {
  LoggedExerciseRequest,
  LoggedSet,
  SavedSession,
  SessionRequest,
  draftFromWorkout,
  emptySet,
  hasAnyValue,
  localToday,
  recordLabel,
} from './training.model';
import { TrainingService } from './training.service';

/**
 * Logs a workout: freestyle, from a released program workout (pre-filled with the planned sets), or edits a logged
 * session. Program exercises keep the program's name.
 */
@Component({
  selector: 'app-log-session',
  imports: [ReactiveFormsModule, RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="mx-auto max-w-3xl px-4 py-8">
      <a routerLink="/me/training" class="text-sm text-indigo-600 hover:underline">← Training log</a>
      <h1 class="mt-2 text-2xl font-semibold">{{ id() ? 'Edit workout' : 'Log a workout' }}</h1>
      @if (context(); as c) {
        <p class="mt-1 text-sm text-slate-500">{{ c }}</p>
      }

      @if (loadError(); as message) {
        <p class="mt-6 text-rose-600">{{ message }}</p>
      } @else if (loading()) {
        <p class="mt-6 text-slate-500">Loading…</p>
      } @else if (saved(); as s) {
        <div class="card mt-6">
          <p class="font-semibold text-emerald-700">Saved.</p>
          @if (s.records.length > 0) {
            <ul class="mt-3 space-y-1 text-sm">
              @for (r of s.records; track r.exerciseKey + r.kind) {
                <li>
                  <span class="mr-2 rounded bg-amber-100 px-1.5 py-0.5 text-xs font-semibold text-amber-800">PR</span>
                  {{ r.name }}: {{ recordLabel(r.kind) }} {{ r.value }} kg <span class="text-slate-500">(was {{ r.previous }} kg)</span>
                </li>
              }
            </ul>
          }
          <div class="mt-4 flex flex-wrap gap-2">
            <a class="btn-primary" [routerLink]="['/me/training/sessions', s.id]">View workout</a>
            <a class="btn-secondary" routerLink="/me/training">Training log</a>
          </div>
        </div>
      } @else {
        <form class="mt-6 space-y-6" [formGroup]="form" (ngSubmit)="save()">
          <div class="grid gap-3 sm:grid-cols-3">
            <label class="block text-sm sm:col-span-2">
              <span class="label">Title</span>
              <input class="input" formControlName="title" placeholder="Workout" />
            </label>
            <label class="block text-sm">
              <span class="label">Date</span>
              <input class="input" type="date" formControlName="performedOn" />
            </label>
            <label class="block text-sm">
              <span class="label">Duration (min)</span>
              <input class="input" type="number" min="1" formControlName="durationMin" />
            </label>
            <label class="block text-sm sm:col-span-2">
              <span class="label">Notes</span>
              <input class="input" formControlName="notes" placeholder="How did it go?" />
            </label>
          </div>

          <datalist id="known-exercises">
            @for (name of knownExercises(); track name) {
              <option [value]="name"></option>
            }
          </datalist>

          <div formArrayName="exercises" class="space-y-4">
            @for (exercise of exercises.controls; track exercise; let i = $index) {
              <div class="card" [formGroupName]="i">
                <div class="flex items-center gap-2">
                  <input
                    class="input flex-1 font-medium"
                    formControlName="name"
                    list="known-exercises"
                    placeholder="Exercise"
                    [attr.aria-label]="'Exercise ' + (i + 1)"
                    [readOnly]="!!exercise.controls.programExerciseId.value"
                  />
                  <button type="button" class="text-sm text-rose-600 hover:underline" (click)="removeExercise(i)">Remove</button>
                </div>

                <div class="mt-3 grid grid-cols-[2rem_repeat(5,minmax(0,1fr))_1.5rem] gap-1 text-xs text-slate-500">
                  <span>Set</span><span>Reps</span><span>kg</span><span>Sec</span><span>Metres</span><span>RPE</span><span></span>
                </div>
                <div formArrayName="sets" class="space-y-1">
                  @for (set of exercise.controls.sets.controls; track set; let j = $index) {
                    <div class="grid grid-cols-[2rem_repeat(5,minmax(0,1fr))_1.5rem] items-center gap-1" [formGroupName]="j">
                      <span class="text-sm text-slate-500">{{ j + 1 }}</span>
                      <input class="input px-2" type="number" min="1" formControlName="reps" [attr.aria-label]="'Set ' + (j + 1) + ' reps'" />
                      <input class="input px-2" type="number" min="0" step="0.5" formControlName="weightKg" [attr.aria-label]="'Set ' + (j + 1) + ' weight'" />
                      <input class="input px-2" type="number" min="1" formControlName="durationSec" [attr.aria-label]="'Set ' + (j + 1) + ' seconds'" />
                      <input class="input px-2" type="number" min="1" formControlName="distanceM" [attr.aria-label]="'Set ' + (j + 1) + ' metres'" />
                      <input class="input px-2" type="number" min="1" max="10" step="0.5" formControlName="rpe" [attr.aria-label]="'Set ' + (j + 1) + ' RPE'" />
                      <button type="button" class="text-rose-600" (click)="removeSet(i, j)" aria-label="Remove set">×</button>
                    </div>
                  }
                </div>
                <button type="button" class="mt-2 text-sm text-indigo-600 hover:underline" (click)="addSet(i)">+ Add set</button>
              </div>
            }
          </div>

          <div class="flex flex-wrap items-center gap-3">
            <button type="button" class="btn-secondary" (click)="addExercise()">Add exercise</button>
            <button class="btn-primary" type="submit" [disabled]="busy() || form.invalid">Save workout</button>
            @if (error(); as message) {
              <span class="text-sm text-rose-600">{{ message }}</span>
            }
          </div>
        </form>
      }
    </section>
  `,
})
export class LogSessionComponent implements OnInit {
  private readonly training = inject(TrainingService);
  private readonly programs = inject(ProgramsService);
  private readonly fb = inject(NonNullableFormBuilder);

  /** Route parameter when editing a session. */
  readonly id = input<string>();

  /** Query parameters when logging a program workout. */
  readonly programId = input<string>();
  readonly workoutId = input<string>();

  protected readonly loading = signal(true);
  protected readonly loadError = signal<string | null>(null);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly saved = signal<SavedSession | null>(null);
  protected readonly context = signal<string | null>(null);
  protected readonly recordLabel = recordLabel;

  private programLink: { programId: string; workoutId: string } | null = null;

  private readonly known = toSignal(this.training.exercises().pipe(catchError(() => of([]))), { initialValue: [] });
  protected readonly knownExercises = computed(() => this.known().map((e) => e.name));

  protected readonly form = this.fb.group({
    title: ['', Validators.maxLength(200)],
    performedOn: [localToday(), Validators.required],
    durationMin: [null as number | null, [Validators.min(1), Validators.max(1440)]],
    notes: ['', Validators.maxLength(5000)],
    exercises: this.fb.array([this.exerciseGroup(this.blankExercise())]),
  });

  protected get exercises() {
    return this.form.controls.exercises;
  }

  ngOnInit(): void {
    void this.load();
  }

  protected addExercise(): void {
    this.exercises.push(this.exerciseGroup(this.blankExercise()));
  }

  protected removeExercise(index: number): void {
    this.exercises.removeAt(index);
  }

  /** A new set starts as a copy of the one before it. */
  protected addSet(exerciseIndex: number): void {
    const sets = this.exercises.at(exerciseIndex).controls.sets;
    const last = sets.length > 0 ? sets.at(sets.length - 1).getRawValue() : emptySet();
    sets.push(this.setGroup(last));
  }

  protected removeSet(exerciseIndex: number, setIndex: number): void {
    this.exercises.at(exerciseIndex).controls.sets.removeAt(setIndex);
  }

  protected async save(): Promise<void> {
    const request = this.toRequest();
    if (typeof request === 'string') {
      this.error.set(request);
      return;
    }

    this.busy.set(true);
    this.error.set(null);
    try {
      const id = this.id();
      this.saved.set(await firstValueFrom(id ? this.training.update(id, request) : this.training.log(request)));
    } catch (error) {
      this.error.set(problemMessage(error));
    } finally {
      this.busy.set(false);
    }
  }

  /** The request, or what's missing. Sets left empty are dropped. */
  private toRequest(): SessionRequest | string {
    const v = this.form.getRawValue();
    const exercises: LoggedExerciseRequest[] = [];

    for (const e of v.exercises) {
      const sets = e.sets.filter(hasAnyValue);
      if (sets.length === 0) {
        return `Add at least one set to ${e.name || 'each exercise'}.`;
      }

      exercises.push({ name: e.name.trim(), programExerciseId: e.programExerciseId, notes: e.notes || null, sets });
    }

    if (exercises.length === 0) {
      return 'Add at least one exercise.';
    }

    return {
      performedOn: v.performedOn,
      title: v.title.trim() || null,
      notes: v.notes.trim() || null,
      durationMin: v.durationMin,
      programId: this.programLink?.programId ?? null,
      programWorkoutId: this.programLink?.workoutId ?? null,
      exercises,
    };
  }

  private async load(): Promise<void> {
    try {
      const id = this.id();
      const programId = this.programId();
      const workoutId = this.workoutId();

      if (id) {
        const session = await firstValueFrom(this.training.session(id));
        this.form.patchValue({
          title: session.title,
          performedOn: session.performedOn,
          durationMin: session.durationMin,
          notes: session.notes ?? '',
        });
        this.setExercises(session.exercises);
        if (session.programTitle) {
          this.context.set(`${session.programTitle} · Week ${session.weekNumber}`);
        }
      } else if (programId && workoutId) {
        const program = await firstValueFrom(this.programs.content(programId));
        const found = program.weeks
          .flatMap((week) => week.days.flatMap((day) => day.workouts.map((workout) => ({ week, day, workout }))))
          .find((x) => x.workout.id === workoutId);

        if (!found) {
          this.loadError.set("That workout isn't released yet, or is no longer in the program.");
          return;
        }

        this.programLink = { programId, workoutId };
        this.context.set(`${program.title} · Week ${found.week.number} · Day ${found.day.number}`);
        this.form.patchValue({ title: found.workout.title });
        this.setExercises(draftFromWorkout(found.workout));
      }
    } catch (error) {
      this.loadError.set(problemMessage(error));
    } finally {
      this.loading.set(false);
    }
  }

  private setExercises(exercises: LoggedExerciseRequest[]): void {
    this.exercises.clear();
    for (const exercise of exercises) {
      this.exercises.push(this.exerciseGroup(exercise));
    }
  }

  private blankExercise(): LoggedExerciseRequest {
    return { name: '', programExerciseId: null, notes: null, sets: [emptySet()] };
  }

  private exerciseGroup(exercise: LoggedExerciseRequest) {
    return this.fb.group({
      name: [exercise.name, [Validators.required, Validators.maxLength(200)]],
      programExerciseId: [exercise.programExerciseId],
      notes: [exercise.notes ?? ''],
      sets: this.fb.array(exercise.sets.map((s) => this.setGroup(s))) as FormArray<ReturnType<LogSessionComponent['setGroup']>>,
    });
  }

  private setGroup(set: LoggedSet) {
    return this.fb.group({
      reps: [set.reps, [Validators.min(1), Validators.max(1000)]],
      weightKg: [set.weightKg, [Validators.min(0), Validators.max(1000)]],
      durationSec: [set.durationSec, [Validators.min(1), Validators.max(86400)]],
      distanceM: [set.distanceM, [Validators.min(1), Validators.max(1000000)]],
      rpe: [set.rpe, [Validators.min(1), Validators.max(10)]],
    });
  }
}
