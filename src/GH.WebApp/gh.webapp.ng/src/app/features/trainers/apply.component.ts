import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { AuthService } from '../../core/auth/auth.service';
import { problemMessage } from '../../core/http/problem';
import { TrainerApplication, TrainerDetailsRequest } from './trainer.model';
import { TrainerDetailsFormComponent } from './trainer-details-form.component';
import { TrainersService } from './trainers.service';

type ApplyState =
  | { status: 'loading' }
  | { status: 'error'; message: string }
  | { status: 'ok'; application: TrainerApplication | null };

/** Apply to become a trainer, or see where your application stands. */
@Component({
  selector: 'app-apply',
  imports: [RouterLink, TrainerDetailsFormComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="mx-auto max-w-2xl px-4 py-8">
      <h1 class="text-2xl font-semibold">Become a trainer</h1>

      @switch (state().status) {
        @case ('loading') {
          <p class="mt-6 text-slate-500">Loading…</p>
        }
        @case ('error') {
          <p class="mt-6 text-rose-600">{{ errorText() }}</p>
        }
      }

      @if (state().status === 'ok') {
        @if (auth.isTrainer()) {
          <div class="card mt-6">
            <p>You're a trainer. <a routerLink="/studio" class="text-indigo-600 underline">Open the Trainer studio</a></p>
          </div>
        } @else {
          @switch (application()?.status) {
            @case ('Pending') {
              <div class="card mt-6">
                <p class="font-medium">Your application is waiting for review.</p>
                <p class="mt-1 text-sm text-slate-500">Submitted {{ application()!.submittedAt.slice(0, 10) }} as {{ application()!.displayName }}.</p>
              </div>
            }
            @case ('Approved') {
              <div class="card mt-6">
                <p class="font-medium text-emerald-700">You're approved!</p>
                <p class="mt-1 text-sm text-slate-600">Sign out and sign in again to open the Trainer studio.</p>
                <button class="btn-primary mt-3" (click)="auth.logout()">Sign out</button>
              </div>
            }
            @default {
              @if (application()?.status === 'Rejected') {
                <div class="card mt-6 border-rose-200">
                  <p class="font-medium">Your last application wasn't approved.</p>
                  <p class="mt-1 text-sm text-slate-600">{{ application()!.rejectionReason }}</p>
                  <p class="mt-1 text-sm text-slate-500">You're welcome to apply again.</p>
                </div>
              } @else {
                <p class="mt-2 text-slate-600">
                  Tell us about your coaching. An admin reviews every application before you can publish programs.
                </p>
              }
              <div class="mt-6">
                <app-trainer-details-form
                  [initial]="initial()"
                  submitLabel="Submit application"
                  [busy]="busy()"
                  (save)="submit($event)"
                />
              </div>
              @if (submitError(); as message) {
                <p class="mt-2 text-sm text-rose-600">{{ message }}</p>
              }
            }
          }
        }
      }
    </section>
  `,
})
export class ApplyComponent {
  private readonly trainers = inject(TrainersService);
  protected readonly auth = inject(AuthService);

  protected readonly state = signal<ApplyState>({ status: 'loading' });
  protected readonly busy = signal(false);
  protected readonly submitError = signal<string | null>(null);

  protected readonly application = computed(() => {
    const state = this.state();
    return state.status === 'ok' ? state.application : null;
  });

  /** Prefill from a rejected application, else the signed-in name. */
  protected readonly initial = computed<Partial<TrainerDetailsRequest>>(
    () => this.application() ?? { displayName: this.auth.user()?.name ?? '' },
  );

  protected readonly errorText = computed(() => {
    const state = this.state();
    return state.status === 'error' ? state.message : '';
  });

  constructor() {
    this.trainers.myApplication().subscribe({
      next: (application) => this.state.set({ status: 'ok', application }),
      error: (error: unknown) =>
        this.state.set(
          error instanceof HttpErrorResponse && error.status === 404
            ? { status: 'ok', application: null }
            : { status: 'error', message: problemMessage(error) },
        ),
    });
  }

  protected async submit(request: TrainerDetailsRequest): Promise<void> {
    this.busy.set(true);
    this.submitError.set(null);
    try {
      const application = await firstValueFrom(this.trainers.apply(request));
      this.state.set({ status: 'ok', application });
    } catch (error) {
      this.submitError.set(problemMessage(error));
    } finally {
      this.busy.set(false);
    }
  }
}
