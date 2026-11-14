import { DecimalPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject, input, signal } from '@angular/core';
import { toObservable, toSignal } from '@angular/core/rxjs-interop';
import { Router, RouterLink } from '@angular/router';
import { firstValueFrom, switchMap } from 'rxjs';
import { LoadState, toLoadState } from '../../core/http/load-state';
import { problemMessage } from '../../core/http/problem';
import { Session, describeSet } from './training.model';
import { TrainingService } from './training.service';

/** One logged workout, with edit and delete. */
@Component({
  selector: 'app-session-detail',
  imports: [DecimalPipe, RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="mx-auto max-w-3xl px-4 py-8">
      <a routerLink="/me/training" class="text-sm text-indigo-600 hover:underline">← Training log</a>

      @switch (state().status) {
        @case ('loading') {
          <p class="mt-6 text-slate-500">Loading…</p>
        }
        @case ('error') {
          <p class="mt-6 text-rose-600">This workout isn't in your log.</p>
        }
      }

      @if (session(); as s) {
        <div class="mt-2 flex flex-wrap items-start justify-between gap-3">
          <div>
            <h1 class="text-2xl font-semibold">{{ s.title }}</h1>
            <p class="text-sm text-slate-500">
              {{ s.performedOn }}
              @if (s.durationMin) {
                · {{ s.durationMin }} min
              }
            </p>
            @if (s.programId) {
              <a [routerLink]="['/programs', s.programId]" class="text-sm text-indigo-600 hover:underline">{{ s.programTitle }} · Week {{ s.weekNumber }}</a>
            }
          </div>
          <div class="flex gap-2">
            <a class="btn-secondary" [routerLink]="['/me/training/sessions', s.id, 'edit']">Edit</a>
            @if (confirming()) {
              <button class="btn-danger" [disabled]="busy()" (click)="remove(s.id)">Delete for good</button>
              <button class="btn-secondary" (click)="confirming.set(false)">Keep</button>
            } @else {
              <button class="btn-danger" (click)="confirming.set(true)">Delete</button>
            }
          </div>
        </div>
        @if (error(); as message) {
          <p class="mt-2 text-sm text-rose-600">{{ message }}</p>
        }
        @if (s.notes) {
          <p class="mt-4 whitespace-pre-line text-slate-700">{{ s.notes }}</p>
        }

        <ul class="mt-6 space-y-3">
          @for (e of s.exercises; track e.id) {
            <li class="card">
              <div class="flex flex-wrap items-baseline justify-between gap-2">
                <a [routerLink]="['/me/training/exercises', e.exerciseKey]" class="font-semibold hover:text-indigo-700">{{ e.name }}</a>
                <span class="text-xs text-slate-500">
                  @if (e.bestE1RmKg !== null) {
                    e1RM {{ e.bestE1RmKg }} kg ·
                  }
                  @if (e.volumeKg > 0) {
                    {{ e.volumeKg | number: '1.0-0' }} kg volume
                  }
                </span>
              </div>
              <ol class="mt-2 space-y-0.5 text-sm text-slate-700">
                @for (set of e.sets; track $index) {
                  <li><span class="inline-block w-12 text-slate-400">Set {{ $index + 1 }}</span>{{ describeSet(set) }}</li>
                }
              </ol>
              @if (e.notes) {
                <p class="mt-1 text-xs text-slate-500">{{ e.notes }}</p>
              }
            </li>
          }
        </ul>
      }
    </section>
  `,
})
export class SessionDetailComponent {
  private readonly training = inject(TrainingService);
  private readonly router = inject(Router);

  /** Route parameter, bound by withComponentInputBinding. */
  readonly id = input.required<string>();

  protected readonly confirming = signal(false);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly describeSet = describeSet;

  protected readonly state = toSignal(
    toObservable(this.id).pipe(switchMap((id) => this.training.session(id).pipe(toLoadState()))),
    { initialValue: { status: 'loading' } as LoadState<Session> },
  );

  protected readonly session = computed(() => {
    const state = this.state();
    return state.status === 'ok' ? state.value : null;
  });

  protected async remove(id: string): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    try {
      await firstValueFrom(this.training.delete(id));
      await this.router.navigateByUrl('/me/training');
    } catch (error) {
      this.error.set(problemMessage(error));
      this.busy.set(false);
    }
  }
}
