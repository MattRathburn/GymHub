import { ChangeDetectionStrategy, Component, computed, effect, inject, input, signal } from '@angular/core';
import { toObservable, toSignal } from '@angular/core/rxjs-interop';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { Observable, firstValueFrom, switchMap } from 'rxjs';
import { LoadState, toLoadState } from '../../../core/http/load-state';
import { problemMessage } from '../../../core/http/problem';
import { BillingInterval, Exercise, PricingModel, ProgramContent, ProgramKind, Week, Workout, formatPrice } from '../program.model';
import { ProgramsService } from '../programs.service';
import { WorkoutViewComponent } from '../workout-view.component';

@Component({
  selector: 'app-program-editor',
  imports: [ReactiveFormsModule, RouterLink, WorkoutViewComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './program-editor.component.html',
})
export class ProgramEditorComponent {
  private readonly programsService = inject(ProgramsService);
  private readonly fb = inject(NonNullableFormBuilder);

  /** Route parameter, bound by withComponentInputBinding. */
  readonly id = input.required<string>();

  private readonly refresh = signal(0);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly notice = signal<string | null>(null);
  protected readonly selectedWeekId = signal<string | null>(null);
  protected readonly formatPrice = formatPrice;
  protected readonly dayNumbers = [1, 2, 3, 4, 5, 6, 7];

  protected readonly state = toSignal(
    toObservable(computed(() => ({ id: this.id(), refresh: this.refresh() }))).pipe(
      switchMap(({ id }) => this.programsService.forEditing(id).pipe(toLoadState())),
    ),
    { initialValue: { status: 'loading' } as LoadState<ProgramContent> },
  );

  protected readonly program = computed(() => {
    const state = this.state();
    return state.status === 'ok' ? state.value : null;
  });

  protected readonly isArchived = computed(() => this.program()?.status === 'Archived');
  protected readonly selectedWeek = computed(() => this.program()?.weeks.find((w) => w.id === this.selectedWeekId()) ?? null);

  protected readonly details = this.fb.group({
    title: ['', [Validators.required, Validators.maxLength(200)]],
    summary: [''],
    description: [''],
    kind: ['Ongoing' as ProgramKind],
    startsOn: [''],
    endsOn: [''],
  });

  protected readonly pricing = this.fb.group({
    model: ['Free' as PricingModel],
    amount: [0],
    currency: ['USD'],
    interval: ['Monthly' as BillingInterval],
    trialDays: [0, [Validators.min(0), Validators.max(30)]],
  });

  protected readonly newWeek = this.fb.group({
    title: ['', Validators.required],
    releasesOn: ['', Validators.required],
  });

  protected readonly day = this.fb.group({
    number: [1],
    title: [''],
    isRestDay: [false],
  });

  protected readonly workout = this.fb.group({
    title: ['', Validators.required],
    notes: [''],
    exercises: this.fb.array([this.exerciseRow()]),
  });

  constructor() {
    // Reset the forms to the saved program each time it (re)loads.
    effect(() => {
      const p = this.program();
      if (!p) {
        return;
      }

      this.details.reset({
        title: p.title,
        summary: p.summary ?? '',
        description: p.description ?? '',
        kind: p.kind,
        startsOn: p.startsOn ?? '',
        endsOn: p.endsOn ?? '',
      });

      this.pricing.reset({
        model: p.price.model,
        amount: (p.price.amountMinor ?? 0) / 100,
        currency: p.price.currency ?? 'USD',
        interval: p.price.interval ?? 'Monthly',
        trialDays: p.price.trialDays ?? 0,
      });
    });
  }

  /** The workout loaded into the form for editing, or null when adding one. */
  protected readonly editingWorkout = signal<{ weekId: string; dayNumber: number; workout: Workout } | null>(null);

  protected exerciseRow(exercise?: Exercise) {
    const first = exercise?.sets[0];
    return this.fb.group({
      id: [exercise?.id ?? null],
      name: [exercise?.name ?? '', Validators.required],
      sets: [exercise ? exercise.sets.length || 1 : 3, [Validators.min(1), Validators.max(30)]],
      reps: [exercise ? (first?.reps ?? null) : (10 as number | null)],
      weightKg: [first?.weightKg ?? null],
    });
  }

  /**
   * Loads a workout into the form. Saving it keeps the workout's and exercises' ids, so members' logs keep pointing at
   * them. An exercise whose row isn't changed keeps its exact sets, even ones the simple row can't show.
   */
  protected editWorkout(week: Week, dayNumber: number, workout: Workout): void {
    this.editingWorkout.set({ weekId: week.id, dayNumber, workout });
    this.day.patchValue({ number: dayNumber });
    this.workout.reset({ title: workout.title, notes: workout.notes ?? '' });
    this.workout.controls.exercises.clear();
    for (const exercise of workout.exercises) {
      this.workout.controls.exercises.push(this.exerciseRow(exercise));
    }
  }

  protected cancelEdit(): void {
    this.editingWorkout.set(null);
    this.resetWorkoutForm();
  }

  protected addExerciseRow(): void {
    this.workout.controls.exercises.push(this.exerciseRow());
  }

  protected removeExerciseRow(index: number): void {
    this.workout.controls.exercises.removeAt(index);
  }

  protected selectWeek(week: Week): void {
    this.selectedWeekId.set(this.selectedWeekId() === week.id ? null : week.id);
  }

  protected saveDetails(): Promise<void> {
    const v = this.details.getRawValue();
    const finite = v.kind === 'Finite';

    return this.run(
      this.programsService.updateDetails(this.id(), {
        title: v.title,
        summary: v.summary || null,
        description: v.description || null,
        kind: v.kind,
        startsOn: v.startsOn || null,
        endsOn: finite ? v.endsOn || null : null,
      }),
      'Details saved.',
    );
  }

  protected savePricing(): Promise<void> {
    const v = this.pricing.getRawValue();
    const free = v.model === 'Free';

    return this.run(
      this.programsService.setPricing(this.id(), {
        model: v.model,
        amountMinor: free ? null : Math.round(v.amount * 100),
        currency: free ? null : v.currency.toUpperCase(),
        interval: v.model === 'Recurring' ? v.interval : null,
        trialDays: v.model === 'Recurring' && v.trialDays > 0 ? v.trialDays : null,
      }),
      'Pricing saved.',
    );
  }

  protected async addWeek(): Promise<void> {
    const v = this.newWeek.getRawValue();
    await this.run(this.programsService.addWeek(this.id(), v.title, v.releasesOn), 'Week added.');
    this.newWeek.reset();
  }

  protected updateWeek(week: Week, title: string, releasesOn: string): Promise<void> {
    return this.run(this.programsService.updateWeek(this.id(), week.id, title, releasesOn), 'Week saved.');
  }

  protected removeWeek(week: Week): Promise<void> {
    return this.run(this.programsService.removeWeek(this.id(), week.id), 'Week removed.');
  }

  protected saveDay(week: Week): Promise<void> {
    const v = this.day.getRawValue();
    return this.run(
      this.programsService.upsertDay(this.id(), week.id, v.number, { title: v.title || null, isRestDay: v.isRestDay, notes: null }),
      `Day ${v.number} saved.`,
    );
  }

  protected async saveWorkout(week: Week): Promise<void> {
    const v = this.workout.getRawValue();
    const editing = this.editingWorkout();
    const dayNumber = editing?.dayNumber ?? this.day.getRawValue().number;
    const original = new Map(editing?.workout.exercises.map((e) => [e.id, e]) ?? []);

    await this.run(
      this.programsService.saveWorkout(this.id(), editing?.weekId ?? week.id, dayNumber, editing?.workout.id ?? null, {
        title: v.title,
        notes: v.notes || null,
        exercises: v.exercises.map((e) => {
          const before = e.id ? original.get(e.id) : undefined;
          const unchanged = before && rowMatches(this.exerciseRow(before).getRawValue(), e);

          return {
            id: before ? before.id : null,
            name: e.name,
            notes: before?.notes ?? null,
            sets: unchanged
              ? before.sets
              : Array.from({ length: e.sets }, () => ({
                  reps: e.reps,
                  weightKg: e.weightKg,
                  durationSec: null,
                  distanceM: null,
                  restSec: null,
                  rpe: null,
                })),
          };
        }),
      }),
      editing ? 'Workout saved.' : 'Workout added.',
    );

    if (!this.error()) {
      this.editingWorkout.set(null);
      this.resetWorkoutForm();
    }
  }

  private resetWorkoutForm(): void {
    this.workout.reset();
    this.workout.controls.exercises.clear();
    this.workout.controls.exercises.push(this.exerciseRow());
  }

  protected removeWorkout(week: Week, dayNumber: number, workoutId: string): Promise<void> {
    return this.run(this.programsService.removeWorkout(this.id(), week.id, dayNumber, workoutId), 'Workout removed.');
  }

  protected async uploadCover(event: Event): Promise<void> {
    const file = (event.target as HTMLInputElement).files?.[0];
    if (!file) {
      return;
    }

    this.busy.set(true);
    this.error.set(null);
    try {
      const upload = await firstValueFrom(this.programsService.requestUpload(this.id(), file));
      await firstValueFrom(this.programsService.upload(upload, file));
      await firstValueFrom(this.programsService.attachMedia(this.id(), 'Cover', null, upload.objectKey, file.type, null));
      this.notice.set('Cover uploaded.');
      this.refresh.update((n) => n + 1);
    } catch (error) {
      this.error.set(problemMessage(error));
    } finally {
      this.busy.set(false);
    }
  }

  protected publish(): Promise<void> {
    return this.run(this.programsService.publish(this.id()), 'Published. Subscribers can find it now.');
  }

  protected archive(): Promise<void> {
    return this.run(this.programsService.archive(this.id()), 'Archived.');
  }

  private async run(request: Observable<unknown>, success: string): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    this.notice.set(null);
    try {
      await firstValueFrom(request);
      this.notice.set(success);
      this.refresh.update((n) => n + 1);
    } catch (error) {
      this.error.set(problemMessage(error));
    } finally {
      this.busy.set(false);
    }
  }
}

interface ExerciseRowValue {
  name: string;
  sets: number;
  reps: number | null;
  weightKg: number | null;
}

function rowMatches(a: ExerciseRowValue, b: ExerciseRowValue): boolean {
  return a.name === b.name && a.sets === b.sets && a.reps === b.reps && a.weightKg === b.weightKg;
}
